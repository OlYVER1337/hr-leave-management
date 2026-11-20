import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ToastProvider, useToast } from './ToastProvider';

function ToastEmitter() {
  const notify = useToast();
  return (
    <button onClick={() => {
      notify('Dữ liệu không hợp lệ: Ngày hiệu lực không hợp lệ.', 'error');
      notify('Dữ liệu không hợp lệ: Ngày hiệu lực không hợp lệ.', 'error');
    }}>
      Gửi hai thông báo trùng
    </button>
  );
}

describe('ToastProvider', () => {
  it('deduplicates identical messages while the first toast is visible', () => {
    render(
      <ToastProvider>
        <ToastEmitter />
      </ToastProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Gửi hai thông báo trùng' }));

    expect(screen.getAllByRole('status')).toHaveLength(1);
    expect(screen.getByText('Dữ liệu không hợp lệ: Ngày hiệu lực không hợp lệ.')).toBeInTheDocument();
  });
});
