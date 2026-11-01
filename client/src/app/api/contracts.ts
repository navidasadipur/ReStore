import { PaginatedResponse } from '../models/pagination';

export interface Envelope<T> {
    succeeded: boolean;
    error?: string | null;
    errorCode?: string | null;
    errors?: Record<string, string[]>;
    data?: T | null;
    totalCount?: number;
    returnCount?: number;
    key?: number;
}

export interface ApiErrorData {
    message: string;
    errorCode?: string;
    errors: Record<string, string[]>;
    status?: number;
}

export class ApiError extends Error {
    constructor(public readonly data: ApiErrorData) {
        super(data.message);
        this.name = 'ApiError';
        Object.setPrototypeOf(this, ApiError.prototype);
    }
}

export function toApiErrorData(error: unknown): ApiErrorData {
    if (error instanceof ApiError) return error.data;
    if (Array.isArray(error)) return { message: error.flat().join('\n'), errors: {} };
    const value = error as any;
    const data = typeof value?.succeeded === 'boolean' ? value : value?.data ?? value;
    return {
        message: data?.message || data?.error || data?.title || 'The request could not be completed.',
        errorCode: data?.errorCode ?? undefined,
        errors: data?.errors ?? {},
        status: value?.status ?? data?.status
    };
}

export function getErrorMessages(error: unknown): string[] {
    const data = toApiErrorData(error);
    const fields = Object.values(data.errors).flat();
    return fields.length ? fields : data.message.split(/\r?\n/).filter(Boolean);
}

export function getFormErrors(error: unknown, fields: string[]) {
    const data = toApiErrorData(error);
    const fieldErrors: Record<string, string> = {};
    for (const [key, messages] of Object.entries(data.errors)) {
        const field = fields.find(name => name.toLowerCase() === key.split('.').pop()?.toLowerCase());
        if (field) fieldErrors[field] = messages.join('\n');
    }
    // Identity returns validator codes rather than a field dictionary.
    for (const code of (data.errorCode ?? '').split(',')) {
        const field = fields.find(name => code.toLowerCase().includes(name.toLowerCase()));
        if (field && !fieldErrors[field]) fieldErrors[field] = data.message;
    }
    return { fieldErrors, message: data.message };
}

function check<T>(envelope: Envelope<T>) {
    if (!envelope.succeeded) throw new ApiError(toApiErrorData(envelope));
}

export function unwrapNullable<T>(envelope: Envelope<T>): T | null {
    check(envelope);
    return envelope.data ?? null;
}

export function unwrapOne<T>(envelope: Envelope<T>): T {
    const data = unwrapNullable(envelope);
    if (data === null) throw new ApiError({ message: 'The requested record was not found.', errors: {} });
    return data;
}

export function unwrapKey(envelope: Envelope<unknown>): number {
    check(envelope);
    if (!envelope.key || envelope.key < 1) throw new ApiError({ message: 'The server did not return the new record id.', errors: {} });
    return envelope.key;
}

export function unwrapNothing(envelope: Envelope<unknown>): void { check(envelope); }

export function unwrapPage<T>(envelope: Envelope<T[]>, pageNumber: number, pageSize: number): PaginatedResponse<T[]> {
    check(envelope);
    const items = envelope.data ?? [];
    const totalCount = envelope.totalCount ?? items.length;
    // returnCount is the number of rows on this page, not the page size.
    return new PaginatedResponse(items, {
        currentPage: pageNumber, pageSize, totalCount,
        totalPages: Math.max(1, Math.ceil(totalCount / pageSize))
    });
}
