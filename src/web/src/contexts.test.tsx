import { act, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from './contexts';
import type { AuthUser } from './types';

const mocks = vi.hoisted(() => ({
  getStoredUser: vi.fn(),
  clearSession: vi.fn(),
  login: vi.fn(),
  logout: vi.fn(),
  restoreSession: vi.fn(),
}));

vi.mock('./services/authService', () => ({
  getStoredUser: mocks.getStoredUser,
  clearSession: mocks.clearSession,
  login: mocks.login,
  logout: mocks.logout,
  restoreSession: mocks.restoreSession,
}));

vi.mock('./services/config', () => ({ DEMO_MODE: false }));
vi.mock('./services/apiClient', () => ({ getApiErrorMessage: vi.fn(() => 'API error') }));

const initialUser: AuthUser = {
  id: 'employee-1',
  fullName: 'Nguyễn An',
  email: 'employee@example.com',
  role: 'Employee',
  accessToken: 'token-before-refresh',
  permissions: [{ p: 'leaves.read', s: ['Self'] }],
};

function SessionProbe() {
  const { user } = useAuth();
  return <output data-testid="permissions">{user?.permissions.map((grant) => grant.p).join(',') ?? 'signed-out'}</output>;
}

describe('AuthContext permission refresh', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.getStoredUser.mockReturnValue(initialUser);
  });

  it('đồng bộ grant mới sau refresh khi event thuộc đúng user đang đăng nhập', async () => {
    render(<AuthProvider><SessionProbe /></AuthProvider>);

    expect(await screen.findByTestId('permissions')).toHaveTextContent('leaves.read');

    const refreshedUser: AuthUser = {
      ...initialUser,
      accessToken: 'token-after-refresh',
      permissions: [
        { p: 'leaves.read', s: ['Self'] },
        { p: 'leaves.decide', s: ['Team'] },
      ],
    };

    act(() => {
      window.dispatchEvent(new CustomEvent<AuthUser>('auth:session-updated', { detail: refreshedUser }));
    });

    await waitFor(() => {
      expect(screen.getByTestId('permissions')).toHaveTextContent('leaves.read,leaves.decide');
    });
  });

  it('bỏ qua event refresh thuộc một tài khoản khác', async () => {
    render(<AuthProvider><SessionProbe /></AuthProvider>);

    expect(await screen.findByTestId('permissions')).toHaveTextContent('leaves.read');

    const differentUser: AuthUser = {
      ...initialUser,
      id: 'another-employee',
      permissions: [{ p: 'leaves.decide', s: ['All'] }],
    };

    act(() => {
      window.dispatchEvent(new CustomEvent<AuthUser>('auth:session-updated', { detail: differentUser }));
    });

    expect(screen.getByTestId('permissions')).toHaveTextContent('leaves.read');
    expect(screen.getByTestId('permissions')).not.toHaveTextContent('leaves.decide');
  });
});
