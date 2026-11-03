import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser, LeaveRequest } from '../types';
import { useAuth } from '../contexts';
import { getCalendarLeaves, getHolidays } from '../services/leaveService';
import { CalendarPage } from './CalendarPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  getCalendarLeaves: vi.fn(),
  getHolidays: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../services/leaveService', () => ({
  getCalendarLeaves: mocks.getCalendarLeaves,
  getHolidays: mocks.getHolidays,
}));

const employee: AuthUser = {
  id: 'employee-1',
  fullName: 'Nguyễn An',
  email: 'employee@example.com',
  role: 'Employee',
  accessToken: 'test-token',
};

function todayIso() {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

const request: LeaveRequest = {
  id: 'request-1',
  employeeId: employee.id,
  employeeName: employee.fullName,
  employeeCode: 'E001',
  departmentName: 'Kỹ thuật',
  typeCode: 'ANNUAL',
  typeName: 'Nghỉ phép năm',
  startDate: todayIso(),
  endDate: todayIso(),
  days: 1,
  reason: 'Personal leave',
  status: 'Approved',
  approverName: 'Quản lý',
  createdAt: new Date().toISOString(),
};

describe('CalendarPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: employee,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getCalendarLeaves).mockResolvedValue([request]);
    vi.mocked(getHolidays).mockResolvedValue([
      { id: 'holiday-1', name: 'Ngày lễ demo', date: todayIso() },
    ]);
  });

  it('shows approved leave and holidays on the selected day', async () => {
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <CalendarPage />
      </MemoryRouter>,
    );

    expect(await screen.findByText('Ngày lễ demo')).toBeInTheDocument();
    expect(await screen.findAllByText('Nguyễn An')).not.toHaveLength(0);
    expect(getCalendarLeaves).toHaveBeenCalledWith(
      employee,
      expect.stringMatching(/^\d{4}-\d{2}-01$/),
      expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
    );
  });
});
