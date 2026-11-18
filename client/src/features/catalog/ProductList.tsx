import { Box } from '@mui/material';
import { MouseEventHandler } from 'react';
import { Product } from '../../app/models/product';
import ProductCard from './ProductCard';
import ProductCardSkeleton from './ProductCardSkeleton';

export interface ProductListProps {
  products: Product[];
  busy: boolean;
  addingId?: number;
  basketQuantities: ReadonlyMap<number, number>;
  productHref: (id: number) => string;
  onLinkClick: MouseEventHandler<HTMLAnchorElement>;
  onAdd: (productId: number) => void;
  loading?: boolean;
}

export default function ProductList({ products, busy, addingId, basketQuantities, productHref, onLinkClick, onAdd, loading }: ProductListProps) {
  return (
    <Box aria-busy={loading || undefined} sx={{
      display: 'grid', gap: { xs: 1, sm: 2 }, gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', sm: 'repeat(3, minmax(0, 1fr))' },
      '@media (max-width: 339px)': { gridTemplateColumns: 'minmax(0, 1fr)' },
    }}>
      {loading ? Array.from({ length: 6 }, (_, index) => <ProductCardSkeleton key={index} />) : products.map((product, index) => (
        <ProductCard key={product.id} product={product} href={productHref(product.id)} busy={busy} adding={addingId === product.id}
          quantityInBasket={basketQuantities.get(product.id) ?? 0} eager={index < 2} onLinkClick={onLinkClick} onAdd={() => onAdd(product.id)} />
      ))}
    </Box>
  );
}
