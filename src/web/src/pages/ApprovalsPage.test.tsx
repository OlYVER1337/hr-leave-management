import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser, LeaveRequest } from '../types';
import { useAuth } from '../contexts';
import {
  decideLeaveRequest,
  getApprovalsInboxPage,
  getLeaveRequestsPage,
} from '../services/leaveService';
import { ApprovalsPage } from './ApprovalsPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  decideLeaveRequest: vi.fn(),
  getApprovalsInboxPage: vi.fn(),
  getLeaveRequestsPage: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  decideLeaveRequest: mocks.decideLeaveRequest,
  getApprovalsInboxPage: mocks.getApprovalsInboxPage,
  getLeaveRequestsPage: mocks.getLeaveRequestsPage,
}));

const manager: AuthUser = {
  id: 'manager-1',
  fullName: 'Trần Minh Anh',
  email: 'manager@example.com',
  role: 'Manager',
  accessToken: 'test-token',
};

const pendingRequest: LeaveRequest = {
  id: 'request-1',
  employeeId: 'employee-1',
  employeeName: 'Lê Hoàng Nam',
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
};

function makePendingRequest(index: number): LeaveRequest {
  const suffix = String(index).padStart(2, '0');
  return {
    ...pendingRequest,
    id: `request-${suffix}`,
    employeeId: `employee-${suffix}`,
    employeeName: `Nhân viên ${suffix}`,
    employeeCode: `E${suffix}`,
    reason: `Lý do nghỉ ${suffix}`,
  };
}

describe('ApprovalsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: manager,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getLeaveRequestsPage).mockResolvedValue({ items: [pendingRequest], totalCount: 1 });
    vi.mocked(getApprovalsInboxPage).mockResolvedValue({ items: [pendingRequest], totalCount: 1 });
    vi.mocked(decideLeaveRequest).mockResolvedValue(undefined);
  });

  it('approves a pending request and refreshes the inbox', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <ApprovalsPage />
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('button', { name: /Xem xét đơn/ }));
    expect(screen.getByRole('dialog', { name: 'Xem xét đơn nghỉ phép' })).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Ghi chú phê duyệt/), 'Đã sắp xếp nhân sự thay ca.');
    await user.click(screen.getByRole('button', { name: 'Phê duyệt đơn' }));

    await waitFor(() => {
      expect(decideLeaveRequest).toHaveBeenCalledWith(
        'request-1',
        'approve',
        'Đã sắp xếp nhân sự thay ca.',
      );
    });
    expect(mocks.notify).toHaveBeenCalledWith('Đã phê duyệt đơn nghỉ.');
    expect(getApprovalsInboxPage).toHaveBeenCalledWith(manager, expect.objectContaining({ page: 1, pageSize: 20 }));
    expect(getApprovalsInboxPage).toHaveBeenCalledTimes(expect.any(Number));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('shows an error notification when the decision fails', async () => {
    const user = userEvent.setup();
    vi.mocked(decideLeaveRequest).mockRejectedValue(new Error('Đơn đã được xử lý.'));
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <ApprovalsPage />
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('button', { name: /Xem xét đơn/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Phê duyệt đơn' }));

    await waitFor(() => {
      expect(mocks.notify).toHaveBeenCalledWith('Đơn đã được xử lý.', 'error');
    });
    expect(screen.getByRole('dialog', { name: 'Xem xét đơn nghỉ phép' })).toBeInTheDocument();
  });

  it('paginates approval requests and resets to the first page when searching', async () => {
    const user = userEvent.setup();
    const requests = Array.from({ length: 25 }, (_, index) => makePendingRequest(index + 1));
    const pageResult = async (query: { page: number; pageSize: number }) => ({
      items: requests.slice((query.page - 1) * query.pageSize, query.page * query.pageSize),
      totalCount: requests.length,
    });
    vi.mocked(getLeaveRequestsPage).mockImplementation(pageResult);
    vi.mocked(getApprovalsInboxPage).mockImplementation(pageResult);
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <ApprovalsPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Nhân viên 01')).toBeInTheDocument();
    expect(screen.getByText('Trang 1 / 3')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(screen.getByText('Nhân viên 11')).toBeInTheDocument();
    expect(screen.getByText('Trang 2 / 3')).toBeInTheDocument();

    await user.type(screen.getByRole('textbox', { name: 'Tìm đơn cần duyệt' }), 'Lý do nghỉ 20');
    expect(await screen.findByText('Trang 1 / 1')).toBeInTheDocument();
    expect(await screen.findByText('Nhân viên 20')).toBeInTheDocument();
    expect(screen.queryByText('Nhân viên 11')).not.toBeInTheDocument();
  });

  it('clamps the current page when a decision removes requests from the last page', async () => {
    const user = userEvent.setup();
    const initialRequests = Array.from({ length: 21 }, (_, index) => makePendingRequest(index + 1));
    const remainingRequests = initialRequests.slice(0, 20);
    const initialResult = async (query: { page: number; pageSize: number }) => ({
      items: initialRequests.slice((query.page - 1) * query.pageSize, query.page * query.pageSize),
      totalCount: initialRequests.length,
    });
    const remainingResult = async (query: { page: number; pageSize: number }) => ({
      items: remainingRequests.slice((query.page - 1) * query.pageSize, query.page * query.pageSize),
      totalCount: remainingRequests.length,
    });
    vi.mocked(getLeaveRequestsPage).mockImplementationOnce(initialResult).mockImplementation(remainingResult);
    vi.mocked(getApprovalsInboxPage).mockImplementationOnce(initialResult).mockImplementation(remainingResult);
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <ApprovalsPage />
      </MemoryRouter>,
    );

    await screen.findByText('Nhân viên 01');
    await user.click(screen.getByRole('button', { name: 'Trang 3' }));
    expect(await screen.findByText('Nhân viên 21')).toBeInTheDocument();
    expect(screen.getByText('Trang 3 / 3')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /Xem xét đơn/ }));
    await user.click(screen.getByRole('button', { name: 'Phê duyệt đơn' }));

    await waitFor(() => {
      expect(screen.getByText('Trang 2 / 2')).toBeInTheDocument();
      expect(screen.getByText('Nhân viên 11')).toBeInTheDocument();
    });
  });
});
