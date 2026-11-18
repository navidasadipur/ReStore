import { ArrowBack, CategoryOutlined } from '@mui/icons-material';
import { Alert, Box, Button, Link, Paper, Skeleton, Stack, Typography } from '@mui/material';
import ProductList, { ProductListProps } from '../catalog/ProductList';

interface Props {
  categories: { label: string; image?: string; href: string }[];
  categoryStatus: 'loading' | 'ready' | 'error';
  products: ProductListProps;
  productsFailed: boolean;
  catalogHref: string;
  onRetryCategories: () => void;
  onRetryProducts: () => void;
}

export default function HomeView({ categories, categoryStatus, products, productsFailed, catalogHref, onRetryCategories, onRetryProducts }: Props) {
  const { onLinkClick } = products;
  return (
    <Stack spacing={{ xs: 2, md: 3 }}>
      {categoryStatus === 'error' && <Alert severity="warning" action={<Button onClick={onRetryCategories}>تلاش دوباره</Button>}>دسته‌بندی‌ها دریافت نشدند.</Alert>}
      {(categories.length > 0 || categoryStatus === 'loading') && (
        <Box component="nav" aria-label="دسته‌بندی محصولات" sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: { xs: 0.75, sm: 1.5 } }}>
          {categoryStatus === 'loading' ? Array.from({ length: 4 }, (_, index) => <Skeleton key={index} variant="rounded" height={84} />)
            : categories.map(({ label, image, href }) => (
              <Paper component={Link} key={href} href={href} onClick={onLinkClick} variant="outlined" underline="none" sx={{
                display: 'grid', alignContent: 'center', justifyItems: 'center', gap: 0.5, minHeight: { xs: 78, md: 94 },
                p: 0.5, color: 'text.primary', '&:hover': { borderColor: 'primary.main' },
              }}>
                {image ? <Box component="img" src={image} alt="" width={48} height={48} sx={{ objectFit: 'contain' }} /> : <CategoryOutlined aria-hidden color="primary" />}
                <Typography dir="auto" sx={{ fontSize: { xs: '0.75rem', sm: '0.9rem' }, fontWeight: 650, textAlign: 'center', overflowWrap: 'anywhere' }}>{label}</Typography>
              </Paper>
            ))}
        </Box>
      )}
      <Paper component="section" elevation={0} sx={theme => ({
        display: 'grid', alignItems: 'center', gridTemplateColumns: 'minmax(0, 5fr) minmax(0, 4fr)',
        minHeight: { xs: 140, md: 200 }, gap: { xs: 1, md: 2 }, px: { xs: 1.75, sm: 4 }, py: 2, overflow: 'hidden',
        bgcolor: theme.palette.mode === 'light' ? '#E2EDE3' : '#283B30',
      })}>
        <Box>
          <Typography variant="h4" component="h1" sx={{ mb: 1.5, fontSize: { xs: '1.25rem', sm: '1.85rem', md: '2.3rem' }, color: 'primary.main' }}>تجهیزات اسنوبرد</Typography>
          <Button variant="contained" href={catalogHref} onClick={onLinkClick} endIcon={<ArrowBack />} sx={{ minHeight: 40 }}>مشاهده محصولات</Button>
        </Box>
        <Box component="img" src="/images/products/sb-react1.png" alt="" width={320} height={180}
          sx={{ width: '100%', height: { xs: 105, sm: 145, md: 170 }, objectFit: 'contain', justifySelf: 'center' }} />
      </Paper>
      <Paper component="section" variant="outlined" aria-labelledby="featured-products" sx={{ p: { xs: 1, sm: 2 }, bgcolor: 'background.paper' }}>
        <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ mb: 1.5, gap: 1, flexWrap: 'wrap' }}>
          <Typography id="featured-products" component="h2" variant="h6">محصولات منتخب</Typography>
          <Button href={catalogHref} onClick={onLinkClick} endIcon={<ArrowBack />}>مشاهده همه</Button>
        </Stack>
        {productsFailed ? <Alert severity="error" action={<Button onClick={onRetryProducts}>تلاش دوباره</Button>}>محصولات دریافت نشدند.</Alert>
          : products.loading || products.products.length ? <ProductList {...products} />
            : <Typography color="text.secondary">فعلاً محصولی برای نمایش وجود ندارد.</Typography>}
      </Paper>
    </Stack>
  );
}
