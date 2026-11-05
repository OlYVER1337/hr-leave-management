import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Holiday, LeavePolicy } from '../types';
import {
  getHolidays,
  getPolicies,
  restoreBuiltInLeaveType,
  saveHolidays,
  savePolicies,
} from '../services/leaveService';
import { PoliciesPage } from './PoliciesPage';

const mocks = vi.hoisted(() => ({
  notify: vi.fn(),
  getHolidays: vi.fn(),
  getPolicies: vi.fn(),
  restoreBuiltInLeaveType: vi.fn(),
  saveHolidays: vi.fn(),
  savePolicies: vi.fn(),
}));

vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  getHolidays: mocks.getHolidays,
  getPolicies: mocks.getPolicies,
  restoreBuiltInLeaveType: mocks.restoreBuiltInLeaveType,
  saveHolidays: mocks.saveHolidays,
  savePolicies: mocks.savePolicies,
}));

const futureYear = new Date().getFullYear() + 1;
const annual: LeavePolicy = {
  id: 'annual',
  year: futureYear,
  leaveTypeId: 1,
  typeCode: 'ANNUAL',
  typeName: 'Nghỉ phép năm',
  daysPerYear: 12,
  paid: true,
  deductsBalance: true,
  description: 'Nghỉ phép năm',
  isActive: true,
};
const sick: LeavePolicy = {
  ...annual,
  id: 'sick',
  leaveTypeId: 2,
  typeCode: 'SICK',
  typeName: 'Nghỉ ốm',
  daysPerYear: 10,
};
const unpaid: LeavePolicy = {
  ...annual,
  id: 'unpaid',
  leaveTypeId: 3,
  typeCode: 'UNPAID',
  typeName: 'Nghỉ không lương',
  daysPerYear: null,
  paid: false,
  deductsBalance: false,
};
const policies = [annual, sick, unpaid];
const holidays: Holiday[] = [];

function renderPoliciesPage() {
  return render(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <PoliciesPage />
    </MemoryRouter>,
  );
}

describe('PoliciesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    vi.mocked(getPolicies).mockResolvedValue(policies);
    vi.mocked(getHolidays).mockResolvedValue(holidays);
    vi.mocked(saveHolidays).mockResolvedValue(holidays);
    vi.mocked(savePolicies).mockResolvedValue(undefined);
    vi.mocked(restoreBuiltInLeaveType).mockResolvedValue(undefined);
  });

  it('shows only the three built-in leave types and has no create-type action', async () => {
    renderPoliciesPage();

    expect(await screen.findByRole('heading', { name: 'Nghỉ phép năm' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Nghỉ ốm' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Nghỉ không lương' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Thêm loại nghỉ' })).not.toBeInTheDocument();
  });

  it('keeps active policy actions available without a delete action', async () => {
    renderPoliciesPage();

    expect(await screen.findAllByRole('button', { name: 'Sửa chính sách' })).not.toHaveLength(0);
    expect(screen.queryByRole('button', { name: /Xóa chính sách/ })).not.toBeInTheDocument();
  });

  it('locks policy editing for the current year', async () => {
    const user = userEvent.setup();
    renderPoliciesPage();

    await screen.findByRole('heading', { name: 'Nghỉ phép năm' });
    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Năm áp dụng' }),
      String(new Date().getFullYear()),
    );

    await waitFor(() => {
      expect(screen.getAllByRole('button', { name: 'Chính sách đã khóa' })).toHaveLength(3);
    });
    expect(screen.getAllByRole('button', { name: 'Chính sách đã khóa' }).every(
      (button) => (button as HTMLButtonElement).disabled,
    )).toBe(true);
  });

  it('restores a deleted built-in policy', async () => {
    const user = userEvent.setup();
    vi.mocked(getPolicies)
      .mockResolvedValueOnce([annual, { ...sick, isActive: false }, unpaid])
      .mockResolvedValueOnce(policies);
    renderPoliciesPage();

    await user.click(await screen.findByRole('button', { name: 'Thêm lại chính sách Nghỉ ốm' }));

    await waitFor(() => expect(restoreBuiltInLeaveType).toHaveBeenCalledWith({ ...sick, isActive: false }));
    expect(mocks.notify).toHaveBeenCalledWith('Đã thêm lại chính sách nghỉ.');
  });
});
