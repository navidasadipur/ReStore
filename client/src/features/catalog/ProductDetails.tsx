import { useCallback } from 'react';
import { useParams } from 'react-router-dom';
import agent from '../../app/api/agent';
import useStorefrontQuery from '../../app/hooks/useStorefrontQuery';
import useStorefrontLink from '../../app/router/useStorefrontLink';
import { useAppDispatch, useAppSelector } from '../../app/store/configureStore';
import { addBasketItemAsync, removeBasketItemAsync } from '../basket/basketSlice';
import ProductDetailsView from './ProductDetailsView';

export default function ProductDetails() {
  const { id } = useParams<{ id: string }>();
  const productId = id && /^\d+$/.test(id) ? Number(id) : 0;
  const validId = productId > 0 && productId <= 2147483647;
  const fetchProduct = useCallback(() => validId ? agent.Catalog.find(productId) : Promise.resolve(null), [productId, validId]);
  const { result, retry } = useStorefrontQuery(fetchProduct);
  const { basket, status } = useAppSelector(state => state.basket);
  const dispatch = useAppDispatch();
  const onLinkClick = useStorefrontLink();
  const quantityInBasket = basket?.items.find(item => item.productId === productId)?.quantity ?? 0;
  const unavailable = !validId || (result.status === 'error' && result.error.status === 404);
  const error = result.status === 'error' && !unavailable;
  const product = result.status === 'ready' ? result.data : null;

  async function updateBasket(quantity: number) {
    if (!product || status !== 'idle' || !Number.isInteger(quantity) || quantity < 0) return;
    const delta = quantity - quantityInBasket;
    if (delta > 0 && (product.quantityInStock === undefined || quantity <= product.quantityInStock)) {
      await dispatch(addBasketItemAsync({ productId, quantity: delta })).unwrap();
    } else if (delta < 0) {
      await dispatch(removeBasketItemAsync({ productId, quantity: -delta })).unwrap();
    }
  }

  return <ProductDetailsView product={product} loading={!unavailable && result.status === 'loading'} error={error}
    onRetry={retry} catalogHref="/catalog" onLinkClick={onLinkClick} quantityInBasket={quantityInBasket}
    busy={status !== 'idle'} onUpdateBasket={updateBasket} />;
}
