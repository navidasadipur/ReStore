import { configureStore } from '@reduxjs/toolkit';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Provider } from 'react-redux';
import { MemoryRouter } from 'react-router-dom';
import agent from '../../app/api/agent';
import { ApiError } from '../../app/api/contracts';
import { accountSlice, fetchCurrentUser, signInUser, signOut } from './accountSlice';
import { basketSlice, fetchBasketAsync, removeBasketItemAsync, setBasket } from '../basket/basketSlice';
import { catalogSlice } from '../catalog/catalogSlice';
import { counterSlice } from '../contact/counterSlice';
import Login from './Login';

const mockNavigate = jest.fn();
jest.mock('react-router-dom', () => ({ ...jest.requireActual('react-router-dom'), useNavigate: () => mockNavigate }));
jest.mock('../../app/api/agent', () => ({ __esModule: true, default: {
    Account: { login: jest.fn(), currentUser: jest.fn() },
    Basket: { get: jest.fn(), removeItem: jest.fn() }, Catalog: {}
} }));

function testStore() {
    return configureStore({ reducer: { account: accountSlice.reducer, basket: basketSlice.reducer,
        catalog: catalogSlice.reducer, counter: counterSlice.reducer } });
}
const token = 'header.' + btoa(JSON.stringify({ role: ['Member', 'Admin'] })) + '.signature';
const guestBasket = { id: 1, buyerId: 'guest', items: [{ productId: 7, quantity: 2 }] };

beforeEach(() => {
    jest.clearAllMocks();
    localStorage.clear();
    document.cookie = 'buyerId=; Max-Age=0';
});

test('a failed sign-in rejects normally without a reducer throwing or storing a user', async () => {
    const store = testStore();
    jest.mocked(agent.Account.login).mockRejectedValue(new ApiError({ message: 'Invalid credentials', errors: {} }));
    await expect(store.dispatch(signInUser({ username: 'Alice', password: 'wrong' })).unwrap()).rejects.toMatchObject({ message: 'Invalid credentials' });
    expect(store.getState().account.user).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
});

test('a successful sign-in clears a stale guest basket when the account has no basket and reads JWT roles', async () => {
    const store = testStore();
    store.dispatch(setBasket(guestBasket));
    jest.mocked(agent.Account.login).mockResolvedValue({ email: 'alice@example.test', token, basket: null });
    await store.dispatch(signInUser({})).unwrap();
    expect(store.getState().basket.basket).toBeNull();
    expect(store.getState().account.user?.roles).toEqual(['Member', 'Admin']);
    expect(JSON.parse(localStorage.getItem('user')!)).toEqual({ email: 'alice@example.test', token });
    store.dispatch(setBasket(guestBasket));
    store.dispatch(signOut());
    expect(store.getState().basket.basket).toBeNull();
});

test('restoring a rejected session clears its stored user and stale basket', async () => {
    const store = testStore();
    localStorage.setItem('user', JSON.stringify({ email: 'alice@example.test', token }));
    store.dispatch(setBasket(guestBasket));
    jest.mocked(agent.Account.currentUser).mockRejectedValue(new ApiError({ message: 'Expired session', errors: {}, status: 401 }));
    await store.dispatch(fetchCurrentUser());
    expect(store.getState().account.user).toBeNull();
    expect(store.getState().basket.basket).toBeNull();
    expect(localStorage.getItem('user')).toBeNull();
});

test('empty guest baskets are fulfilled null results and removals apply the server basket', async () => {
    const store = testStore();
    document.cookie = 'buyerId=guest';
    store.dispatch(setBasket(guestBasket));
    jest.mocked(agent.Basket.get).mockResolvedValue(null);
    expect((await store.dispatch(fetchBasketAsync())).meta.requestStatus).toBe('fulfilled');
    expect(store.getState().basket.basket).toBeNull();
    store.dispatch(setBasket(guestBasket));
    jest.mocked(agent.Basket.removeItem).mockResolvedValue({ id: 1, buyerId: 'guest', items: [] });
    await store.dispatch(removeBasketItemAsync({ productId: 7, quantity: 9 }));
    expect(store.getState().basket.basket?.items).toEqual([]);
    expect(store.getState().basket.status).toBe('idle');
});

test('the login form stays on screen and displays a failed envelope', async () => {
    jest.mocked(agent.Account.login).mockRejectedValue(new ApiError({ message: 'Invalid username or password.', errors: {} }));
    render(<Provider store={testStore()}><MemoryRouter><Login /></MemoryRouter></Provider>);
    fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'Alice' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'wrong' } });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign In' })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: 'Sign In' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid username or password.');
    expect(mockNavigate).not.toHaveBeenCalled();
});
