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

const mockStripe = { confirmCardPayment: jest.fn() };
const mockElements = { getElement: () => ({}) };
jest.mock('@stripe/react-stripe-js', () => ({ useStripe: () => mockStripe, useElements: () => mockElements, CardNumberElement: () => null }));
jest.mock('../../app/api/agent', () => ({ __esModule: true, default: { Account: { fetchAddress: jest.fn() }, Orders: { create: jest.fn() } } }));
jest.mock('./Review', () => () => <div>Order review</div>);
jest.mock('./PaymentForm', () => (props: any) => {
    const { register } = require('react-hook-form').useFormContext();
    return <><input aria-label="Name on card" {...register('nameOnCard')} />
        <button type="button" onClick={() => ['cardNumber', 'cardExpiry', 'cardCvc'].forEach(elementType =>
            props.onCardInputChange({ elementType, complete: true }))}>Complete card</button></>;
});

test('a failed order after confirmed payment keeps the basket and retries the order without paying again', async () => {
    const store = configureStore({ reducer: { account: accountSlice.reducer, basket: basketSlice.reducer,
        catalog: catalogSlice.reducer, counter: counterSlice.reducer } });
    store.dispatch(setBasket({ id: 1, buyerId: 'Alice', clientSecret: 'secret', items: [{ productId: 7, quantity: 2 }] }));
    jest.mocked(agent.Account.fetchAddress).mockResolvedValue({ fullName: 'Alice', address1: 'Street', address2: 'Unit 2', city: 'City', state: 'State', zip: '12345', country: 'Country' });
    mockStripe.confirmCardPayment.mockResolvedValue({ paymentIntent: { id: 'pi-test', status: 'succeeded' } });
    jest.mocked(agent.Orders.create).mockRejectedValueOnce(new ApiError({ message: 'Order storage unavailable', errors: {} })).mockResolvedValueOnce(42);
    render(<Provider store={store}><CheckoutPage /></Provider>);
    await waitFor(() => expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    await screen.findByText('Order review');
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
    fireEvent.change(await screen.findByLabelText('Name on card'), { target: { value: 'Alice' } });
    fireEvent.click(screen.getByRole('button', { name: 'Complete card' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Place order' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Order storage unavailable');
    expect(store.getState().basket.basket).not.toBeNull();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Place order' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Place order' }));
    await waitFor(() => expect(document.body).toHaveTextContent('Your order number is #42'));
    expect(mockStripe.confirmCardPayment).toHaveBeenCalledTimes(1);
    expect(agent.Orders.create).toHaveBeenCalledTimes(2);
    expect(store.getState().basket.basket).toBeNull();
});
