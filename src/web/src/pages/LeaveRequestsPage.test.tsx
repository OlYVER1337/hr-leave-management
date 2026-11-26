import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser, LeaveRequest } from '../types';
import { useAuth } from '../contexts';
import { getLeaveRequestsPage } from '../services/leaveService';
import { LeaveRequestsPage } from './LeaveRequestsPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  cancelLeaveRequest: vi.fn(),
  getLeaveRequestsPage: vi.fn(),
  getLeaveRequestHistory: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  cancelLeaveRequest: mocks.cancelLeaveRequest,
  getLeaveRequestsPage: mocks.getLeaveRequestsPage,
  getLeaveRequestHistory: mocks.getLeaveRequestHistory,
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
    vi.mocked(getLeaveRequestsPage).mockResolvedValue({ items: [request], totalCount: 1 });
  });

  it('opens request details in an accessible dialog and closes with Escape', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
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

  it('loads the history only when requested and renders the timeline details', async () => {
    const user = userEvent.setup();
    mocks.getLeaveRequestHistory.mockResolvedValue([{
      id: 'history-1',
      actorId: employee.id,
      action: 'Created',
      fromStatus: null,
      toStatus: 'Pending',
      note: 'Đã gửi yêu cầu',
      createdAtUtc: '2026-10-30T10:00:00Z',
    }]);

    render(
      <MemoryRouter>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('button', { name: 'Xem chi tiết request-1' }));
    expect(mocks.getLeaveRequestHistory).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Xem lịch sử' }));

    expect(await screen.findByText('Tạo đơn')).toBeInTheDocument();
    expect(screen.getByText('Người thực hiện: Nguyễn An (nhân viên)')).toBeInTheDocument();
    expect(screen.getByText('Khởi tạo → Chờ duyệt')).toBeInTheDocument();
    expect(screen.getByText('Ghi chú: Đã gửi yêu cầu')).toBeInTheDocument();
    expect(mocks.getLeaveRequestHistory).toHaveBeenCalledTimes(1);
    expect(mocks.getLeaveRequestHistory).toHaveBeenCalledWith('request-1');
  });

  it('shows an empty history state when the request has no history records', async () => {
    const user = userEvent.setup();
    mocks.getLeaveRequestHistory.mockResolvedValue([]);

    render(
      <MemoryRouter>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('button', { name: 'Xem chi tiết request-1' }));
    await user.click(screen.getByRole('button', { name: 'Xem lịch sử' }));

    expect(await screen.findByText('Chưa có bản ghi lịch sử cho đơn này.')).toBeInTheDocument();
  });

  it('shows a history loading error without hiding request details', async () => {
    const user = userEvent.setup();
    mocks.getLeaveRequestHistory.mockRejectedValue(new Error('Không có quyền xem lịch sử.'));

    render(
      <MemoryRouter>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('button', { name: 'Xem chi tiết request-1' }));
    await user.click(screen.getByRole('button', { name: 'Xem lịch sử' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Không có quyền xem lịch sử.');
    expect(screen.getByText('Việc gia đình cần giải quyết.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Xem lịch sử' })).toBeInTheDocument();
  });

  it('marks report export unavailable instead of offering a non-working action', async () => {
    render(
      <MemoryRouter>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(await screen.findByRole('button', { name: /Xuất báo cáo \(chưa khả dụng\)/ })).toBeDisabled();
  });

  it('shows a loading state while leave requests are being fetched', () => {
    vi.mocked(getLeaveRequestsPage).mockReturnValue(new Promise(() => {}));
    render(
      <MemoryRouter>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(screen.getByText('Đang tải danh sách đơn…')).toBeInTheDocument();
  });

  it('shows the request loading error', async () => {
    vi.mocked(getLeaveRequestsPage).mockRejectedValue(new Error('Không thể tải đơn nghỉ.'));
    render(
      <MemoryRouter>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Không thể tải đơn nghỉ.')).toBeInTheDocument();
  });

  it('shows the empty state when no leave requests exist', async () => {
    vi.mocked(getLeaveRequestsPage).mockResolvedValue({ items: [], totalCount: 0 });
    render(
      <MemoryRouter>
        <LeaveRequestsPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Không tìm thấy đơn nghỉ')).toBeInTheDocument();
  });
});
