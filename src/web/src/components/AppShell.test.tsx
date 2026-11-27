import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppShell } from './AppShell';

const mocks = vi.hoisted(() => ({ useAuth: vi.fn() }));
vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));

function renderShell(role: 'HR' | 'Manager' | 'Employee', permissions: { p: string; s: string[] }[]) {
  mocks.useAuth.mockReturnValue({
    user: {
      id: 'user-1',
      fullName: 'Nguyễn An',
      email: 'user@example.com',
      role,
      accessToken: 'test-token',
      permissions,
    },
    signOut: vi.fn(),
  });

  return render(
    <MemoryRouter>
      <AppShell />
    </MemoryRouter>,
  );
}

describe('AppShell permission navigation', () => {
  beforeEach(() => vi.clearAllMocks());

  it('shows approval navigation for an Employee with both read and decide scopes', () => {
    renderShell('Employee', [
      { p: 'leaves.read', s: ['Self', 'Team'] },
      { p: 'leaves.decide', s: ['Team'] },
    ]);

    expect(screen.getByRole('link', { name: 'Hộp thư phê duyệt' })).toHaveAttribute('href', '/approvals');
    expect(screen.getByRole('link', { name: 'Lịch nghỉ trong phạm vi' })).toHaveAttribute('href', '/calendar');
    expect(screen.queryByRole('link', { name: 'Nhân viên' })).not.toBeInTheDocument();
  });

  it('hides approval navigation for a Manager without the decision permission', () => {
    renderShell('Manager', [
      { p: 'leaves.read', s: ['Self', 'Team'] },
      { p: 'employees.read', s: ['Self', 'Team'] },
    ]);

    expect(screen.queryByRole('link', { name: 'Hộp thư phê duyệt' })).not.toBeInTheDocument();
  });

  it('shows the read-only employee list only with employees.read:All', () => {
    renderShell('Employee', [
      { p: 'employees.read', s: ['All'] },
      { p: 'leaves.read', s: ['Self'] },
    ]);

    expect(screen.getByRole('link', { name: 'Nhân viên' })).toHaveAttribute('href', '/employees');
    expect(screen.getByRole('link', { name: 'Lịch nghỉ của tôi' })).toHaveAttribute('href', '/calendar');
    expect(screen.queryByRole('link', { name: 'Phòng ban' })).not.toBeInTheDocument();
  });
});
