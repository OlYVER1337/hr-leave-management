import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser, LeaveRequest } from '../types';
import { useAuth } from '../contexts';
import {
  decideLeaveRequest,
  getApprovalsInbox,
  getLeaveRequests,
} from '../services/leaveService';
import { ApprovalsPage } from './ApprovalsPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  decideLeaveRequest: vi.fn(),
  getApprovalsInbox: vi.fn(),
  getLeaveRequests: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  decideLeaveRequest: mocks.decideLeaveRequest,
  getApprovalsInbox: mocks.getApprovalsInbox,
  getLeaveRequests: mocks.getLeaveRequests,
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
    vi.mocked(getLeaveRequests).mockResolvedValue([pendingRequest]);
    vi.mocked(getApprovalsInbox).mockResolvedValue([pendingRequest]);
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
    expect(getApprovalsInbox).toHaveBeenCalledTimes(2);
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
});
