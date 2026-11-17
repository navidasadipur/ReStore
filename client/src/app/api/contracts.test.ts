import { ApiError, getErrorMessages, getFormErrors, unwrapKey, unwrapNullable, unwrapOne, unwrapPage } from './contracts';
import { buildCatalogParams } from './catalogParams';

test('a partial last page retains the requested size and correct page count', () => {
    const page = unwrapPage({ succeeded: true, data: [{ id: 13 }, { id: 14 }], totalCount: 14, returnCount: 2 }, 3, 6);
    expect(page.metaData).toEqual({ currentPage: 3, pageSize: 6, totalCount: 14, totalPages: 3 });
});

test('an empty page still retains its size and has no rows', () => {
    const page = unwrapPage({ succeeded: true, data: [], totalCount: 0, returnCount: 0 }, 1, 6);
    expect(page.items).toEqual([]);
    expect(page.metaData.pageSize).toBe(6);
    expect(page.metaData.totalPages).toBe(1);
});

test('nullable successes are empty states, while failed list envelopes retain their error', () => {
    expect(unwrapNullable({ succeeded: true, data: null })).toBeNull();
    expect(() => unwrapPage({ succeeded: false, data: [], error: 'Database unavailable' }, 1, 6)).toThrow('Database unavailable');
    expect(() => unwrapOne({ succeeded: false, error: 'Invalid credentials' })).toThrow(ApiError);
    expect(() => unwrapKey({ succeeded: false, key: 0, error: 'Registration failed' })).toThrow('Registration failed');
});

test('binding errors and Identity validator codes can be shown on their form fields', () => {
    const binding = getFormErrors({ message: 'Invalid values', errors: { 'command.Email': ['Invalid email'] } }, ['email']);
    expect(binding.fieldErrors).toEqual({ email: 'Invalid email' });
    const identity = getFormErrors({ succeeded: false, error: 'Email already registered', errorCode: 'DuplicateEmail' }, ['username', 'email', 'password']);
    expect(identity.fieldErrors.email).toBe('Email already registered');
    expect(getErrorMessages({ errors: { Problem1: ['First'], Problem2: ['Second'] } })).toEqual(['First', 'Second']);
});

test('catalog requests use the new search name and preserve filter and sort inputs', () => {
    const params = buildCatalogParams({ pageNumber: 2, pageSize: 6, searchTerm: ' run ', brands: ['Nike', 'Puma'], types: ['Shoes'], orderBy: 'priceDesc' });
    expect(params.get('fullSearchPhrase')).toBe('run');
    expect(params.has('searchTerm')).toBe(false);
    expect(params.get('brands')).toBe('Nike,Puma');
    expect(params.get('types')).toBe('Shoes');
    expect(params.get('orderBy')).toBe('priceDesc');
    expect(params.get('pageNumber')).toBe('2');
});
