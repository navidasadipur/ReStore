import { LoadingButton } from '@mui/lab';
import { Alert, Box, Button, Chip, Link, Paper, Skeleton, Stack, TextField, Typography } from '@mui/material';
import { FormEvent, MouseEventHandler, useState } from 'react';
import { Product } from '../../app/models/product';
import { currencyFormat } from '../../app/util/util';
import { categoryDisplay } from './categoryLabels';
import StorefrontImage from './StorefrontImage';

interface Props {
  product: Product | null;
  loading: boolean;
  error: boolean;
  onRetry: () => void;
  catalogHref: string;
  onLinkClick: MouseEventHandler<HTMLAnchorElement>;
  quantityInBasket: number;
  busy: boolean;
  onUpdateBasket: (quantity: number) => Promise<void>;
}

function PurchaseControls({ stock, quantityInBasket, busy, onUpdateBasket }: Pick<Props, 'quantityInBasket' | 'busy' | 'onUpdateBasket'> & { stock?: number }) {
  const available = stock === undefined || stock > 0;
  const [draft, setDraft] = useState(String(quantityInBasket || (available ? 1 : 0)));
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);
  const quantity = Number(draft);
  const valid = draft.trim() !== '' && Number.isInteger(quantity) && quantity >= 0
    && (quantity < quantityInBasket || stock === undefined || quantity <= stock);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy || saving || !valid || quantity === quantityInBasket) return;
    setFailed(false);
    setSaving(true);
    try { await onUpdateBasket(quantity); }
    catch { setFailed(true); }
    finally { setSaving(false); }
  }

  return (
    <Stack component="form" spacing={1.25} onSubmit={submit} noValidate>
      <Chip size="small" color={available ? 'success' : 'default'} variant="outlined" sx={{ alignSelf: 'start' }}
        label={stock === undefined ? 'موجودی در زمان خرید بررسی می‌شود' : available ? 'موجود' : 'ناموجود'} />
      <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }}>
        {(available || quantityInBasket > 0) && <TextField value={draft} onChange={event => setDraft(event.target.value)} type="number" size="small"
          label="تعداد" disabled={busy || saving} error={!valid} inputProps={{ min: 0, step: 1 }} sx={{ width: 92 }} />}
        <LoadingButton variant="contained" type="submit" loading={saving} disabled={busy || !valid || quantity === quantityInBasket}
          sx={{ flexGrow: 1, minHeight: 42 }}>
          {quantityInBasket > 0 ? 'به‌روزرسانی سبد' : available ? 'افزودن به سبد' : 'ناموجود'}
        </LoadingButton>
      </Stack>
      {!valid && <Typography color="error" variant="body2">تعداد را بر اساس موجودی کالا وارد کنید.</Typography>}
      {failed && <Alert severity="error">سبد به‌روزرسانی نشد. دوباره تلاش کنید.</Alert>}
    </Stack>
  );
}

export default function ProductDetailsView(props: Props) {
  const { product } = props;
  return (
    <Stack spacing={1.5}>
      <Link href={props.catalogHref} onClick={props.onLinkClick} sx={{ alignSelf: 'start', fontWeight: 650 }}>بازگشت به محصولات</Link>
      {props.loading ? <Skeleton variant="rounded" height={330} aria-label="در حال دریافت محصول" />
        : props.error ? <Alert severity="error" action={<Button onClick={props.onRetry}>تلاش دوباره</Button>}>اطلاعات محصول دریافت نشد.</Alert>
          : !product ? <Alert severity="info">محصول پیدا نشد.</Alert>
            : <Paper component="article" variant="outlined" sx={{ display: 'grid', gap: { xs: 2, md: 4 }, p: { xs: 1.5, sm: 3 }, gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: 'minmax(0, 1fr) minmax(0, 1fr)' } }}>
              <StorefrontImage src={product.pictureUrl} alt={product.name} eager />
              <Stack spacing={2} sx={{ minWidth: 0 }}>
                {product.type && <Typography color="text.secondary" variant="body2" dir="auto">{categoryDisplay(product.type).label}</Typography>}
                <Typography component="h1" variant="h4" dir="auto" sx={{ fontSize: { xs: '1.35rem', md: '1.8rem' }, overflowWrap: 'anywhere' }}>{product.name}</Typography>
                {product.brand && <Typography color="text.secondary">برند <bdi>{product.brand}</bdi></Typography>}
                {product.description && <Typography dir="auto" color="text.secondary" sx={{ overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}>{product.description}</Typography>}
                <Box sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'background.default' }}>
                  <Typography color="primary.main" variant="h6" sx={{ mb: 1.5 }}>{currencyFormat(product.price)}</Typography>
                  <PurchaseControls key={`${product.id}:${props.quantityInBasket}`} stock={product.quantityInStock}
                    quantityInBasket={props.quantityInBasket} busy={props.busy} onUpdateBasket={props.onUpdateBasket} />
                </Box>
              </Stack>
            </Paper>}
    </Stack>
  );
}
