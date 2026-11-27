import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { deleteDepartment, getDepartments, getEmployeePage, saveDepartment } from '../services/hrService';
import { DepartmentsPage } from './DepartmentsPage';

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  notify: vi.fn(),
  deleteDepartment: vi.fn(),
  getDepartments: vi.fn(),
  getEmployeePage: vi.fn(),
  saveDepartment: vi.fn(),
}));

vi.mock('../contexts', () => ({ useAuth: mocks.useAuth }));
vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/hrService', () => ({
  deleteDepartment: mocks.deleteDepartment,
  getDepartments: mocks.getDepartments,
  getEmployeePage: mocks.getEmployeePage,
  saveDepartment: mocks.saveDepartment,
}));

const department = { id: 1, name: 'Kỹ thuật' };

describe('DepartmentsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.useAuth.mockReturnValue({
      user: {
        id: 'hr-1',
        fullName: 'HR User',
        email: 'hr@example.com',
        role: 'HR',
        accessToken: 'test-token',
        permissions: [{"p":"employees.read","s":["All"]},{"p":"employees.create","s":["All"]},{"p":"employees.update","s":["All"]},{"p":"employees.deactivate","s":["All"]},{"p":"employees.password.reset","s":["All"]},{"p":"departments.create","s":["All"]},{"p":"departments.update","s":["All"]},{"p":"departments.delete","s":["All"]},{"p":"leave-policies.create","s":["All"]},{"p":"leave-policies.update","s":["All"]},{"p":"leave-types.create","s":["All"]},{"p":"leave-types.update","s":["All"]},{"p":"holidays.create","s":["All"]},{"p":"holidays.update","s":["All"]},{"p":"holidays.delete","s":["All"]},{"p":"leaves.read","s":["All"]},{"p":"leaves.create","s":["Self"]},{"p":"leaves.decide","s":["All"]},{"p":"leaves.cancel","s":["Self"]}],
      },
      busy: false,
      restoreError: null,
      signIn: vi.fn(),
      signOut: vi.fn(),
    });
    vi.mocked(getDepartments).mockResolvedValue([department]);
    vi.mocked(getEmployeePage).mockResolvedValue({ items: [], totalCount: 0 });
    vi.mocked(saveDepartment).mockResolvedValue({ id: 2, name: 'Vận hành' });
    vi.mocked(deleteDepartment).mockResolvedValue(undefined);
  });

  it('creates a department and refreshes the list', async () => {
    const user = userEvent.setup();
    vi.mocked(getDepartments)
      .mockResolvedValueOnce([department])
      .mockResolvedValueOnce([department, { id: 2, name: 'Vận hành' }]);
    render(<DepartmentsPage />);

    expect(await screen.findByRole('heading', { name: 'Kỹ thuật' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Thêm phòng ban' }));
    await user.type(screen.getByLabelText('Tên phòng ban *'), 'Vận hành');
    await user.click(screen.getByRole('button', { name: 'Tạo phòng ban' }));

    await waitFor(() => expect(saveDepartment).toHaveBeenCalledWith('Vận hành', undefined));
    expect(await screen.findByRole('heading', { name: 'Vận hành' })).toBeInTheDocument();
    expect(mocks.notify).toHaveBeenCalledWith('Đã tạo phòng ban.');
  });
});
