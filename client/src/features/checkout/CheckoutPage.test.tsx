import { configureStore } from '@reduxjs/toolkit';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import agent from '../../app/api/agent';
import { ApiError } from '../../app/api/contracts';
import { accountSlice } from '../account/accountSlice';
import { basketSlice, setBasket } from '../basket/basketSlice';
import { catalogSlice } from '../catalog/catalogSlice';
import { counterSlice } from '../contact/counterSlice';
import CheckoutPage from './CheckoutPage';

import PaymentResultPage from "./PaymentResultPage";
import { MemoryRouter } from "react-router-dom";

jest.mock("../../app/api/agent", () => ({ __esModule: true, default: {
    Account: { fetchAddress: jest.fn() },
    Orders: { createPending: jest.fn() },
    Payments: { startZarinpal: jest.fn(), verifyZarinpal: jest.fn() }
} }));
jest.mock("./Review", () => () => <div>Order review</div>);

test("retries the payment link for the same pending order", async () => {
    const store = configureStore({ reducer: { account: accountSlice.reducer, basket: basketSlice.reducer,
        catalog: catalogSlice.reducer, counter: counterSlice.reducer } });
    store.dispatch(setBasket({ id: 1, buyerId: "Alice", items: [{ productId: 7, quantity: 2 }] }));
    jest.mocked(agent.Account.fetchAddress).mockResolvedValue({ fullName: "Alice", address1: "Street",
        address2: "Unit 2", city: "City", state: "State", zip: "12345", country: "Country" });
    jest.mocked(agent.Orders.createPending).mockResolvedValue(42);
    const paymentUrl = "https://sandbox.zarinpal.com/pg/StartPay/S00000000000000000000000000000000000";
    jest.mocked(agent.Payments.startZarinpal).mockRejectedValueOnce(new ApiError({ message: "Gateway unavailable", errors: {} }))
        .mockResolvedValueOnce({ orderId: 42, paymentUrl });
    Object.defineProperty(window, "crypto", { configurable: true, value: { randomUUID: () => "11111111-1111-4111-8111-111111111111" } });
    const redirectToPayment = jest.fn();
    render(<Provider store={store}><CheckoutPage redirectToPayment={redirectToPayment} /></Provider>);
    await waitFor(() => expect(screen.getByRole("button", { name: "Next" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    const pay = await screen.findByRole("button", { name: "Pay with ZarinPal" });
    fireEvent.click(pay);
    expect(await screen.findByRole("alert")).toHaveTextContent("Gateway unavailable");
    fireEvent.click(pay);
    await waitFor(() => expect(redirectToPayment).toHaveBeenCalledWith(paymentUrl));
    expect(agent.Orders.createPending).toHaveBeenCalledTimes(1);
    expect(agent.Payments.startZarinpal).toHaveBeenNthCalledWith(1, 42);
    expect(agent.Payments.startZarinpal).toHaveBeenNthCalledWith(2, 42);
});

test("shows paid only when the backend confirms the return", async () => {
    jest.mocked(agent.Payments.verifyZarinpal).mockResolvedValueOnce({ orderId: 42, paid: false });
    render(<MemoryRouter initialEntries={["/checkout/result?Authority=S123&Status=OK"]}>
        <PaymentResultPage />
    </MemoryRouter>);
    expect(await screen.findByRole("alert")).toHaveTextContent("Payment was not completed. Order #42.");
    expect(agent.Payments.verifyZarinpal).toHaveBeenCalledWith("S123", "OK");
});

test("shows the confirmed order and tracking number", async () => {
    jest.mocked(agent.Payments.verifyZarinpal).mockResolvedValueOnce({ orderId: 42, paid: true, referenceId: 9876 });
    render(<MemoryRouter initialEntries={["/checkout/result?Authority=S123&Status=OK"]}>
        <PaymentResultPage />
    </MemoryRouter>);
    expect(await screen.findByRole("alert")).toHaveTextContent("Payment confirmed. Order #42. Tracking number: 9876.");
});
