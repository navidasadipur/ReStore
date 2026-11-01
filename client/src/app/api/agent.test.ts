import axios, { AxiosError, AxiosResponse } from 'axios';
import { toast } from 'react-toastify';
import { router } from '../router/Routes';
import agent, { createFormData } from './agent';
import { ApiError, getErrorMessages } from './contracts';

jest.mock('../store/configureStore', () => ({ store: { getState: () => ({ account: { user: null } }) } }));
jest.mock('../router/Routes', () => ({ router: { navigate: jest.fn() } }));
jest.mock('react-toastify', () => ({ toast: { error: jest.fn() } }));

function respond(data: unknown, status = 200) {
    axios.defaults.adapter = async config => {
        const response: AxiosResponse = { data, status, statusText: '', headers: {}, config };
        if (status >= 400) throw new AxiosError('HTTP failure', undefined, config, undefined, response);
        return response;
    };
}

beforeEach(() => jest.clearAllMocks());

test('login and current-user unwrap the session, and empty saved addresses remain null', async () => {
    const session = { email: 'alice@example.test', token: 'token', basket: null };
    respond({ succeeded: true, data: session });
    expect(await agent.Account.login({ username: 'Alice', password: 'password' })).toEqual(session);
    expect(await agent.Account.currentUser()).toEqual(session);
    respond({ succeeded: true, data: null });
    expect(await agent.Account.fetchAddress()).toBeNull();
    expect(await agent.Basket.get()).toBeNull();
    expect(toast.error).not.toHaveBeenCalled();
});

test('registration and order creation return the inserted key', async () => {
    respond({ succeeded: true, key: 42 });
    expect(await agent.Account.register({})).toBe(42);
    expect(await agent.Orders.create({})).toBe(42);
});

test('failed HTTP 200 account responses reject for the form without a duplicate toast', async () => {
    respond({ succeeded: false, error: 'Invalid username or password.' });
    await expect(agent.Account.login({})).rejects.toThrow('Invalid username or password.');
    expect(toast.error).not.toHaveBeenCalled();
});

test('orders use nested paging parameters and expose the paginated list', async () => {
    let parameters: URLSearchParams | undefined;
    axios.defaults.adapter = async config => {
        parameters = config.params;
        return { data: { succeeded: true, data: [{ id: 7 }], totalCount: 26, returnCount: 1 }, status: 200, statusText: '', headers: {}, config };
    };
    const page = await agent.Orders.list(2, 25);
    expect(parameters?.get('PagingInfo.PageNumber')).toBe('2');
    expect(parameters?.get('PagingInfo.PageRecordCount')).toBe('25');
    expect(page.items).toEqual([{ id: 7 }]);
    expect(page.metaData.totalPages).toBe(2);
});

test('basket mutations and payment intent creation return the authoritative basket', async () => {
    const basket = { id: 1, buyerId: 'Alice', items: [], paymentIntentId: 'pi-test', clientSecret: 'secret' };
    respond({ succeeded: true, data: basket });
    expect(await agent.Basket.removeItem(7, 9)).toEqual(basket);
    expect(await agent.Payments.createPaymentIntent()).toEqual(basket);
});

test('admin creation reads back the full product after receiving its key', async () => {
    const paths: string[] = [];
    const product = { id: 9, name: 'Shoe', description: 'Full description' };
    axios.defaults.adapter = async config => {
        paths.push(config.url!);
        return { data: config.method === 'post' ? { succeeded: true, key: 9 } : { succeeded: true, data: product }, status: 200, statusText: '', headers: {}, config };
    };
    expect(await agent.Admin.createProduct({ name: 'Shoe', file: null })).toEqual(product);
    expect(paths).toEqual(['products', 'products/9']);
});

test('multipart omits missing files and includes real uploads', () => {
    const file = new File(['image'], 'shoe.png', { type: 'image/png' });
    expect(createFormData({ name: 'Shoe', file: null }).has('file')).toBe(false);
    expect(createFormData({ file, price: 100 }).get('file')).toEqual(file);
});

test('intentional validation errors still expose all messages', async () => {
    respond({ title: 'Validation failed', errors: { Problem1: ['First'], Problem2: ['Second'] } }, 400);
    try { await agent.TestErrors.getValidationError(); throw new Error('Expected rejection'); }
    catch (error) { expect(getErrorMessages(error)).toEqual(['First', 'Second']); }
    expect(toast.error).not.toHaveBeenCalled();
});

test('intentional 500 errors still navigate to the server-error demonstration', async () => {
    const problem = { title: 'This is a server error', status: 500 };
    respond(problem, 500);
    await expect(agent.TestErrors.get500Error()).rejects.toBeInstanceOf(ApiError);
    expect(router.navigate).toHaveBeenCalledWith('/server-error', { state: { error: problem } });
});

test('network failures are readable errors rather than crashes on a missing response', async () => {
    axios.defaults.adapter = async () => { throw new AxiosError('Network Error'); };
    await expect(agent.Catalog.details(1)).rejects.toThrow('Unable to reach the server');
    expect(toast.error).toHaveBeenCalledTimes(1);
});
