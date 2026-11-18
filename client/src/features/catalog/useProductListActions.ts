import useStorefrontLink from '../../app/router/useStorefrontLink';
import { useAppDispatch, useAppSelector } from '../../app/store/configureStore';
import { addBasketItemAsync } from '../basket/basketSlice';

export default function useProductListActions() {
  const dispatch = useAppDispatch();
  const { basket, status } = useAppSelector(state => state.basket);
  return {
    busy: status !== 'idle',
    addingId: status.startsWith('pendingAddItem') ? Number(status.slice('pendingAddItem'.length)) : undefined,
    basketQuantities: new Map((basket?.items ?? []).map(item => [item.productId, item.quantity])),
    productHref: (id: number) => `/catalog/${id}`,
    onLinkClick: useStorefrontLink(),
    onAdd: (productId: number) => { dispatch(addBasketItemAsync({ productId })); },
  };
}
