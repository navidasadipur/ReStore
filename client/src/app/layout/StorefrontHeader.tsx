import { DarkModeOutlined, LightModeOutlined, PersonOutline, Search, ShoppingBagOutlined } from '@mui/icons-material';
import { AppBar, Badge, Box, Button, Container, IconButton, InputBase, Link, Paper, Stack } from '@mui/material';
import { FormEvent, MouseEventHandler, useState } from 'react';
import SignedInMenu from './SignedInMenu';

interface Props {
  basketCount: number;
  accountEmail?: string;
  links: { label: string; href: string }[];
  pathname: string;
  searchValue: string;
  isDarkMode: boolean;
  onThemeChange: () => void;
  onLinkClick: MouseEventHandler<HTMLAnchorElement>;
  onSearch: (query: string) => void;
  onSignOut: () => void;
}

function SearchForm({ value, onSearch }: { value: string; onSearch: Props['onSearch'] }) {
  const [query, setQuery] = useState(value);
  const submit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSearch(query.trim());
  };

  return (
    <Paper component="form" role="search" variant="outlined" onSubmit={submit} sx={{
      gridArea: 'search', display: 'flex', alignItems: 'center', bgcolor: 'background.default', minWidth: 0, px: 1,
      '&:focus-within': { borderColor: 'primary.main' },
    }}>
      <IconButton aria-label="جست‌وجو" type="submit" color="primary"><Search /></IconButton>
      <InputBase type="search" value={query} onChange={event => setQuery(event.target.value)} fullWidth
        placeholder="جست‌وجوی محصول" inputProps={{ 'aria-label': 'جست‌وجوی محصول', dir: 'auto' }}
        sx={{ minWidth: 0, '& input::placeholder': { color: 'text.secondary', opacity: 1 } }} />
    </Paper>
  );
}

export default function StorefrontHeader(props: Props) {
  const count = new Intl.NumberFormat('fa-IR').format(props.basketCount);

  return (
    <AppBar position="static" color="inherit" elevation={0} sx={{ bgcolor: 'background.paper', borderBottom: 1, borderColor: 'divider' }}>
      <Container maxWidth="lg">
        <Box sx={{
          display: 'grid', alignItems: 'center', columnGap: { xs: 1, md: 3 }, rowGap: 1,
          gridTemplateAreas: { xs: '"brand actions" "search search"', md: '"brand search actions"' },
          gridTemplateColumns: { xs: 'minmax(0, 1fr) auto', md: 'auto minmax(0, 1fr) auto' },
          py: { xs: 1.25, md: 2 },
        }}>
          <Link href="/" onClick={props.onLinkClick} underline="none" sx={{
            gridArea: 'brand', justifySelf: 'start', color: 'primary.main', fontWeight: 850, fontSize: { xs: 22, sm: 27 }, whiteSpace: 'nowrap',
          }}>ری‌استور</Link>
          <SearchForm key={props.searchValue} value={props.searchValue} onSearch={props.onSearch} />
          <Stack direction="row" alignItems="center" sx={{ gridArea: 'actions', gap: { xs: 0.25, sm: 1 } }}>
            {props.accountEmail ? <SignedInMenu email={props.accountEmail} onLinkClick={props.onLinkClick} onSignOut={props.onSignOut} /> : (
              <Button href="/login" onClick={props.onLinkClick} startIcon={<PersonOutline />} sx={{ whiteSpace: 'nowrap' }}>ورود</Button>
            )}
            <IconButton href="/basket" onClick={props.onLinkClick} aria-label={`سبد خرید، ${count} کالا`} color="primary" sx={{ minHeight: 44, minWidth: 44 }}>
              <Badge badgeContent={props.basketCount > 99 ? '۹۹+' : count} invisible={!props.basketCount} color="primary"><ShoppingBagOutlined /></Badge>
            </IconButton>
            <IconButton onClick={props.onThemeChange} aria-label={props.isDarkMode ? 'نمای روشن' : 'نمای تیره'} color="primary" sx={{ minHeight: 44, minWidth: 44 }}>
              {props.isDarkMode ? <LightModeOutlined /> : <DarkModeOutlined />}
            </IconButton>
          </Stack>
        </Box>
        <Stack component="nav" aria-label="ناوبری فروشگاه" direction="row" sx={{ gap: { xs: 1.5, sm: 3 }, flexWrap: 'wrap' }}>
          {props.links.map(({ label, href }) => (
            <Link key={href} href={href} onClick={props.onLinkClick} underline="hover"
              aria-current={props.pathname === href ? 'page' : undefined}
              sx={{ display: 'flex', alignItems: 'center', minHeight: 40, color: props.pathname === href ? 'primary.main' : 'text.secondary', fontSize: '0.875rem', fontWeight: 650 }}>
              {label}
            </Link>
          ))}
        </Stack>
      </Container>
    </AppBar>
  );
}
