import { useLocation, useNavigate } from 'react-router-dom';
import { signOut } from '../../features/account/accountSlice';
import { catalogHref } from '../../features/catalog/catalogLocation';
import useStorefrontLink from '../router/useStorefrontLink';
import { useAppDispatch, useAppSelector } from '../store/configureStore';
import StorefrontHeader from './StorefrontHeader';

interface Props {
  isDarkMode: boolean;
  handleThemeChange: () => void;
}

export default function Header({ isDarkMode, handleThemeChange }: Props) {
  const { pathname, search } = useLocation();
  const navigate = useNavigate();
  const onLinkClick = useStorefrontLink();
  const dispatch = useAppDispatch();
  const user = useAppSelector(state => state.account.user);
  const basket = useAppSelector(state => state.basket.basket);
  const basketCount = basket?.items.reduce((count, item) => count + item.quantity, 0) ?? 0;
  const searchValue = pathname === '/catalog' ? new URLSearchParams(search).get('q') ?? '' : '';
  const links = [
    { label: 'همه محصولات', href: '/catalog' },
    { label: 'درباره ما', href: '/about' },
    { label: 'تماس با ما', href: '/contact' },
  ];
  if (user?.roles?.includes('Admin')) links.push({ label: 'مدیریت محصولات', href: '/inventory' });

  return (
    <StorefrontHeader
      basketCount={basketCount}
      accountEmail={user?.email}
      links={links}
      pathname={pathname}
      searchValue={searchValue}
      isDarkMode={isDarkMode}
      onThemeChange={handleThemeChange}
      onLinkClick={onLinkClick}
      onSearch={query => navigate(catalogHref({ query }))}
      onSignOut={() => { dispatch(signOut()); navigate('/'); }}
    />
  );
}
