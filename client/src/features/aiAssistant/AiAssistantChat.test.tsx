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
