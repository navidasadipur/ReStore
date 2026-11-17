import { ProductParams } from '../models/product';

export function buildCatalogParams(productParams: ProductParams): URLSearchParams {
    const params = new URLSearchParams();
    params.set('pageNumber', Math.max(1, productParams.pageNumber).toString());
    params.set('pageSize', Math.min(50, productParams.pageSize > 0 ? productParams.pageSize : 6).toString());
    params.set('orderBy', productParams.orderBy);
    if (productParams.searchTerm?.trim()) params.set('fullSearchPhrase', productParams.searchTerm.trim());
    if (productParams.brands.length) params.set('brands', productParams.brands.join(','));
    if (productParams.types.length) params.set('types', productParams.types.join(','));
    return params;
}
