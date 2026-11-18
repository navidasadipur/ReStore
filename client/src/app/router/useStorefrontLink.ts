import { MouseEvent } from 'react';
import { useNavigate } from 'react-router-dom';

export default function useStorefrontLink() {
  const navigate = useNavigate();
  return (event: MouseEvent<HTMLAnchorElement>) => {
    if (event.defaultPrevented || event.button !== 0 || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const href = event.currentTarget.getAttribute('href');
    if (!href?.startsWith('/') || href.startsWith('//')) return;

    event.preventDefault();
    navigate(href);
  };
}
