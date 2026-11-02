import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser } from '../types';
import { useAuth } from '../contexts';
import {
  createLeaveRequest,
  getBalances,
  getHolidays,
  getRequestLeaveTypes,
} from '../services/leaveService';
import { RequestLeavePage } from './RequestLeavePage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  createLeaveRequest: vi.fn(),
  getBalances: vi.fn(),
  getHolidays: vi.fn(),
  getRequestLeaveTypes: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  createLeaveRequest: mocks.createLeaveRequest,
  getBalances: mocks.getBalances,
  getHolidays: mocks.getHolidays,
  getRequestLeaveTypes: mocks.getRequestLeaveTypes,
}));

const employee: AuthUser = {
  id: 'employee-1',
  fullName: 'Nguyễn An',
  email: 'employee@example.com',
  role: 'Employee',
  accessToken: 'test-token',
};

describe('RequestLeavePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: employee,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getRequestLeaveTypes).mockResolvedValue([
      {
        code: 'ANNUAL',
        name: 'Nghỉ phép năm',
        description: 'Nghỉ phép năm',
        deductsBalance: true,
      },
    ]);
    vi.mocked(getBalances).mockResolvedValue([]);
    vi.mocked(getHolidays).mockResolvedValue([]);
    vi.mocked(createLeaveRequest).mockResolvedValue(undefined);
  });

  it('submits a valid annual leave request and navigates to the request list', async () => {
    const user = userEvent.setup();
    render(
      <MemoryRouter
        initialEntries={['/request']}
        future={{ v7_startTransition: true, v7_relativeSplatPath: true }}
      >
        <Routes>
          <Route path="/request" element={<RequestLeavePage />} />
          <Route path="/leaves" element={<h1>Đơn nghỉ của tôi</h1>} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByRole('button', { name: /Nghỉ phép năm/ });
    fireEvent.change(screen.getByLabelText(/Ngày bắt đầu/), {
      target: { value: '2099-01-05' },
    });
    fireEvent.change(screen.getByLabelText(/Ngày kết thúc/), {
      target: { value: '2099-01-05' },
    });
    await user.type(screen.getByLabelText(/Lý do nghỉ/), 'Nghỉ việc gia đình');
    await user.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }));

    expect(await screen.findByRole('heading', { name: 'Đơn nghỉ của tôi' })).toBeInTheDocument();
    expect(createLeaveRequest).toHaveBeenCalledWith(employee, {
      typeCode: 'ANNUAL',
      startDate: '2099-01-05',
      endDate: '2099-01-05',
      reason: 'Nghỉ việc gia đình',
    });
    expect(mocks.notify).toHaveBeenCalledWith('Đã gửi đơn nghỉ phép để chờ phê duyệt.');
  });

  it('blocks submission when the selected dates contain no working days', async () => {
    render(
      <MemoryRouter
        initialEntries={['/request']}
        future={{ v7_startTransition: true, v7_relativeSplatPath: true }}
      >
        <Routes>
          <Route path="/request" element={<RequestLeavePage />} />
        </Routes>
      </MemoryRouter>,
    );

    await screen.findByRole('button', { name: /Nghỉ phép năm/ });
    fireEvent.change(screen.getByLabelText(/Ngày bắt đầu/), {
      target: { value: '2099-01-10' },
    });
    fireEvent.change(screen.getByLabelText(/Ngày kết thúc/), {
      target: { value: '2099-01-11' },
    });

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Gửi yêu cầu' })).toBeDisabled();
    });
    expect(createLeaveRequest).not.toHaveBeenCalled();
  });
});
