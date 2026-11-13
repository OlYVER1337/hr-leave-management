import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Department, Employee } from '../types';
import { deactivateEmployee, getDepartments, getEmployeePage, getEmployeeSummary, saveEmployee } from '../services/hrService';
import { EmployeesPage } from './EmployeesPage';

const mocks = vi.hoisted(() => ({
  notify: vi.fn(),
  deactivateEmployee: vi.fn(),
  getDepartments: vi.fn(),
  getEmployeePage: vi.fn(),
  getEmployeeSummary: vi.fn(),
  saveEmployee: vi.fn(),
}));

vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/hrService', () => ({
  deactivateEmployee: mocks.deactivateEmployee,
  getDepartments: mocks.getDepartments,
  getEmployeePage: mocks.getEmployeePage,
  getEmployeeSummary: mocks.getEmployeeSummary,
  saveEmployee: mocks.saveEmployee,
}));

const department: Department = { id: 1, name: 'Kỹ thuật' };
const secondDepartment: Department = { id: 2, name: 'Văn phòng Hà Nội' };
const employees: Employee[] = Array.from({ length: 25 }, (_, index) => {
  const number = index + 1;
  const suffix = String(number).padStart(2, '0');
  return {
    id: `employee-${number}`,
    code: `E${suffix}`,
    fullName: `Nhân viên ${suffix}`,
    email: `employee${suffix}@example.com`,
    role: 'Employee',
    hireDate: '2024-01-01',
    isActive: true,
    departmentId: department.id,
    departmentName: department.name,
    managerId: null,
    managerName: null,
  };
});

