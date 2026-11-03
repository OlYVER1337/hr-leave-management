import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser, LeaveRequest } from '../types';
import { useAuth } from '../contexts';
import { getLeaveRequests } from '../services/leaveService';
import { LeaveRequestsPage } from './LeaveRequestsPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  cancelLeaveRequest: vi.fn(),
  getLeaveRequests: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  cancelLeaveRequest: mocks.cancelLeaveRequest,
  getLeaveRequests: mocks.getLeaveRequests,
}));

const employee: AuthUser = {
  id: 'employee-1',
  fullName: 'Nguyễn An',
  email: 'employee@example.com',
  role: 'Employee',
  accessToken: 'test-token',
};

const request: LeaveRequest = {
  id: 'request-1',
  employeeId: employee.id,
  employeeName: employee.fullName,
  employeeCode: 'E001',
  departmentName: 'Kỹ thuật',
  typeCode: 'ANNUAL',
  typeName: 'Nghỉ phép năm',
  startDate: '2026-11-05',
  endDate: '2026-11-06',
  days: 2,
  reason: 'Việc gia đình cần giải quyết.',
  status: 'Rejected',
  approverName: 'Trần Minh Anh',
  createdAt: '2026-10-30T10:00:00Z',
  decisionNote: 'Vui lòng trao đổi lại với quản lý.',
};

describe('LeaveRequestsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: employee,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getLeaveRequests).mockResolvedValue([request]);
  });

  it('opens request details in an accessible dialog and closes with Escape', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    const viewButton = await screen.findByRole('button', { name: 'Xem chi tiết request-1' });
    await user.click(viewButton);
    const dialog = screen.getByRole('dialog', { name: 'Chi tiết đơn request-1' });
    expect(dialog).toHaveTextContent('Việc gia đình cần giải quyết.');
    expect(dialog).toHaveTextContent('Vui lòng trao đổi lại với quản lý.');
    const closeButtons = within(dialog).getAllByRole('button', { name: 'Đóng' });
    expect(document.activeElement).toBe(closeButtons[0]);
    await user.tab({ shift: true });
    expect(document.activeElement).toBe(closeButtons[1]);
    await user.tab();
    expect(document.activeElement).toBe(closeButtons[0]);

    await user.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(document.activeElement).toBe(viewButton);
  });

  it('marks report export unavailable instead of offering a non-working action', async () => {
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(await screen.findByRole('button', { name: /Xuất báo cáo \(chưa khả dụng\)/ })).toBeDisabled();
  });

  it('shows a loading state while leave requests are being fetched', () => {
    vi.mocked(getLeaveRequests).mockReturnValue(new Promise(() => {}));
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(screen.getByText('Đang tải danh sách đơn…')).toBeInTheDocument();
  });

  it('shows the request loading error', async () => {
    vi.mocked(getLeaveRequests).mockRejectedValue(new Error('Không thể tải đơn nghỉ.'));
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Không thể tải đơn nghỉ.')).toBeInTheDocument();
  });

  it('shows the empty state when no leave requests exist', async () => {
    vi.mocked(getLeaveRequests).mockResolvedValue([]);
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Không tìm thấy đơn nghỉ')).toBeInTheDocument();
  });
});
