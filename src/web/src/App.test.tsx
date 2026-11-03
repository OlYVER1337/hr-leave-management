import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';

const mocks = vi.hoisted(() => ({ useAuth: vi.fn() }));

vi.mock('./contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('./pages/DashboardPage', () => ({
  DashboardPage: () => <div>Dashboard landing</div>,
}));
vi.mock('./pages/RequestLeavePage', () => ({
  RequestLeavePage: () => <div>Leave request form</div>,
}));

function renderApp(path: string, role: 'HR' | 'Manager' | 'Employee') {
  mocks.useAuth.mockReturnValue({
    user: {
      id: `${role.toLowerCase()}-1`,
      fullName: 'Nguyễn An',
      email: 'user@example.com',
      role,
      accessToken: 'test-token',
    },
    busy: false,
    restoreError: null,
    signIn: vi.fn(),
    signOut: vi.fn(),
  });
  return render(
    <MemoryRouter
      initialEntries={[path]}
      future={{ v7_startTransition: true, v7_relativeSplatPath: true }}
    >
      <App />
    </MemoryRouter>,
  );
}

describe('App role guards', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('redirects HR away from the leave request route', async () => {
    renderApp('/request', 'HR');

    expect(await screen.findByText('Dashboard landing')).toBeInTheDocument();
    expect(screen.queryByText('Leave request form')).not.toBeInTheDocument();
  });

  it.each(['Employee', 'Manager'] as const)('allows %s to open the leave request route', async (role) => {
    renderApp('/request', role);

    expect(await screen.findByText('Leave request form')).toBeInTheDocument();
  });
});
