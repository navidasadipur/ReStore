import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import agent from '../../app/api/agent';
import AiAssistantChat from './AiAssistantChat';

jest.mock('../../app/api/agent', () => ({
  __esModule: true,
  default: { AiAssistant: { chat: jest.fn() } }
}));

test('replaces the previous exchange and clears the chat on close', async () => {
  const chat = jest.mocked(agent.AiAssistant.chat);
  chat.mockResolvedValueOnce({ message: 'پاسخ اول' }).mockResolvedValueOnce({ message: 'پاسخ دوم' });
  render(<AiAssistantChat />);

  const launcher = screen.getByRole('button', { name: 'باز کردن دستیار هوشمند' });
  fireEvent.click(launcher);
  const dialog = within(screen.getByRole('dialog'));
  const input = dialog.getByRole('textbox', { name: 'پیام شما' });
  const send = dialog.getByRole('button', { name: 'ارسال پیام' });

  fireEvent.change(input, { target: { value: 'پیام اول' } });
  fireEvent.click(send);
  expect(await dialog.findByText('پاسخ اول')).toBeInTheDocument();
  expect(chat).toHaveBeenNthCalledWith(1, 'پیام اول', expect.any(AbortSignal));

  fireEvent.change(input, { target: { value: 'پیام دوم' } });
  fireEvent.click(send);
  expect(await dialog.findByText('پاسخ دوم')).toBeInTheDocument();
  expect(dialog.queryByText('پیام اول')).toBeNull();
  expect(dialog.queryByText('پاسخ اول')).toBeNull();

  fireEvent.click(dialog.getByRole('button', { name: 'بستن دستیار هوشمند' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  fireEvent.click(launcher);
  expect(screen.queryByText('پاسخ دوم')).toBeNull();
  expect(screen.getByRole('textbox', { name: 'پیام شما' })).toHaveValue('');
});

test('resizes from the upper-right corner without maximizing when the drag ends', () => {
  render(<AiAssistantChat />);
  fireEvent.click(screen.getByRole('button', { name: 'باز کردن دستیار هوشمند' }));
  const dialog = screen.getByRole('dialog', { name: 'دستیار هوشمند' });
  const handle = within(dialog).getByRole('button', { name: 'تغییر اندازه گفتگو' });

  jest.spyOn(dialog, 'getBoundingClientRect').mockReturnValue({ width: 420, height: 520 } as DOMRect);
  handle.setPointerCapture = jest.fn();
  handle.hasPointerCapture = jest.fn().mockReturnValue(true);

  const start = new MouseEvent('pointerdown', { bubbles: true, clientX: 430, clientY: 140 });
  Object.defineProperties(start, { pointerId: { value: 1 }, isPrimary: { value: true } });
  fireEvent(handle, start);
  fireEvent(handle, new MouseEvent('pointermove', { bubbles: true, clientX: 530, clientY: 60 }));
  fireEvent.click(handle, { detail: 1 });

  expect(handle.setPointerCapture).toHaveBeenCalledWith(1);
  expect(dialog).toHaveStyle({ width: '520px', height: '600px' });
});
