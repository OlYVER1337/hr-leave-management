import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuth } from '../contexts';
import { LoginPage } from './LoginPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  signIn: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/config', () => ({ DEMO_MODE: false }));

describe('LoginPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: null,
      busy: false,
      restoreError: null,
      signIn: mocks.signIn,
      signOut: vi.fn(),
    });
  });

  it('submits credentials, shows success notification, and navigates to the app', async () => {
    const user = userEvent.setup();
    mocks.signIn.mockResolvedValue(undefined);
    render(
      <MemoryRouter
        initialEntries={['/login']}
        future={{ v7_startTransition: true, v7_relativeSplatPath: true }}
      >
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<h1>Trang tổng quan</h1>} />
        </Routes>
      </MemoryRouter>,
    );

    await user.type(screen.getByLabelText('Email công việc'), 'employee@example.com');
    await user.type(screen.getByLabelText('Mật khẩu'), 'secure-password');
    await user.click(screen.getByRole('button', { name: /Đăng nhập Peopleflow/i }));

    expect(await screen.findByRole('heading', { name: 'Trang tổng quan' })).toBeInTheDocument();
    expect(mocks.signIn).toHaveBeenCalledWith('employee@example.com', 'secure-password');
    expect(mocks.notify).toHaveBeenCalledWith('Đăng nhập thành công.');
  });

  it('shows an authentication error without navigating away', async () => {
    const user = userEvent.setup();
    mocks.signIn.mockRejectedValue(new Error('Thông tin đăng nhập không chính xác.'));
    render(
      <MemoryRouter
        initialEntries={['/login']}
        future={{ v7_startTransition: true, v7_relativeSplatPath: true }}
      >
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/" element={<h1>Trang tổng quan</h1>} />
        </Routes>
      </MemoryRouter>,
    );

    await user.type(screen.getByLabelText('Email công việc'), 'wrong@example.com');
    await user.type(screen.getByLabelText('Mật khẩu'), 'wrong-password');
    fireEvent.submit(screen.getByRole('button', { name: /Đăng nhập Peopleflow/i }).closest('form')!);

    expect(await screen.findByText('Thông tin đăng nhập không chính xác.')).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Trang tổng quan' })).not.toBeInTheDocument();
    await waitFor(() => expect(mocks.notify).not.toHaveBeenCalled());
  });
});
