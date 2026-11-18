import { ImageOutlined } from '@mui/icons-material';
import { Box } from '@mui/material';
import { useState } from 'react';

interface Props {
  src?: string | null;
  alt: string;
  ratio?: string;
  eager?: boolean;
}

export default function StorefrontImage({ src, alt, ratio = '4 / 3', eager = false }: Props) {
  const [failedSource, setFailedSource] = useState<string>();
  const showImage = Boolean(src && src !== failedSource);

  return (
    <Box sx={{ aspectRatio: ratio, bgcolor: '#F1F3EF', borderRadius: 1, display: 'grid', placeItems: 'center', overflow: 'hidden' }}>
      {showImage ? <Box component="img" src={src!} alt={alt} loading={eager ? 'eager' : 'lazy'}
        onError={() => setFailedSource(src!)} sx={{ display: 'block', width: '100%', height: '100%', minHeight: 0, objectFit: 'contain' }} />
        : <Box role={alt ? 'img' : undefined} aria-label={alt ? `تصویر محصول در دسترس نیست: ${alt}` : undefined} sx={{ color: '#526258' }}><ImageOutlined aria-hidden /></Box>}
    </Box>
  );
}
