import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser, Employee } from '../types';
import { useAuth } from '../contexts';
import { getEmployeePage } from '../services/hrService';
import { ManagerTeamPage } from './ManagerTeamPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  getEmployeePage: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../services/hrService', () => ({ getEmployeePage: mocks.getEmployeePage }));

const manager: AuthUser = {
  id: 'manager-1',
  fullName: 'Nguyễn Quản Lý',
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

const team: Employee[] = [
  {
    id: 'employee-1',
    code: 'EMP-001',
    fullName: 'Nguyễn An',
    email: 'an@example.com',
    role: 'Employee',
    hireDate: '2024-01-15',
    isActive: true,
    departmentId: 1,
    departmentName: 'Kỹ thuật',
    managerId: manager.id,
    managerName: manager.fullName,
  },
  {
    id: 'employee-2',
    code: 'EMP-002',
    fullName: 'Trần Bình',
    email: 'binh@example.com',
    role: 'Employee',
    hireDate: '2023-06-01',
    isActive: true,
    departmentId: 1,
    departmentName: 'Kỹ thuật',
    managerId: manager.id,
    managerName: manager.fullName,
  },
];

describe('ManagerTeamPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useAuth).mockReturnValue({
      user: manager,
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getEmployeePage).mockImplementation(async (query, options) => {
      const term = query.search?.trim().toLocaleLowerCase('vi-VN') ?? '';
      const filtered = team
        .filter((employee) => employee.isActive && employee.managerId === options?.currentEmployeeId)
        .filter((employee) =>
          !term || [
            employee.fullName,
            employee.code,
            employee.email,
            employee.departmentName,
          ].some((value) => value.toLocaleLowerCase('vi-VN').includes(term)),
        );
      const offset = (query.page - 1) * query.pageSize;
      return {
        items: filtered.slice(offset, offset + query.pageSize),
        totalCount: filtered.length,
      };
    });
  });

  it('loads and displays only the manager team returned by the team service', async () => {
    render(<ManagerTeamPage />);

    expect(await screen.findByText('Nguyễn An')).toBeInTheDocument();
    expect(screen.getByText('Trần Bình')).toBeInTheDocument();
    expect(screen.getByText('Tổng nhân viên phụ trách').parentElement?.textContent).toContain('2');
    expect(getEmployeePage).toHaveBeenCalledWith(
      expect.objectContaining({ page: 1, directReportsOnly: true }),
      { currentEmployeeId: manager.id },
    );
  });

  it('filters the manager team and opens a read-only employee detail dialog', async () => {
    const user = userEvent.setup();
    render(<ManagerTeamPage />);

    await screen.findByText('Nguyễn An');
    await user.type(screen.getByRole('textbox', { name: 'Tìm nhân viên phụ trách' }), 'binh');

    expect(await screen.findByText('Trần Bình')).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText('Nguyễn An')).not.toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'Xem hồ sơ Trần Bình' }));
    expect(screen.getByRole('dialog', { name: 'Thông tin nhân viên' })).toBeInTheDocument();
    expect(screen.getAllByText('binh@example.com')).toHaveLength(2);
    expect(screen.getAllByText('EMP-002')).toHaveLength(2);
    expect(screen.getAllByText('Kỹ thuật')).toHaveLength(2);
  });

  it('shows an empty state when the manager has no assigned active employees', async () => {
    vi.mocked(getEmployeePage).mockResolvedValue({ items: [], totalCount: 0 });
    render(<ManagerTeamPage />);

    expect(await screen.findByText('Bạn chưa có nhân viên được phân công')).toBeInTheDocument();
  });
});
