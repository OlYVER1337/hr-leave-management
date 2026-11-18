import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Holiday, LeavePolicy } from '../types';
import {
  getHolidays,
  getPolicies,
  getPolicyVersions,
  restoreBuiltInLeaveType,
  saveHolidays,
  savePolicies,
} from '../services/leaveService';
import { PoliciesPage } from './PoliciesPage';

const mocks = vi.hoisted(() => ({
  notify: vi.fn(),
  getHolidays: vi.fn(),
  getPolicies: vi.fn(),
  getPolicyVersions: vi.fn(),
  restoreBuiltInLeaveType: vi.fn(),
  saveHolidays: vi.fn(),
  savePolicies: vi.fn(),
}));

vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  getHolidays: mocks.getHolidays,
  getPolicies: mocks.getPolicies,
  getPolicyVersions: mocks.getPolicyVersions,
  restoreBuiltInLeaveType: mocks.restoreBuiltInLeaveType,
  saveHolidays: mocks.saveHolidays,
  savePolicies: mocks.savePolicies,
}));

const currentBusinessYear = Number(new Intl.DateTimeFormat('en', {
  timeZone: 'Asia/Ho_Chi_Minh',
  year: 'numeric',
}).format(new Date()));
const futureYear = currentBusinessYear + 1;
const annual: LeavePolicy = {
  id: 'annual',
  year: currentBusinessYear,
  leaveTypeId: 1,
  policyId: 101,
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
  policyId: 102,
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
    vi.mocked(getPolicyVersions).mockResolvedValue([
      { ...annual, versionNumber: 1 },
      { ...sick, versionNumber: 1 },
    ]);
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

  it('offers version creation only for balance-deducting policies', async () => {
    renderPoliciesPage();

    expect(await screen.findAllByRole('button', { name: 'Tạo phiên bản mới' })).toHaveLength(2);
    expect(await screen.findByText('Loại nghỉ này không áp dụng quota.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Xóa chính sách/ })).not.toBeInTheDocument();
  });

  it('shows the current policies by default and opens history for one leave type', async () => {
    const user = userEvent.setup();
    vi.mocked(getPolicyVersions).mockResolvedValue([
      { ...annual, versionNumber: 1 },
      { ...annual, id: 'annual-v2', policyId: 202, versionNumber: 2, daysPerYear: 15, effectiveFrom: `${currentBusinessYear}-06-01` },
      { ...sick, versionNumber: 1 },
    ]);
    renderPoliciesPage();

    const viewFilter = await screen.findByRole('combobox', { name: 'Hiển thị chính sách' });
    expect(viewFilter).toHaveValue('CURRENT');
    expect(screen.getByRole('heading', { name: 'Nghỉ phép năm' })).toBeInTheDocument();

    await user.click(screen.getAllByRole('button', { name: /Xem lịch sử/ })[0]);

    expect(await screen.findByRole('dialog', { name: 'Lịch sử chính sách · Nghỉ phép năm' })).toBeInTheDocument();
    expect(screen.getByText('Version 1')).toBeInTheDocument();
    expect(screen.getByText('Version 2')).toBeInTheDocument();
  });

  it('shows all versions grouped by leave type when history view is selected', async () => {
    const user = userEvent.setup();
    vi.mocked(getPolicyVersions).mockResolvedValue([
      { ...annual, versionNumber: 1 },
      { ...annual, id: 'annual-v2', policyId: 202, versionNumber: 2, daysPerYear: 15, effectiveFrom: `${currentBusinessYear}-06-01` },
      { ...sick, versionNumber: 1 },
    ]);
    renderPoliciesPage();

    await user.selectOptions(
      await screen.findByRole('combobox', { name: 'Hiển thị chính sách' }),
      'HISTORY',
    );

    expect(screen.getAllByText('Version 1')).toHaveLength(2);
    expect(screen.getByText('Version 2')).toBeInTheDocument();
    expect(screen.getByText('UNPAID không áp dụng quota và không có version.')).toBeInTheDocument();
  });

  it('allows current-year policy versions from the current business date onward', async () => {
    const user = userEvent.setup();
    renderPoliciesPage();

    await screen.findByRole('heading', { name: 'Nghỉ phép năm' });
    expect(screen.getByRole('combobox', { name: 'Hiển thị chính sách' })).toHaveValue('CURRENT');
    const createButtons = await screen.findAllByRole('button', { name: 'Tạo phiên bản mới' });
    expect(createButtons).toHaveLength(2);
    await user.click(createButtons[0]);

    expect(await screen.findByLabelText('Năm áp dụng')).toHaveValue(String(currentBusinessYear));
    expect(screen.getByLabelText('Ngày hiệu lực *')).toHaveAttribute('min');
    expect(screen.getByLabelText('Ngày hiệu lực *')).toHaveValue(
      new Intl.DateTimeFormat('en-CA', {
        timeZone: 'Asia/Ho_Chi_Minh',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
      }).format(new Date()),
    );
  });

  it('opens a new current-year version after the latest scheduled version', async () => {
    const user = userEvent.setup();
    const today = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Ho_Chi_Minh',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date());
    const nextDateValue = new Date(`${today}T00:00:00Z`);
    nextDateValue.setUTCDate(nextDateValue.getUTCDate() + 1);
    const nextDate = nextDateValue.toISOString().slice(0, 10);
    vi.mocked(getPolicies).mockImplementation(async (year, asOfDate) => {
      if (asOfDate === `${currentBusinessYear}-12-31`) {
        return [
          { ...annual, year, effectiveFrom: today },
          { ...sick, year },
          { ...unpaid, year },
        ];
      }
      return [{ ...annual, year, effectiveFrom: `${currentBusinessYear}-01-01` }, sick, unpaid];
    });
    renderPoliciesPage();

    await user.click((await screen.findAllByRole('button', { name: 'Tạo phiên bản mới' }))[0]);

    if (nextDate <= `${currentBusinessYear}-12-31`) {
      expect(await screen.findByLabelText('Ngày hiệu lực *')).toHaveValue(nextDate);
      expect(screen.getByLabelText('Ngày hiệu lực *')).toHaveAttribute('min', nextDate);
    } else {
      await waitFor(() => expect(mocks.notify).toHaveBeenCalledWith(
        'Không còn ngày hiệu lực hợp lệ trong năm hiện tại.',
        'error',
      ));
    }
  });

  it('sets a future policy version to the first day of its policy year', async () => {
    const user = userEvent.setup();
    vi.mocked(getPolicies).mockImplementation(async (year) => {
      if (year === futureYear) {
        return [
          { ...annual, year: futureYear, policyId: undefined, effectiveFrom: undefined },
          { ...sick, year: futureYear, policyId: undefined, effectiveFrom: undefined },
          { ...unpaid, year: futureYear },
        ];
      }
      return policies;
    });
    renderPoliciesPage();

    await user.click((await screen.findAllByRole('button', { name: 'Tạo phiên bản mới' }))[0]);
    await user.selectOptions(screen.getByLabelText('Năm áp dụng'), String(futureYear));

    await waitFor(() => expect(screen.getByLabelText('Ngày hiệu lực *')).toHaveValue(`${futureYear}-01-01`));
    expect(screen.getByLabelText('Ngày hiệu lực *')).toHaveAttribute('min', `${futureYear}-01-01`);
  });

  it('defaults to the next policy year when the current year has no later effective date', async () => {
    const user = userEvent.setup();
    vi.mocked(getPolicies).mockImplementation(async (year, asOfDate) => {
      if (year === currentBusinessYear && asOfDate === `${currentBusinessYear}-12-31`) {
        return [{ ...annual, year, effectiveFrom: `${currentBusinessYear}-12-31` }, { ...sick, year }, { ...unpaid, year }];
      }
      if (year === futureYear) {
        return [
          { ...annual, year, policyId: undefined, effectiveFrom: undefined },
          { ...sick, year, policyId: undefined, effectiveFrom: undefined },
          { ...unpaid, year },
        ];
      }
      return policies;
    });
    renderPoliciesPage();

    await user.click((await screen.findAllByRole('button', { name: 'Tạo phiên bản mới' }))[0]);

    expect(await screen.findByLabelText('Năm áp dụng')).toHaveValue(String(futureYear));
    expect(screen.getByLabelText('Ngày hiệu lực *')).toHaveValue(`${futureYear}-01-01`);
  });

  it('submits a policy save only once when the form is submitted repeatedly', async () => {
    const user = userEvent.setup();
    let releaseSave: (() => void) | undefined;
    vi.mocked(savePolicies).mockImplementation(() => new Promise<void>((resolve) => {
      releaseSave = resolve;
    }));
    renderPoliciesPage();

    await user.click((await screen.findAllByRole('button', { name: 'Tạo phiên bản mới' }))[0]);
    const form = screen.getByRole('dialog').querySelector('form');
    expect(form).not.toBeNull();

    fireEvent.submit(form!);
    fireEvent.submit(form!);

    expect(savePolicies).toHaveBeenCalledTimes(1);
    releaseSave?.();
    await waitFor(() => expect(mocks.notify).toHaveBeenCalledWith('Đã lưu chính sách nghỉ phép.'));
  });

  it('restores a deleted built-in policy', async () => {
    const user = userEvent.setup();
    vi.mocked(getPolicies)
      .mockResolvedValueOnce([annual, { ...sick, isActive: false }, unpaid])
      .mockResolvedValueOnce(policies);
    renderPoliciesPage();

    await user.click(await screen.findByRole('button', { name: 'Thêm lại chính sách Nghỉ ốm' }));

    await waitFor(() => expect(restoreBuiltInLeaveType).toHaveBeenCalledWith({
      ...sick,
      isActive: false,
      year: currentBusinessYear,
    }));
    expect(mocks.notify).toHaveBeenCalledWith('Đã thêm lại chính sách nghỉ.');
  });
});
