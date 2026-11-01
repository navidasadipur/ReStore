import axios, { AxiosError, AxiosResponse } from 'axios';
import { toast } from 'react-toastify';
import { store } from '../store/configureStore';
import { router } from '../router/Routes';
import { Basket as BasketModel } from '../models/basket';
import { Order, ShippingAddress } from '../models/order';
import { Product } from '../models/product';
import { UserSession } from '../models/user';
import { ApiError, Envelope, getErrorMessages, toApiErrorData, unwrapKey, unwrapNothing, unwrapNullable, unwrapOne, unwrapPage } from './contracts';

axios.defaults.baseURL = process.env.REACT_APP_API_URL ?? '/api/';
axios.defaults.withCredentials = true;
const responseBody = <T>(response: AxiosResponse<T>) => response.data;

axios.interceptors.request.use(config => {
    const token = store.getState().account.user?.token;
    if (token) config.headers.Authorization = `Bearer ${token}`;
    return config;
});

axios.interceptors.response.use(response => response, (error: AxiosError<any>) => {
    // BuggyController, authentication and HTTP protocol failures still use status codes.
    if (!error.response) {
        const failure = new ApiError({ message: 'Unable to reach the server. Please try again.', errors: {} });
        toast.error(failure.message);
        return Promise.reject(failure);
    }
    const { status, data } = error.response;
    const failure = new ApiError(toApiErrorData({ data, status }));
    switch (status) {
        case 400:
            if (!data?.errors) toast.error(data?.title ?? failure.message);
            break;
        case 401:
            toast.error(data?.title ?? 'Please sign in to continue.');
            break;
        case 403:
            toast.error('You are not allowed to do that!');
            break;
        case 500:
            router.navigate('/server-error', { state: { error: data } });
            break;
        default:
            break;
    }
    return Promise.reject(failure);
});

async function read<T, R>(request: Promise<Envelope<T>>, unwrap: (envelope: Envelope<T>) => R, notify = true): Promise<R> {
    // Only envelope failures are handled here; HTTP errors were already handled above.
    const envelope = await request;
    try { return unwrap(envelope); }
    catch (error) {
        if (notify) toast.error(getErrorMessages(error).join('\n'));
        throw error;
    }
}

const requests = {
    get: <T>(url: string, params?: URLSearchParams) => axios.get<T>(url, { params }).then(responseBody),
    post: <T>(url: string, body: {}) => axios.post<T>(url, body).then(responseBody),
    put: <T>(url: string, body: {}) => axios.put<T>(url, body).then(responseBody),
    delete: <T>(url: string) => axios.delete<T>(url).then(responseBody),
    postForm: <T>(url: string, data: FormData) => axios.post<T>(url, data).then(responseBody),
    putForm: <T>(url: string, data: FormData) => axios.put<T>(url, data).then(responseBody)
};

export function createFormData(item: Record<string, any>) {
    const formData = new FormData();
    for (const [key, value] of Object.entries(item)) {
        if (value === null || value === undefined) continue;
        if (key === 'file' && !(value instanceof Blob)) continue;
        formData.append(key, value instanceof Blob ? value : String(value));
    }
    return formData;
}

const Catalog = {
    list: (params: URLSearchParams) => {
        const pageNumber = Math.max(1, Number(params.get('pageNumber')) || 1);
        const requestedSize = Number(params.get('pageSize')) || 6;
        const pageSize = requestedSize > 0 ? Math.min(50, requestedSize) : 6;
        const query = new URLSearchParams(params);
        query.set('pageNumber', String(pageNumber));
        query.set('pageSize', String(pageSize));
        return read(requests.get<Envelope<Product[]>>('products', query), envelope => unwrapPage(envelope, pageNumber, pageSize));
    },
    details: (id: number): Promise<Product> => read(requests.get<Envelope<Product>>(`products/${id}`), unwrapOne),
    fetchFilters: (): Promise<{ brands: string[], types: string[] }> =>
        read(requests.get<Envelope<{ brands: string[], types: string[] }>>('products/filters'), unwrapOne)
};

const Admin = {
    createProduct: async (product: any): Promise<Product> => {
        const id = await read(requests.postForm<Envelope<unknown>>('products', createFormData(product)), unwrapKey, false);
        return Catalog.details(id);
    },
    updateProduct: async (product: any): Promise<Product> => {
        await read(requests.putForm<Envelope<unknown>>('products', createFormData(product)), unwrapNothing, false);
        return Catalog.details(product.id);
    },
    deleteProduct: (id: number) => read(requests.delete<Envelope<unknown>>(`products/${id}`), unwrapNothing)
};

const Basket = {
    get: (): Promise<BasketModel | null> => read(requests.get<Envelope<BasketModel>>('basket'), unwrapNullable),
    addItem: (productId: number, quantity = 1): Promise<BasketModel> =>
        read(requests.post<Envelope<BasketModel>>(`basket?productId=${productId}&quantity=${quantity}`, {}), unwrapOne),
    removeItem: (productId: number, quantity = 1): Promise<BasketModel | null> =>
        read(requests.delete<Envelope<BasketModel>>(`basket?productId=${productId}&quantity=${quantity}`), unwrapNullable)
};

const Account = {
    login: (values: any): Promise<UserSession> => read(requests.post<Envelope<UserSession>>('account/login', values), unwrapOne, false),
    register: (values: any): Promise<number> => read(requests.post<Envelope<unknown>>('account/register', values), unwrapKey, false),
    currentUser: (): Promise<UserSession> => read(requests.get<Envelope<UserSession>>('account/currentUser'), unwrapOne, false),
    fetchAddress: (): Promise<ShippingAddress | null> => read(requests.get<Envelope<ShippingAddress>>('account/savedAddress'), unwrapNullable)
};

const Orders = {
    list: (pageNumber = 1, pageSize = 25) => {
        const page = Math.max(1, pageNumber);
        const size = pageSize > 0 ? Math.min(200, pageSize) : 25;
        const params = new URLSearchParams({ 'PagingInfo.PageNumber': String(page), 'PagingInfo.PageRecordCount': String(size) });
        return read(requests.get<Envelope<Order[]>>('orders', params), envelope => unwrapPage(envelope, page, size), false);
    },
    fetch: (id: number): Promise<Order> => read(requests.get<Envelope<Order>>(`orders/${id}`), unwrapOne),
    create: (values: any): Promise<number> => read(requests.post<Envelope<unknown>>('orders', values), unwrapKey, false)
};

const Payments = {
    createPaymentIntent: (): Promise<BasketModel> => read(requests.post<Envelope<BasketModel>>('payments', {}), unwrapOne, false)
};

const TestErrors = {
    get400Error: () => requests.get('buggy/bad-request'),
    get401Error: () => requests.get('buggy/unauthorized'),
    get404Error: () => requests.get('buggy/not-found'),
    get500Error: () => requests.get('buggy/server-error'),
    getValidationError: () => requests.get('buggy/validation-error')
};

const agent = { Catalog, Admin, Basket, Account, Orders, Payments, TestErrors };
export default agent;
