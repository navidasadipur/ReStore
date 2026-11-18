import { ProductParams } from '../../app/models/product';

const orderOptions = ['name', 'price', 'priceDesc'];
const storefrontPageSize = 6;

export function readCatalogLocation(search: URLSearchParams): ProductParams {
  const page = Number(search.get('page'));
  const sort = search.get('sort') ?? 'name';
  return {
    searchTerm: search.get('q')?.trim() ?? '',
    types: search.getAll('type').filter(Boolean),
    brands: search.getAll('brand').filter(Boolean),
    orderBy: orderOptions.includes(sort) ? sort : 'name',
    pageNumber: Number.isInteger(page) && page > 0 && page <= 100000 ? page : 1,
    pageSize: storefrontPageSize,
  };
}

export function catalogHref({ query, type }: { query?: string; type?: string } = {}) {
  const search = new URLSearchParams();
  if (query?.trim()) search.set('q', query.trim());
  if (type) search.set('type', type);
  const queryString = search.toString();
  return queryString ? `/catalog?${queryString}` : '/catalog';
}
