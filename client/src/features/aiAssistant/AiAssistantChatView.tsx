import { KeyboardEvent, MouseEvent, PointerEvent, useId, useRef, useState } from 'react';
import Close from '@mui/icons-material/Close';
import OpenInFull from '@mui/icons-material/OpenInFull';
import SendOutlined from '@mui/icons-material/SendOutlined';
import SmsOutlined from '@mui/icons-material/SmsOutlined';
import {
  Alert, Box, Dialog, DialogContent, Fab, IconButton, Stack, TextField, Typography
} from '@mui/material';

export interface ChatExchange {
  message: string;
  reply: string;
}

interface Props {
  open: boolean;
  draft: string;
  exchange?: ChatExchange;
  sending: boolean;
  sendFailed: boolean;
  onOpen: () => void;
  onClose: () => void;
  onDraftChange: (draft: string) => void;
  onSend: () => void;
}

interface ChatSize {
  width: number;
  height: number;
}

const resizeSteps: Record<string, ChatSize> = {
  ArrowRight: { width: 24, height: 0 },
  ArrowLeft: { width: -24, height: 0 },
  ArrowUp: { width: 0, height: 24 },
  ArrowDown: { width: 0, height: -24 }
};

export default function AiAssistantChatView({
  open, draft, exchange, sending, sendFailed, onOpen, onClose, onDraftChange, onSend
}: Props) {
  const titleId = useId();
  const draftId = useId();
  const paperRef = useRef<HTMLDivElement>(null);
  const dragStart = useRef<{ x: number; y: number; size: ChatSize; moved: boolean }>();
  const [size, setSize] = useState<ChatSize | 'expanded'>();

  function startResize(event: PointerEvent<HTMLButtonElement>) {
    const bounds = paperRef.current?.getBoundingClientRect();
    if (event.button !== 0 || !event.isPrimary || !bounds) return;

    dragStart.current = {
      x: event.clientX,
      y: event.clientY,
      size: bounds,
      moved: false
    };
    event.currentTarget.setPointerCapture(event.pointerId);
  }

  function dragResize(event: PointerEvent<HTMLButtonElement>) {
    const start = dragStart.current;
    if (!start || !event.currentTarget.hasPointerCapture(event.pointerId)) return;

    const widthChange = event.clientX - start.x;
    const heightChange = start.y - event.clientY;
    if (!start.moved && Math.hypot(widthChange, heightChange) < 4) return;

    start.moved = true;
    setSize({
      width: Math.max(0, start.size.width + widthChange),
      height: Math.max(0, start.size.height + heightChange)
    });
  }

  function resizeWithKeys(event: KeyboardEvent<HTMLButtonElement>) {
    const step = resizeSteps[event.key];
    const bounds = paperRef.current?.getBoundingClientRect();
    if (!step || !bounds) return;

    event.preventDefault();
    setSize({
      width: Math.max(0, bounds.width + step.width),
      height: Math.max(0, bounds.height + step.height)
    });
  }

  function toggleSize(event: MouseEvent<HTMLButtonElement>) {
    if (event.detail > 0 && dragStart.current?.moved) return;
    setSize((current) => current === 'expanded' ? undefined : 'expanded');
  }

  return (
    <>
      <Fab
        color="primary"
        dir="rtl"
        aria-label="باز کردن دستیار هوشمند"
        aria-haspopup="dialog"
        onClick={onOpen}
        sx={{
          position: 'fixed',
          insetInlineEnd: 'max(20px, env(safe-area-inset-left))',
          bottom: 'max(16px, env(safe-area-inset-bottom))',
          zIndex: 'fab',
          boxShadow: 6
        }}
      >
        <SmsOutlined />
      </Fab>
      <Dialog
        open={open}
        onClose={onClose}
        dir="rtl"
        aria-labelledby={titleId}
        PaperProps={{
          ref: paperRef,
          style: size === 'expanded' ? { width: '100%', height: '100%' } : size
        }}
        sx={{
          '& .MuiDialog-container': { alignItems: 'flex-end', justifyContent: 'flex-end' },
          '& .MuiDialog-paper': {
            m: '16px',
            width: 420,
            height: 520,
            minWidth: 'min(320px, calc(100% - 32px))',
            minHeight: 'min(340px, calc(100% - 32px))',
            maxWidth: 'calc(100% - 32px)',
            maxHeight: 'calc(100% - 32px)'
          }
        }}
      >
        <Box
          component="header"
          sx={{
            position: 'relative',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexShrink: 0,
            py: 2,
            paddingInlineStart: '48px',
            paddingInlineEnd: 3,
            bgcolor: 'primary.main',
            color: 'primary.contrastText'
          }}
        >
          <IconButton
            aria-label="تغییر اندازه گفتگو"
            aria-pressed={size === 'expanded'}
            title="کشیدن یا استفاده از کلیدهای جهت‌دار برای تغییر اندازه"
            size="small"
            color="inherit"
            onPointerDown={startResize}
            onPointerMove={dragResize}
            onKeyDown={resizeWithKeys}
            onClick={toggleSize}
            sx={{
              position: 'absolute',
              insetInlineStart: 6,
              top: 6,
              cursor: 'nesw-resize',
              touchAction: 'none',
              '&.Mui-focusVisible': { outlineColor: 'primary.contrastText' }
            }}
          >
            <OpenInFull fontSize="small" />
          </IconButton>
          <Typography id={titleId} variant="h6" component="h2">دستیار هوشمند</Typography>
          <IconButton aria-label="بستن دستیار هوشمند" color="inherit" edge="end" onClick={onClose}>
            <Close />
          </IconButton>
        </Box>
        <DialogContent dividers sx={{ bgcolor: 'background.default', minHeight: 0 }}>
          <Stack role="log" aria-label="پیام‌ها" spacing={2}>
            {exchange ? (
              <>
                <Box sx={{ alignSelf: 'flex-start', maxWidth: '90%', borderRadius: 2, p: 1.5, bgcolor: 'primary.main', color: 'primary.contrastText' }}>
                  <Typography variant="caption">شما</Typography>
                  <Typography dir="auto" sx={{ overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}>{exchange.message}</Typography>
                </Box>
                <Box sx={{ alignSelf: 'flex-end', maxWidth: '90%', borderRadius: 2, p: 1.5, bgcolor: 'background.paper', border: 1, borderColor: 'divider' }}>
                  <Typography variant="caption">دستیار هوشمند</Typography>
                  <Typography dir="auto" sx={{ overflowWrap: 'anywhere', whiteSpace: 'pre-wrap' }}>{exchange.reply}</Typography>
                </Box>
              </>
            ) : (
              <Typography color="text.secondary">پیام خود را برای دستیار هوشمند بفرستید.</Typography>
            )}
            {sending && <Typography color="text.secondary">در حال ارسال پیام…</Typography>}
          </Stack>
          {sendFailed && <Alert severity="error" sx={{ mt: 2 }}>ارسال پیام ناموفق بود. دوباره تلاش کنید.</Alert>}
        </DialogContent>
        <Box
          component="form"
          onSubmit={(event) => {
            event.preventDefault();
            onSend();
          }}
          sx={{ display: 'flex', alignItems: 'center', gap: 1, p: 2, flexShrink: 0 }}
        >
          <TextField
            id={draftId}
            label="پیام شما"
            value={draft}
            onChange={(event) => onDraftChange(event.target.value)}
            inputProps={{ readOnly: sending, dir: 'auto' }}
            size="small"
            fullWidth
            autoFocus
          />
          <IconButton type="submit" aria-label="ارسال پیام" color="primary" disabled={sending || !draft.trim()}>
            <SendOutlined sx={{ transform: 'scaleX(-1)' }} />
          </IconButton>
        </Box>
      </Dialog>
    </>
  );
}
