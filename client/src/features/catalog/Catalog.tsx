import { useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import agent from '../../app/api/agent';
import { buildCatalogParams } from '../../app/api/catalogParams';
import useStorefrontQuery from '../../app/hooks/useStorefrontQuery';
import { readCatalogLocation } from './catalogLocation';
import CatalogView from './CatalogView';
import useProductListActions from './useProductListActions';

const fetchFilters = () => agent.Catalog.fetchFilters(true);

export default function Catalog() {
  const [search, setSearch] = useSearchParams();
  const params = readCatalogLocation(search);
  const apiQuery = buildCatalogParams(params).toString();
  const fetchProducts = useCallback(() => agent.Catalog.list(new URLSearchParams(apiQuery), true), [apiQuery]);
  const products = useStorefrontQuery(fetchProducts);
  const filters = useStorefrontQuery(fetchFilters);
  const productActions = useProductListActions();
  const page = products.result.status === 'ready' ? products.result.data : undefined;
  const options = filters.result.status === 'ready' ? filters.result.data : undefined;

  function updateFilters(update: (next: URLSearchParams) => void) {
    const next = new URLSearchParams(search);
    update(next);
    next.delete('page');
    setSearch(next, { preventScrollReset: true });
  }

  function setFilter(key: string, values: string[]) {
    updateFilters(next => {
      next.delete(key);
      values.forEach(value => next.append(key, value));
    });
  }

  function setPage(value: number) {
    const next = new URLSearchParams(search);
    if (value > 1) next.set('page', String(value));
    else next.delete('page');
    setSearch(next);
  }

  return <CatalogView query={params.searchTerm ?? ''} sort={params.orderBy}
    brands={options?.brands ?? []} types={options?.types ?? []} selectedBrands={params.brands} selectedTypes={params.types}
    products={{ ...productActions, products: page?.items ?? [], loading: products.result.status === 'loading' }}
    metaData={page?.metaData} productError={products.result.status === 'error'} filterStatus={filters.result.status}
    onRetryProducts={products.retry} onRetryFilters={filters.retry} onPage={setPage}
    onSort={value => updateFilters(next => next.set('sort', value))}
    onBrands={values => setFilter('brand', values)} onTypes={values => setFilter('type', values)}
    onClear={() => updateFilters(next => { next.delete('brand'); next.delete('type'); })} />;
}
