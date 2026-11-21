import { useId } from 'react';
import AutoAwesomeOutlined from '@mui/icons-material/AutoAwesomeOutlined';
import Close from '@mui/icons-material/Close';
import SendOutlined from '@mui/icons-material/SendOutlined';
import {
  Alert, Box, Dialog, DialogContent, DialogTitle, Fab, IconButton, Stack, TextField, Typography
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

export default function AiAssistantChatView({
  open, draft, exchange, sending, sendFailed, onOpen, onClose, onDraftChange, onSend
}: Props) {
  const titleId = useId();
  const draftId = useId();

  return (
    <>
      <Fab
        color="primary"
        aria-label="باز کردن دستیار هوشمند"
        aria-haspopup="dialog"
        onClick={onOpen}
        sx={{ position: 'fixed', insetInlineEnd: 16, bottom: 16, zIndex: 'fab' }}
      >
        <AutoAwesomeOutlined />
      </Fab>
      <Dialog
        open={open}
        onClose={onClose}
        aria-labelledby={titleId}
        sx={{
          '& .MuiDialog-container': { alignItems: 'flex-end', justifyContent: 'flex-end' },
          '& .MuiDialog-paper': { m: 2, width: 420, maxWidth: 'calc(100% - 32px)', maxHeight: 'calc(100% - 32px)' }
        }}
      >
        <DialogTitle
          id={titleId}
          sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', bgcolor: 'primary.main', color: 'primary.contrastText' }}
        >
          دستیار هوشمند
          <IconButton aria-label="بستن دستیار هوشمند" color="inherit" edge="end" onClick={onClose}>
            <Close />
          </IconButton>
        </DialogTitle>
        <DialogContent dividers sx={{ bgcolor: 'background.default', height: 220 }}>
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
          sx={{ display: 'flex', alignItems: 'center', gap: 1, p: 2 }}
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
