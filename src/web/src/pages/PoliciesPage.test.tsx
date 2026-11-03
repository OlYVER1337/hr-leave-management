import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Holiday, LeavePolicy } from '../types';
import {
  createLeaveType,
  getHolidays,
  getPolicies,
  saveHolidays,
  savePolicies,
} from '../services/leaveService';
import { PoliciesPage } from './PoliciesPage';

const mocks = vi.hoisted(() => ({
  notify: vi.fn(),
  createLeaveType: vi.fn(),
  getHolidays: vi.fn(),
  getPolicies: vi.fn(),
  saveHolidays: vi.fn(),
  savePolicies: vi.fn(),
}));

vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/leaveService', () => ({
  createLeaveType: mocks.createLeaveType,
  getHolidays: mocks.getHolidays,
  getPolicies: mocks.getPolicies,
  saveHolidays: mocks.saveHolidays,
  savePolicies: mocks.savePolicies,
}));

const basePolicy: LeavePolicy = {
  id: 'annual',
  typeCode: 'ANNUAL',
  typeName: 'Nghỉ phép năm',
  daysPerYear: 12,
  paid: true,
  deductsBalance: true,
  description: 'Nghỉ phép năm',
  isActive: true,
};
const holidays: Holiday[] = [];

describe('PoliciesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getPolicies).mockResolvedValue([basePolicy]);
    vi.mocked(getHolidays).mockResolvedValue(holidays);
    vi.mocked(saveHolidays).mockResolvedValue(holidays);
    vi.mocked(savePolicies).mockResolvedValue(undefined);
    vi.mocked(createLeaveType).mockResolvedValue(undefined);
  });

  it('creates a leave type and refreshes the policy list', async () => {
    const user = userEvent.setup();
    vi.mocked(getPolicies)
      .mockResolvedValueOnce([basePolicy])
      .mockResolvedValueOnce([
        basePolicy,
        {
          ...basePolicy,
          id: 'parental',
          typeCode: 'PARENTAL',
          typeName: 'Nghỉ chăm con',
          daysPerYear: 0,
          description: 'Nghỉ phép theo chính sách của tổ chức.',
        },
      ]);
    render(
      <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <PoliciesPage />
      </MemoryRouter>,
    );

    await user.click(await screen.findByRole('button', { name: 'Thêm loại nghỉ' }));
    await user.type(screen.getByLabelText('Mã loại nghỉ *'), 'parental');
    await user.type(screen.getByLabelText('Tên loại nghỉ *'), 'Nghỉ chăm con');
    await user.click(screen.getByRole('button', { name: 'Tạo loại nghỉ' }));

    await waitFor(() => {
      expect(createLeaveType).toHaveBeenCalledWith({
        code: 'PARENTAL',
        name: 'Nghỉ chăm con',
        isPaid: true,
        deductsBalance: true,
      });
    });
    expect(await screen.findByRole('heading', { name: 'Nghỉ chăm con' })).toBeInTheDocument();
    expect(mocks.notify).toHaveBeenCalledWith('Đã tạo loại nghỉ. Hãy cấu hình hạn mức năm trong chính sách.');
  });
});
