import { AddShoppingCartOutlined } from '@mui/icons-material';
import { LoadingButton } from '@mui/lab';
import { Box, Card, CardActionArea, Stack, Typography } from '@mui/material';
import { MouseEventHandler } from 'react';
import { Product } from '../../app/models/product';
import { currencyFormat } from '../../app/util/util';
import StorefrontImage from './StorefrontImage';

interface Props {
  product: Product;
  href: string;
  busy: boolean;
  adding: boolean;
  quantityInBasket: number;
  eager?: boolean;
  onLinkClick?: MouseEventHandler<HTMLAnchorElement>;
  onAdd: () => void;
}

export default function ProductCard({ product, href, busy, adding, quantityInBasket, eager, onLinkClick, onAdd }: Props) {
  const stock = product.quantityInStock;
  const soldOut = stock !== undefined && stock <= 0;
  const atLimit = stock !== undefined && quantityInBasket >= stock;

  return (
    <Card component="article" variant="outlined" sx={{ height: '100%', display: 'flex', flexDirection: 'column', minWidth: 0 }}>
      <CardActionArea component="a" href={href} onClick={onLinkClick} sx={{ flexGrow: 1, display: 'block', p: { xs: 1, sm: 1.5 } }}>
        <StorefrontImage src={product.pictureUrl} alt="" eager={eager} />
        <Typography component="h3" dir="auto" sx={{ mt: 1.25, minHeight: '2.85em', fontWeight: 650, fontSize: { xs: '0.875rem', sm: '0.95rem' }, overflowWrap: 'anywhere' }}>
          {product.name}
        </Typography>
        {product.brand && <Typography dir="auto" variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{product.brand}</Typography>}
      </CardActionArea>
      <Stack sx={{ px: { xs: 1, sm: 1.5 }, pb: { xs: 1, sm: 1.5 }, gap: 1 }}>
        <Box component="p" sx={{ m: 0, fontWeight: 800, color: 'primary.main', fontSize: { xs: '0.875rem', sm: '1rem' }, overflowWrap: 'anywhere' }}>{currencyFormat(product.price)}</Box>
        <LoadingButton fullWidth variant="outlined" loading={adding} disabled={busy || atLimit} onClick={onAdd}
          startIcon={<AddShoppingCartOutlined />} sx={{ minHeight: 40, fontSize: '0.8125rem', px: 0.5 }}>
          {soldOut ? 'ناموجود' : atLimit ? 'سقف موجودی' : 'افزودن به سبد'}
        </LoadingButton>
      </Stack>
    </Card>
  );
}
