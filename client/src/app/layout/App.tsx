import { CacheProvider } from '@emotion/react';
import { Container, CssBaseline } from "@mui/material";
import ThemeProvider from "@mui/material/styles/ThemeProvider";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Outlet, ScrollRestoration } from "react-router-dom";
import Header from "./Header";
import { ToastContainer } from "react-toastify";
import "react-toastify/dist/ReactToastify.css";
import LoadingComponent from "./LoadingComponent";
import { useAppDispatch } from "../store/configureStore";
import { fetchBasketAsync } from "../../features/basket/basketSlice";
import { fetchCurrentUser } from "../../features/account/accountSlice";
import { createStorefrontTheme, rtlCache } from './storefrontTheme';

function App() {
  const dispatch = useAppDispatch();
  const [loading, setLoading] = useState(true);

  const initApp = useCallback(async () => {
    try {
      await dispatch(fetchCurrentUser());
      await dispatch(fetchBasketAsync());
    } catch (error: any) {
      console.log(error);
    }
  }, [dispatch])

  useEffect(() => {
    initApp().then(() => setLoading(false));
  }, [initApp])

  const [darkMode, setDarkMode] = useState(false);
  const theme = useMemo(() => createStorefrontTheme(darkMode), [darkMode]);

  function handleThemeChange() {
    setDarkMode((prev) => !prev);
  }

  return (
    <CacheProvider value={rtlCache}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <ScrollRestoration />
        <ToastContainer theme="colored" position="bottom-right" hideProgressBar rtl />
        <Header isDarkMode={darkMode} handleThemeChange={handleThemeChange} />
        <Container component="main" maxWidth="lg" sx={{ pt: { xs: 2, sm: 3 }, pb: 5 }}>
          {loading ? <LoadingComponent message="در حال آماده‌سازی فروشگاه…" /> : <Outlet />}
        </Container>
      </ThemeProvider>
    </CacheProvider>
  );
}

export default App;
