import createCache from '@emotion/cache';
import { createTheme } from '@mui/material/styles';
import { faIR } from '@mui/material/locale';
import { prefixer } from 'stylis';
import rtlPlugin from 'stylis-plugin-rtl';
import '@fontsource-variable/vazirmatn';

export const rtlCache = createCache({ key: 'restore-rtl', stylisPlugins: [prefixer, rtlPlugin] });

export function createStorefrontTheme(darkMode: boolean) {
  const primary = darkMode ? '#A8D8BA' : '#224938';
  return createTheme({
    direction: 'rtl',
    palette: {
      mode: darkMode ? 'dark' : 'light',
      primary: { main: primary, contrastText: darkMode ? '#14271D' : '#FFFFFF' },
      secondary: { main: darkMode ? '#C7D7AB' : '#496140' },
      background: { default: darkMode ? '#141A16' : '#F8F8F5', paper: darkMode ? '#1D2721' : '#FFFFFF' },
      text: { primary: darkMode ? '#F0F5EF' : '#1B2820', secondary: darkMode ? '#B6C7BA' : '#4E6255' },
      divider: darkMode ? '#435247' : '#DFE6DE',
    },
    shape: { borderRadius: 10 },
    typography: {
      fontFamily: '"Vazirmatn Variable", Tahoma, "Segoe UI", sans-serif',
      h4: { fontWeight: 750 },
      h5: { fontWeight: 700 },
      h6: { fontWeight: 700 },
      button: { fontWeight: 650, textTransform: 'none' },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          ':focus-visible': { outline: `2px solid ${primary}`, outlineOffset: 3 },
        },
      },
      MuiButtonBase: {
        styleOverrides: { root: { '&.Mui-focusVisible': { outline: `2px solid ${primary}`, outlineOffset: 2 } } },
      },
      MuiButton: { defaultProps: { disableElevation: true } },
    },
  }, faIR);
}
