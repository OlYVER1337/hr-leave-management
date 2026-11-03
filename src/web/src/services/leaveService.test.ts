import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser } from '../types';
import { createLeaveType, getLeaveRequests } from './leaveService';

const mocks = vi.hoisted(() => ({
  hrGet: vi.fn(),
  hrPost: vi.fn(),
  leaveGet: vi.fn(),
}));

vi.mock('./config', () => ({ DEMO_MODE: false }));
vi.mock('./apiClient', () => ({
  getApiErrorMessage: (error: unknown) => String(error),
  hrApi: { get: mocks.hrGet, post: mocks.hrPost },
  leaveApi: { get: mocks.leaveGet },
}));
vi.mock('./mockStore', () => ({
  loadDemoState: vi.fn(),
  updateDemoState: vi.fn(),
}));

const employee: AuthUser = {
  id: 'employee-1',
  fullName: 'Nguyễn An',
  email: 'employee@example.com',
  role: 'Employee',
  accessToken: 'test-token',
};

const currentEmployee = {
  id: employee.id,
  code: 'E001',
  fullName: employee.fullName,
  role: employee.role,
  isActive: true,
  departmentId: 1,
  departmentName: 'Kỹ thuật',
  managerId: 'manager-1',
  managerName: 'Trần Minh Anh',
};

const requestRecord = {
  id: 'request-1',
  employeeId: employee.id,
  approverId: 'manager-1',
  departmentId: 1,
  leaveTypeId: 1,
  startDate: '2099-01-05',
  endDate: '2099-01-05',
  totalDays: 1,
  reason: 'Nghỉ việc gia đình',
  status: 'Approved',
  createdAtUtc: '2098-12-01T10:00:00Z',
};

describe('getLeaveRequests', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.hrGet.mockImplementation(async (path: string) => {
      if (path === '/api/v1/me') return { data: currentEmployee };
      if (path === '/api/v1/leave-types') {
        return { data: [{ id: 1, code: 'ANNUAL', name: 'Annual leave', isPaid: true, deductsBalance: true }] };
      }
      throw new Error(`Unexpected HR API path: ${path}`);
    });
    mocks.leaveGet.mockResolvedValue({ data: [requestRecord] });
  });

  it('resolves the signed-in employee’s manager as the actual approver', async () => {
    const [request] = await getLeaveRequests(employee);

    expect(request.approverName).toBe('Trần Minh Anh');
  });

  it('uses a neutral label when the approver is outside the visible directory', async () => {
    mocks.leaveGet.mockResolvedValue({ data: [{ ...requestRecord, approverId: 'former-manager' }] });

    const [request] = await getLeaveRequests(employee);

    expect(request.approverName).toBe('Người duyệt không xác định');
  });
});

describe('createLeaveType', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('normalizes the code and always creates UNPAID as unpaid', async () => {
    await createLeaveType({
      code: ' unpaid ',
      name: 'Nghỉ không lương',
      isPaid: true,
      deductsBalance: false,
    });

    expect(mocks.hrPost).toHaveBeenCalledWith('/api/v1/leave-types', {
      code: 'UNPAID',
      name: 'Nghỉ không lương',
      isPaid: false,
      deductsBalance: false,
    });
  });
});
