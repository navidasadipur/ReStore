import { useEffect, useRef, useState } from 'react';
import agent from '../../app/api/agent';
import AiAssistantChatView, { ChatExchange } from './AiAssistantChatView';

export default function AiAssistantChat() {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState('');
  const [exchange, setExchange] = useState<ChatExchange>();
  const [sending, setSending] = useState(false);
  const [sendFailed, setSendFailed] = useState(false);
  const pending = useRef<AbortController>();

  useEffect(() => () => pending.current?.abort(), []);

  function close() {
    pending.current?.abort();
    pending.current = undefined;
    setOpen(false);
    setDraft('');
    setExchange(undefined);
    setSending(false);
    setSendFailed(false);
  }

  async function send() {
    if (pending.current || !draft.trim()) return;

    const request = new AbortController();
    pending.current = request;
    setSending(true);
    setSendFailed(false);

    try {
      const { message } = await agent.AiAssistant.chat(draft, request.signal);
      if (request.signal.aborted) return;
      setExchange({ message: draft, reply: message });
      setDraft('');
    } catch {
      if (!request.signal.aborted) setSendFailed(true);
    } finally {
      if (!request.signal.aborted) {
        pending.current = undefined;
        setSending(false);
      }
    }
  }

  return (
    <AiAssistantChatView
      open={open}
      draft={draft}
      exchange={exchange}
      sending={sending}
      sendFailed={sendFailed}
      onOpen={() => setOpen(true)}
      onClose={close}
      onDraftChange={setDraft}
      onSend={send}
    />
  );
}
