import agent from '../../app/api/agent';
import { buildCatalogParams } from '../../app/api/catalogParams';
import useStorefrontQuery from '../../app/hooks/useStorefrontQuery';
import { catalogHref } from '../catalog/catalogLocation';
import { categoryDisplay } from '../catalog/categoryLabels';
import useProductListActions from '../catalog/useProductListActions';
import HomeView from './HomeView';

const fetchFeatured = () => agent.Catalog.list(buildCatalogParams({
  pageNumber: 1, pageSize: 6, orderBy: 'name', types: [], brands: [],
}), true);
const fetchCategories = () => agent.Catalog.fetchFilters(true);

export default function HomePage() {
  const categories = useStorefrontQuery(fetchCategories);
  const products = useStorefrontQuery(fetchFeatured);
  const productActions = useProductListActions();
  const types = categories.result.status === 'ready' ? categories.result.data.types : [];
  const tiles = types.map(type => ({ ...categoryDisplay(type), href: catalogHref({ type }) }));
  // Prefer the four categories with dedicated illustrations when the catalog grows.
  tiles.sort((a, b) => Number(Boolean(b.image)) - Number(Boolean(a.image)));

  return <HomeView categories={tiles.slice(0, 4)} categoryStatus={categories.result.status}
    products={{ ...productActions, products: products.result.status === 'ready' ? products.result.data.items : [], loading: products.result.status === 'loading' }}
    productsFailed={products.result.status === 'error'} catalogHref={catalogHref()}
    onRetryCategories={categories.retry} onRetryProducts={products.retry} />;
}
