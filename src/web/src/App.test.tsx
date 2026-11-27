import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { PermissionGrant, UserRole } from './types';

const mocks = vi.hoisted(() => ({ useAuth: vi.fn() }));

vi.mock('./contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('./pages/DashboardPage', () => ({
  DashboardPage: () => <div>Dashboard landing</div>,
}));
vi.mock('./pages/RequestLeavePage', () => ({
  RequestLeavePage: () => <div>Leave request form</div>,
}));
vi.mock('./pages/ApprovalsPage', () => ({
  ApprovalsPage: () => <div>Approvals inbox</div>,
}));

function renderApp(path: string, role: UserRole, permissions: PermissionGrant[]) {
  mocks.useAuth.mockReturnValue({
    user: {
      id: `${role.toLowerCase()}-1`,
      fullName: 'Nguyễn An',
      email: 'user@example.com',
      role,
      accessToken: 'test-token',
      permissions,
    },
    busy: false,
    restoreError: null,
    signIn: vi.fn(),
    signOut: vi.fn(),
  });

  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
}

describe('App permission guards', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('cho phép HR tạo đơn cá nhân khi có leaves.create:Self', async () => {
    renderApp('/request', 'HR', [{ p: 'leaves.create', s: ['Self'] }]);

    expect(await screen.findByText('Leave request form')).toBeInTheDocument();
  });

  it('cho phép Employee duyệt khi được cấp leaves.decide:Team dù role legacy là Employee', async () => {
    renderApp('/approvals', 'Employee', [
      { p: 'leaves.decide', s: ['Team'] },
      { p: 'leaves.read', s: ['Team'] },
    ]);

    expect(await screen.findByText('Approvals inbox')).toBeInTheDocument();
  });

  it('không cho Manager vào trang duyệt khi thiếu leaves.decide:Team/All', async () => {
    renderApp('/approvals', 'Manager', [{ p: 'leaves.read', s: ['Self', 'Team'] }]);

    expect(await screen.findByText('Dashboard landing')).toBeInTheDocument();
    expect(screen.queryByText('Approvals inbox')).not.toBeInTheDocument();
  });

  it('không cho Employee mở inbox khi có quyền duyệt nhưng thiếu quyền đọc Team', async () => {
    renderApp('/approvals', 'Employee', [{ p: 'leaves.decide', s: ['Team'] }]);

    expect(await screen.findByText('Dashboard landing')).toBeInTheDocument();
    expect(screen.queryByText('Approvals inbox')).not.toBeInTheDocument();
  });

  it('không cho Employee tạo đơn khi thiếu leaves.create:Self', async () => {
    renderApp('/request', 'Employee', [{ p: 'leaves.read', s: ['Self'] }]);

    expect(await screen.findByText('Dashboard landing')).toBeInTheDocument();
    expect(screen.queryByText('Leave request form')).not.toBeInTheDocument();
  });
});
