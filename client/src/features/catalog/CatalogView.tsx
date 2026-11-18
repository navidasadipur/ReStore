import { Close, Tune } from '@mui/icons-material';
import { Alert, Box, Button, CircularProgress, Drawer, IconButton, MenuItem, Pagination, Paper, Stack, TextField, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useId, useRef, useState } from 'react';
import { MetaData } from '../../app/models/pagination';
import CatalogFilters from './CatalogFilters';
import ProductList, { ProductListProps } from './ProductList';

const number = new Intl.NumberFormat('fa-IR');

interface Props {
  query: string;
  sort: string;
  brands: string[];
  types: string[];
  selectedBrands: string[];
  selectedTypes: string[];
  products: ProductListProps;
  metaData?: MetaData;
  productError: boolean;
  filterStatus: 'loading' | 'ready' | 'error';
  onRetryProducts: () => void;
  onRetryFilters: () => void;
  onSort: (value: string) => void;
  onPage: (value: number) => void;
  onBrands: (values: string[]) => void;
  onTypes: (values: string[]) => void;
  onClear: () => void;
}

export default function CatalogView(props: Props) {
  const theme = useTheme();
  const desktop = useMediaQuery(theme.breakpoints.up('md'));
  const [filtersOpen, setFiltersOpen] = useState(false);
  const filterTrigger = useRef<HTMLButtonElement>(null);
  const drawerTitle = useId();
  const activeFilters = props.selectedBrands.length + props.selectedTypes.length;
  const filterContent = (
    <Stack spacing={1.5}>
      {activeFilters > 0 && <Button onClick={props.onClear} sx={{ alignSelf: 'start' }}>پاک کردن فیلترها</Button>}
      {props.filterStatus === 'loading' && <CircularProgress size={23} aria-label="در حال دریافت فیلترها" />}
      {props.filterStatus === 'error' && <Alert severity="warning" action={<Button onClick={props.onRetryFilters}>تلاش دوباره</Button>}>فیلترها دریافت نشدند.</Alert>}
      {props.filterStatus === 'ready' && (props.brands.length || props.types.length ? (
        <CatalogFilters brands={props.brands} types={props.types} selectedBrands={props.selectedBrands}
          selectedTypes={props.selectedTypes} onBrandsChange={props.onBrands} onTypesChange={props.onTypes} />
      ) : <Typography variant="body2" color="text.secondary">فیلتری برای این کاتالوگ وجود ندارد.</Typography>)}
    </Stack>
  );

  return (
    <Stack spacing={2}>
      <Box>
        <Typography variant="h4" component="h1" sx={{ fontSize: { xs: '1.45rem', md: '1.9rem' } }}>محصولات</Typography>
        {props.query && <Typography color="text.secondary" sx={{ mt: 0.5, overflowWrap: 'anywhere' }}>نتایج جست‌وجو برای <bdi>{props.query}</bdi></Typography>}
      </Box>
      <Box sx={{ display: 'grid', gap: { xs: 1.5, md: 2.5 }, gridTemplateColumns: { xs: 'minmax(0, 1fr)', md: '228px minmax(0, 1fr)' }, alignItems: 'start' }}>
        {desktop && <Paper component="aside" variant="outlined" sx={{ p: 2 }} aria-label="فیلترهای کاتالوگ">
          <Typography variant="h6" component="h2" sx={{ mb: 1.5 }}>فیلترها</Typography>{filterContent}
        </Paper>}
        <Stack spacing={1.5} sx={{ minWidth: 0 }}>
          <Paper variant="outlined" sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 1, p: 1.25 }}>
            {!desktop && <Button ref={filterTrigger} variant="outlined" onClick={() => setFiltersOpen(true)} startIcon={<Tune />} aria-haspopup="dialog" aria-expanded={filtersOpen} sx={{ minHeight: 40 }}>
              فیلترها{activeFilters ? ` (${number.format(activeFilters)})` : ''}
            </Button>}
            <TextField size="small" select label="مرتب‌سازی" value={props.sort} onChange={event => props.onSort(event.target.value)} sx={{ flex: { xs: '1 1 130px', sm: '0 0 175px' } }}>
              <MenuItem value="name">نام محصول</MenuItem><MenuItem value="price">ارزان‌ترین</MenuItem><MenuItem value="priceDesc">گران‌ترین</MenuItem>
            </TextField>
            {props.metaData && <Typography role="status" color="text.secondary" variant="body2" sx={{ marginInlineStart: 'auto' }}>{number.format(props.metaData.totalCount)} کالا</Typography>}
          </Paper>
          {props.productError ? <Alert severity="error" action={<Button onClick={props.onRetryProducts}>تلاش دوباره</Button>}>محصولات دریافت نشدند.</Alert>
            : props.products.loading || props.products.products.length ? <ProductList {...props.products} />
              : <Paper variant="outlined" sx={{ p: 3 }}><Typography>محصولی با این شرایط پیدا نشد.</Typography></Paper>}
          {props.metaData && props.metaData.totalPages > 1 && <Pagination
            count={props.metaData.totalPages} page={props.metaData.currentPage} onChange={(_, page) => props.onPage(page)}
            color="primary" aria-label="صفحه‌بندی محصولات" siblingCount={0} boundaryCount={1} />}
        </Stack>
      </Box>
      {!desktop && <Drawer anchor="left" open={filtersOpen} onClose={() => setFiltersOpen(false)}
        SlideProps={{ onExited: () => filterTrigger.current?.focus() }}
        PaperProps={{ role: 'dialog', 'aria-modal': true, 'aria-labelledby': drawerTitle, sx: { width: 'min(320px, 90vw)', p: 2 } }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mb: 1 }}>
          <Typography variant="h6" component="h2" id={drawerTitle}>فیلترها</Typography>
          <IconButton aria-label="بستن فیلترها" onClick={() => setFiltersOpen(false)}><Close /></IconButton>
        </Stack>
        {filterContent}
      </Drawer>}
    </Stack>
  );
}