describe('EmployeesPage filtering, sorting and pagination', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getEmployeePage).mockImplementation(async (query) => {
      const search = query.search?.toLocaleLowerCase('vi-VN') ?? '';
      const filtered = employees.filter((employee) =>
        (!search || [employee.fullName, employee.code, employee.email, employee.departmentName].some((value) => value.toLocaleLowerCase('vi-VN').includes(search))) &&
        (!query.role || employee.role === query.role) &&
        (query.isActive === undefined || employee.isActive === query.isActive) &&
        (query.departmentId === undefined || employee.departmentId === query.departmentId));
      const collator = new Intl.Collator('vi', { sensitivity: 'base', numeric: true });
      const sorted = [...filtered].sort((left, right) => {
        const field = query.sortBy ?? 'fullName';
        const leftValue = String(left[field] ?? '');
        const rightValue = String(right[field] ?? '');
        return collator.compare(leftValue, rightValue) * (query.sortDirection === 'desc' ? -1 : 1) || left.id.localeCompare(right.id);
      });
      const offset = (query.page - 1) * query.pageSize;
      return { items: sorted.slice(offset, offset + query.pageSize), totalCount: sorted.length };
    });
    vi.mocked(getDepartments).mockResolvedValue([department]);
    vi.mocked(getEmployeeSummary).mockResolvedValue({ total: 25, active: 25, managers: 0 });
    vi.mocked(deactivateEmployee).mockResolvedValue(undefined);
    vi.mocked(saveEmployee).mockResolvedValue(employees[0]);
  });

  it('loads employee summary in one request instead of three count queries', async () => {
    render(<EmployeesPage />);

    await screen.findByText('Nhân viên 01');

    expect(getEmployeeSummary).toHaveBeenCalledTimes(1);
    expect(getEmployeePage).not.toHaveBeenCalledWith(
      expect.objectContaining({ pageSize: 1 }),
    );
    expect(screen.getByText('25')).toBeInTheDocument();
  });

  it('paginates employee rows and returns to the first page when filtering', async () => {
    const user = userEvent.setup();
    render(<EmployeesPage />);

    await screen.findByText('Nhân viên 01');
    expect(screen.getByText('Trang 1 / 3')).toBeInTheDocument();
    expect(screen.queryByText('Nhân viên 11')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Trang sau' }));
    expect(screen.getByText('Nhân viên 11')).toBeInTheDocument();
    expect(screen.getByText('Trang 2 / 3')).toBeInTheDocument();
    expect(screen.queryByText('Nhân viên 01')).not.toBeInTheDocument();

    await user.type(screen.getByRole('textbox', { name: 'Tìm nhân viên' }), 'Nhân viên 20');
    expect(await screen.findByText('Trang 1 / 1')).toBeInTheDocument();
    expect(await screen.findByText('Nhân viên 20')).toBeInTheDocument();
    expect(screen.queryByText('Nhân viên 11')).not.toBeInTheDocument();
    expect(document.querySelector('.table-footer')?.textContent).toContain('Đang hiển thị 1–1 / 1 nhân viên');
  });

  it('supports changing the number of employees per page', async () => {
    const user = userEvent.setup();
    render(<EmployeesPage />);

    await screen.findByText('Nhân viên 01');
    await user.selectOptions(screen.getByRole('combobox', { name: 'Số nhân viên mỗi trang' }), '25');

    expect(screen.getByText('Trang 1 / 1')).toBeInTheDocument();
    expect(screen.getByText('Nhân viên 25')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(26);
    expect(document.querySelector('.table-footer')?.textContent).toContain('Đang hiển thị 1–25 / 25 nhân viên');

    fireEvent.change(screen.getByRole('combobox', { name: 'Lọc theo trạng thái' }), {
      target: { value: 'Inactive' },
    });
    expect(screen.getByRole('button', { name: 'Trang trước' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Trang sau' })).toBeDisabled();
  });

  it('reuses the prefetched page and requests another block only when leaving it', async () => {
    const user = userEvent.setup();
    render(<EmployeesPage />);
    await screen.findByText('Nhân viên 01');

    const callsAfterInitialBlock = vi.mocked(getEmployeePage).mock.calls.filter(([query]) =>
      query.search === '' && query.pageSize === 20 && query.sortBy === 'fullName').length;
    expect(callsAfterInitialBlock).toBe(1);

    await user.click(screen.getByRole('button', { name: 'Trang 2' }));
    expect(screen.getByText('Nhân viên 11')).toBeInTheDocument();
    const callsAfterPageTwo = vi.mocked(getEmployeePage).mock.calls.filter(([query]) =>
      query.search === '' && query.pageSize === 20 && query.sortBy === 'fullName').length;
    expect(callsAfterPageTwo).toBe(1);

    await user.click(screen.getByRole('button', { name: 'Trang 3' }));
    expect(await screen.findByText('Nhân viên 21')).toBeInTheDocument();
    const callsAfterPageThree = vi.mocked(getEmployeePage).mock.calls.filter(([query]) =>
      query.search === '' && query.pageSize === 20 && query.sortBy === 'fullName').length;
    expect(callsAfterPageThree).toBe(2);
    expect(getEmployeePage).toHaveBeenCalledWith(expect.objectContaining({ page: 2, pageSize: 20 }));
  });

  it('sorts employees by name in ascending and descending order', async () => {
    const user = userEvent.setup();
    render(<EmployeesPage />);

    await screen.findByText('Nhân viên 01');
    const sortByName = screen.getByRole('button', { name: 'Sắp xếp theo họ tên' });
    const rows = () => within(screen.getByRole('table')).getAllByRole('row');

    await user.click(sortByName);
    expect(within(rows()[1]).getByText('Nhân viên 25')).toBeInTheDocument();
    expect(rows()[0].querySelector('th')).toHaveAttribute('aria-sort', 'descending');

    await user.click(sortByName);
    expect(within(rows()[1]).getByText('Nhân viên 01')).toBeInTheDocument();
    expect(rows()[0].querySelector('th')).toHaveAttribute('aria-sort', 'ascending');
  });

  it('filters employees by the selected department name', async () => {
    const user = userEvent.setup();
    const employeesAcrossDepartments = employees.map((employee, index) =>
      index % 2 === 0
        ? employee
        : { ...employee, departmentId: secondDepartment.id, departmentName: secondDepartment.name },
    );
    vi.mocked(getEmployeePage).mockImplementation(async (query) => {
      const search = query.search?.toLocaleLowerCase('vi-VN') ?? '';
      const filtered = employeesAcrossDepartments.filter((employee) =>
        (!search || [employee.fullName, employee.code, employee.email, employee.departmentName].some((value) => value.toLocaleLowerCase('vi-VN').includes(search))) &&
        (!query.role || employee.role === query.role) &&
        (query.isActive === undefined || employee.isActive === query.isActive) &&
        (query.departmentId === undefined || employee.departmentId === query.departmentId));
      const offset = (query.page - 1) * query.pageSize;
      return { items: filtered.slice(offset, offset + query.pageSize), totalCount: filtered.length };
    });
    vi.mocked(getDepartments).mockResolvedValue([department, secondDepartment]);

    render(<EmployeesPage />);

    await screen.findByText('Nhân viên 01');
    await user.selectOptions(
      screen.getByRole('combobox', { name: 'Lọc theo phòng ban' }),
      String(secondDepartment.id),
    );

    expect(screen.getByText('Nhân viên 02')).toBeInTheDocument();
    expect(screen.queryByText('Nhân viên 01')).not.toBeInTheDocument();
    expect(document.querySelector('.table-footer')?.textContent).toContain('1–10 / 12');
  });

  it('shows the employee code as system-generated instead of editable input', async () => {
    const user = userEvent.setup();
    render(<EmployeesPage />);

    await screen.findByText('Nhân viên 01');
    await user.click(screen.getByRole('button', { name: 'Thêm nhân viên' }));

    const codeInput = screen.getByLabelText('Mã nhân viên');
    expect(codeInput).toHaveValue('Tự động sinh sau khi tạo');
    expect(codeInput).toHaveAttribute('readonly');
    expect(screen.getByText('Mã được hệ thống tạo tự động và không thể thay đổi.')).toBeInTheDocument();
  });
});
