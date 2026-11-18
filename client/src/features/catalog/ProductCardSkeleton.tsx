import { Card, Skeleton, Stack } from '@mui/material';

export default function ProductCardSkeleton() {
  return (
    <Card variant="outlined" aria-hidden sx={{ p: { xs: 1, sm: 1.5 } }}>
      <Stack spacing={1}>
        <Skeleton variant="rounded" sx={{ aspectRatio: '4 / 3', height: 'auto' }} />
        <Skeleton variant="text" height={22} />
        <Skeleton variant="text" width="72%" height={22} />
        <Skeleton variant="rounded" height={40} />
      </Stack>
    </Card>
  );
}
