import { PersonOutline } from '@mui/icons-material';
import { Button, Menu, MenuItem, Typography } from '@mui/material';
import { MouseEventHandler, useId, useState } from 'react';

interface Props {
  email: string;
  onLinkClick: MouseEventHandler<HTMLAnchorElement>;
  onSignOut: () => void;
}

export default function SignedInMenu({ email, onLinkClick, onSignOut }: Props) {
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null);
  const id = useId();
  const close = () => setAnchorEl(null);

  return (
    <>
      <Button onClick={event => setAnchorEl(event.currentTarget)} startIcon={<PersonOutline />}
        aria-haspopup="menu" aria-expanded={Boolean(anchorEl)} aria-controls={anchorEl ? id : undefined} sx={{ whiteSpace: 'nowrap' }}>
        حساب
      </Button>
      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={close} MenuListProps={{ id, 'aria-label': 'حساب کاربری' }}>
        <Typography component="li" dir="ltr" sx={{ px: 2, py: 1, maxWidth: 280, overflowWrap: 'anywhere', color: 'text.secondary' }}>{email}</Typography>
        <MenuItem component="a" href="/orders" onClick={event => { onLinkClick(event); close(); }}>سفارش‌های من</MenuItem>
        <MenuItem onClick={() => { close(); onSignOut(); }}>خروج از حساب</MenuItem>
      </Menu>
    </>
  );
}
