import axios, { AxiosError, AxiosResponse } from "axios";
import { toast } from 'react-toastify';
import { PaginatedResponse } from "../models/pagination";
import { store } from "../store/configureStore";
import { router } from "../router/Routes";

const sleep = () => new Promise(resolve => setTimeout(resolve, 500))

axios.defaults.baseURL = process.env.REACT_APP_API_URL;
axios.defaults.withCredentials = true;

const responseBody = (response: AxiosResponse) => response.data;

axios.interceptors.request.use(config => {
    const token = store.getState().account.user?.token;
    if (token) config.headers.Authorization = `Bearer ${token}`;
    return config;
})

axios.interceptors.response.use(async response => {
    if (process.env.NODE_ENV === 'development') await sleep();
    return response;
}, (error: AxiosError) => {
    // Endpoints that have not been migrated to the envelope contract yet still answer
    // with status codes, so this branch stays for them (basket, orders, account, ...).
    const {status} = error.response!;
    const {data}: any = error.response!;
    switch (status) {
        case 400:
            if (data.errors) {
                const modelStateErrors: string[] = [];
                for (const key in data.errors) {
                    if (data.errors[key]) {
                        modelStateErrors.push(data.errors[key])
                    }
                }
                throw modelStateErrors.flat();
            }
            toast.error(data.title);
            break;
        case 401:
            toast.error(data.title);
            break;
        case 403:
            toast.error('You are not allowed to do that!');
            break;
        case 500:
            router.navigate('/server-error', {state: {error: data}});
            break;
        default:
            break;
    }
    return Promise.reject(error.response)
})

/**
 * The shape every migrated endpoint returns: HTTP 200 with the outcome in the body
 * (backend decision D1). `succeeded` mirrors `result`, and is what we branch on.
 */
interface Envelope<T> {
    succeeded: boolean;
    error?: string | null;
    data?: T;
    totalCount?: number;
    returnCount?: number;
    key?: number;
}

function fail(message?: string | null): never {
    const text = message ?? 'The request could not be completed.';
    toast.error(text);
    throw new Error(text);
}

function unwrapOne<T>(envelope: Envelope<T>): T {
    if (!envelope.succeeded) fail(envelope.error);
    if (envelope.data === null || envelope.data === undefined) {
        fail('The requested record was not found.');
    }
    return envelope.data as T;
}

function unwrapCreatedKey(envelope: Envelope<unknown>): number {
    if (!envelope.succeeded) fail(envelope.error);
    if (envelope.key === undefined || envelope.key === null) {
        fail('The server did not return the new record id.');
    }
    return envelope.key;
}

function unwrapNothing(envelope: Envelope<unknown>): void {
    if (!envelope.succeeded) fail(envelope.error);
}

/**
 * Turns a list envelope into the PaginatedResponse the catalog slice already expects.
 * The envelope carries counts rather than a page number, so the page comes from the
 * request parameters that produced it.
 */
function unwrapList<T>(envelope: Envelope<T[]>, params?: URLSearchParams): PaginatedResponse<T[]> {
    if (!envelope.succeeded) fail(envelope.error);

    const items = envelope.data ?? [];
    const pageSize = envelope.returnCount && envelope.returnCount > 0
        ? envelope.returnCount
        : items.length;
    const totalCount = envelope.totalCount ?? items.length;
    const currentPage = Number(params?.get('pageNumber') ?? 1) || 1;

    return new PaginatedResponse(items, {
        currentPage,
        pageSize: pageSize || 1,
        totalCount,
        totalPages: Math.max(1, Math.ceil(totalCount / Math.max(1, pageSize || 1)))
    });
}

const requests = {
    get: (url: string, params?: URLSearchParams) => axios.get(url, {params}).then(responseBody),
    post: (url: string, body: {}) => axios.post(url, body).then(responseBody),
    put: (url: string, body: {}) => axios.put(url, body).then(responseBody),
    delete: (url: string) => axios.delete(url).then(responseBody),
    postForm: (url: string, data: FormData) => axios.post(url, data, {
        headers: {'Content-Type': 'multipart/form-data'}
    }).then(responseBody),
    putForm: (url: string, data: FormData) => axios.put(url, data, {
        headers: {'Content-Type': 'multipart/form-data'}
    }).then(responseBody)
}

function createFormData(item: any) {
    let formData = new FormData()
    for (const key in item) {
        formData.append(key, item[key])
    }
    return formData;
}

const Catalog = {
    list: async (params: URLSearchParams) => {
        const envelope: Envelope<any[]> = await requests.get('products', params);
        return unwrapList(envelope, params);
    },
    details: async (id: number) => {
        const envelope: Envelope<any> = await requests.get(`products/${id}`);
        return unwrapOne(envelope);
    },
    fetchFilters: async () => {
        const envelope: Envelope<{brands: string[], types: string[]}> = await requests.get('products/filters');
        return unwrapOne(envelope);
    }
}

const Admin = {
    // Create answers with the new id only, so the product is read back: the form upserts
    // the returned product into the catalog and expects the full shape.
    createProduct: async (product: any) => {
        const envelope: Envelope<unknown> = await requests.postForm('products', createFormData(product));
        const id = unwrapCreatedKey(envelope);
        return await Catalog.details(id);
    },
    updateProduct: async (product: any) => {
        const envelope: Envelope<unknown> = await requests.putForm('products', createFormData(product));
        unwrapNothing(envelope);
        return await Catalog.details(product.id);
    },
    deleteProduct: async (id: number) => {
        const envelope: Envelope<unknown> = await requests.delete(`products/${id}`);
        unwrapNothing(envelope);
    }
}

const TestErrors = {
    get400Error: () => requests.get('buggy/bad-request'),
    get401Error: () => requests.get('buggy/unauthorized'),
    get404Error: () => requests.get('buggy/not-found'),
    get500Error: () => requests.get('buggy/server-error'),
    getValidationError: () => requests.get('buggy/validation-error'),
}

const Basket = {
    get: () => requests.get('basket'),
    addItem: (productId: number, quantity = 1) => requests.post(`basket?productId=${productId}&quantity=${quantity}`, {}),
    removeItem: (productId: number, quantity = 1) => requests.delete(`basket?productId=${productId}&quantity=${quantity}`),

}

const Account = {
    login: (values: any) => requests.post('account/login', values),
    register: (values: any) => requests.post('account/register', values),
    currentUser: () => requests.get('account/currentUser'),
    fetchAddress: () => requests.get('account/savedAddress')
}

const Orders = {
    list: () => requests.get('orders'),
    fetch: (id: number) => requests.get(`orders/${id}`),
    create: (values: any) => requests.post('orders', values)
}

const Payments = {
    createPaymentIntent: () => requests.post('payments', {})
}

const agent = {
    Catalog,
    TestErrors,
    Basket,
    Account,
    Orders,
    Payments,
    Admin
}

export default agent;
