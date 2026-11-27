import type { ReactNode } from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser, LeaveBalance, LeaveRequest } from '../types';
import { useAuth } from '../contexts';
import {
  getApprovalsInbox,
  getBalances,
  getLeaveRequests,
} from '../services/leaveService';
import { getDepartments, getEmployeePage } from '../services/hrService';
import { DashboardPage } from './DashboardPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  getApprovalsInbox: vi.fn(),
  getBalances: vi.fn(),
  getLeaveRequests: vi.fn(),
  getDepartments: vi.fn(),
  getEmployeePage: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  getApprovalsInbox: mocks.getApprovalsInbox,
  getBalances: mocks.getBalances,
  getLeaveRequests: mocks.getLeaveRequests,
}));
vi.mock('../services/hrService', () => ({
  getDepartments: mocks.getDepartments,
  getEmployeePage: mocks.getEmployeePage,
}));
vi.mock('recharts', () => {
  const Wrapper = ({ children }: { children?: ReactNode }) => <div>{children}</div>;
  return {
    Bar: Wrapper,
    BarChart: Wrapper,
    CartesianGrid: Wrapper,
    Cell: Wrapper,
    Pie: Wrapper,
    PieChart: Wrapper,
    ResponsiveContainer: Wrapper,
    Tooltip: Wrapper,
    XAxis: Wrapper,
    YAxis: Wrapper,
  };
});

const employee: AuthUser = {
  id: 'employee-1',
  fullName: 'Nguyễn An',
  email: 'employee@example.com',
  role: 'Employee',
  accessToken: 'test-token',
  permissions: [
    { p: 'employees.read', s: ['Self'] },
    { p: 'leaves.read', s: ['Self'] },
    { p: 'leaves.create', s: ['Self'] },
    { p: 'leaves.cancel', s: ['Self'] },
  ],
};
const manager: AuthUser = {
  id: 'manager-1',
  fullName: 'Trần Minh Anh',
  email: 'manager@example.com',
  role: 'Manager',
  accessToken: 'test-token',
  permissions: [
    { p: 'employees.read', s: ['Self', 'Team'] },
    { p: 'leaves.read', s: ['Self', 'Team'] },
    { p: 'leaves.create', s: ['Self'] },
    { p: 'leaves.cancel', s: ['Self'] },
    { p: 'leaves.decide', s: ['Team'] },
  ],
};
const hr: AuthUser = {
  id: 'hr-1',
  fullName: 'Lê Thu Hà',
  email: 'hr@example.com',
  role: 'HR',
  accessToken: 'test-token',
  permissions: [
    { p: 'employees.read', s: ['All'] },
    { p: 'employees.create', s: ['All'] },
    { p: 'employees.update', s: ['All'] },
    { p: 'employees.deactivate', s: ['All'] },
    { p: 'leaves.read', s: ['All'] },
    { p: 'leaves.create', s: ['Self'] },
    { p: 'leaves.decide', s: ['All'] },
    { p: 'leaves.cancel', s: ['Self'] },
    { p: 'departments.create', s: ['All'] },
    { p: 'departments.update', s: ['All'] },
    { p: 'departments.delete', s: ['All'] },
    { p: 'holidays.create', s: ['All'] },
    { p: 'holidays.update', s: ['All'] },
    { p: 'holidays.delete', s: ['All'] },
    { p: 'leave-policies.create', s: ['All'] },
    { p: 'leave-policies.update', s: ['All'] },
    { p: 'leave-types.create', s: ['All'] },
    { p: 'leave-types.update', s: ['All'] },
  ],
};

const balance: LeaveBalance = {
  code: 'ANNUAL',
  name: 'Nghỉ phép năm',
  entitled: 12,
  used: 2,
  pending: 1,
  available: 9,
  color: 'blue',
};

function request(overrides: Partial<LeaveRequest> = {}): LeaveRequest {
  return {
    id: 'request-1',
    employeeId: employee.id,
    employeeName: employee.fullName,
    employeeCode: 'E001',
    departmentName: 'Kỹ thuật',
    typeCode: 'ANNUAL',
    typeName: 'Nghỉ phép năm',
    startDate: '2099-01-05',
    endDate: '2099-01-05',
    days: 1,
    reason: 'Nghỉ việc gia đình',
    status: 'Pending',
    approverName: manager.fullName,
    createdAt: '2098-12-01T10:00:00Z',
    ...overrides,
  };
}

function renderDashboard() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  );
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: employee,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getApprovalsInbox).mockResolvedValue([]);
    vi.mocked(getBalances).mockResolvedValue([balance]);
    vi.mocked(getLeaveRequests).mockResolvedValue([]);
    vi.mocked(getDepartments).mockResolvedValue([]);
    vi.mocked(getEmployeePage).mockResolvedValue({ items: [], totalCount: 0 });
  });

  it('labels HR balance as the signed-in user’s balance', async () => {
    vi.mocked(useAuth).mockReturnValue({
      user: hr,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    renderDashboard();

    expect(await screen.findByRole('heading', { name: 'Số dư phép của tôi' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Tổng quan số dư phép' })).not.toBeInTheDocument();
    expect(await screen.findByRole('link', { name: /Tạo đơn nghỉ/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Xuất báo cáo \(chưa khả dụng\)/ })).toBeDisabled();
  });

  it('counts only the manager’s approval inbox, excluding their own pending request', async () => {
    vi.mocked(useAuth).mockReturnValue({
      user: manager,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getLeaveRequests).mockResolvedValue([
      request({ id: 'manager-request', employeeId: manager.id, employeeName: manager.fullName }),
      request({ id: 'team-request-1', employeeId: 'employee-2' }),
      request({ id: 'team-request-2', employeeId: 'employee-3', status: 'Cancelled' }),
    ]);
    vi.mocked(getApprovalsInbox).mockResolvedValue([request({ id: 'team-request-1', employeeId: 'employee-2' })]);
    const { container } = renderDashboard();

    expect(await screen.findByText('Có 1 yêu cầu đang chờ bạn phê duyệt.')).toBeInTheDocument();
    await screen.findByText('Tổng số đơn');
    const pendingCard = screen.getByText('Đơn chờ tôi phê duyệt').closest('.stat-card') as HTMLElement | null;
    expect(pendingCard).not.toBeNull();
    if (!pendingCard) throw new Error('Manager approval KPI card was not rendered.');
    expect(within(pendingCard).getByText('1')).toBeInTheDocument();
    const cancelledLegend = container.querySelector<HTMLElement>('.breakdown-legend');
    expect(cancelledLegend).not.toBeNull();
    if (!cancelledLegend) throw new Error('Request status legend was not rendered.');
    expect(within(cancelledLegend).getByText('Đã hủy')).toBeInTheDocument();
    expect(within(cancelledLegend).getByText('1')).toBeInTheDocument();
    expect(container.querySelector('.donut-center')).toHaveTextContent('3');
  });

  it('counts only the employee’s own pending requests', async () => {
    vi.mocked(getLeaveRequests).mockResolvedValue([
      request(),
      request({ id: 'another-employee-request', employeeId: 'employee-2' }),
    ]);
    renderDashboard();

    const pendingCard = screen.getByText('Đang chờ xử lý').closest('.stat-card') as HTMLElement | null;
    expect(pendingCard).not.toBeNull();
    if (!pendingCard) throw new Error('Employee pending KPI card was not rendered.');
    await waitFor(() => expect(within(pendingCard).getByText('1')).toBeInTheDocument());
  });
});
