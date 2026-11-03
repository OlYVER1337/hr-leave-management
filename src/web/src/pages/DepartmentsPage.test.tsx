import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { deleteDepartment, getDepartments, getEmployees, saveDepartment } from '../services/hrService';
import { DepartmentsPage } from './DepartmentsPage';

const mocks = vi.hoisted(() => ({
  notify: vi.fn(),
  deleteDepartment: vi.fn(),
  getDepartments: vi.fn(),
  getEmployees: vi.fn(),
  saveDepartment: vi.fn(),
}));

vi.mock('../components/ToastProvider', () => ({ useToast: () => mocks.notify }));
vi.mock('../services/hrService', () => ({
  deleteDepartment: mocks.deleteDepartment,
  getDepartments: mocks.getDepartments,
  getEmployees: mocks.getEmployees,
  saveDepartment: mocks.saveDepartment,
}));

const department = { id: 1, name: 'Kỹ thuật' };

describe('DepartmentsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getDepartments).mockResolvedValue([department]);
    vi.mocked(getEmployees).mockResolvedValue([]);
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
