import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser } from '../types';
import { getLeaveRequests, getLeaveRequestsPage, getPolicies, restoreBuiltInLeaveType } from './leaveService';

const mocks = vi.hoisted(() => ({
  hrGet: vi.fn(),
  hrPost: vi.fn(),
  hrPut: vi.fn(),
  leaveGet: vi.fn(),
}));

vi.mock('./config', () => ({ DEMO_MODE: false }));
vi.mock('./apiClient', () => ({
  getApiErrorMessage: (error: unknown) => String(error),
  hrApi: { get: mocks.hrGet, post: mocks.hrPost, put: mocks.hrPut },
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

describe('paged leave display lookups', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.hrGet.mockImplementation(async (path: string) => {
      if (path === '/api/v1/leave-types') {
        return {
          data: [{ id: 1, code: 'ANNUAL', name: 'Annual leave', isPaid: true, deductsBalance: true, isActive: true }],
        };
      }
      throw new Error(`Unexpected HR API path: ${path}`);
    });
    mocks.hrPost.mockResolvedValue({
      data: [
        {
          id: 'employee-1',
          code: 'E001',
          fullName: 'Employee One',
          role: 'Employee',
          isActive: true,
          departmentId: 1,
          departmentName: 'Kỹ thuật',
          managerId: 'manager-1',
          managerName: 'Manager One',
        },
        {
          id: 'manager-1',
          code: 'M001',
          fullName: 'Manager One',
          role: 'Manager',
          isActive: true,
          departmentId: 1,
          departmentName: 'Kỹ thuật',
          managerId: null,
          managerName: null,
        },
      ],
    });
    mocks.leaveGet.mockResolvedValue({ data: [requestRecord], headers: { 'x-total-count': '2' } });
  });

  it('reuses employee directory and leave-type lookups across pages for the same HR user', async () => {
    const hrUser: AuthUser = {
      id: 'hr-pagination-cache',
      fullName: 'HR User',
      email: 'hr@example.com',
      role: 'HR',
      accessToken: 'test-token',
    };

    const firstPage = await getLeaveRequestsPage(hrUser, { page: 1, pageSize: 10 });
    const secondPage = await getLeaveRequestsPage(hrUser, { page: 2, pageSize: 10 });

    expect(firstPage.items[0]).toMatchObject({
      employeeName: 'Employee One',
      approverName: 'Manager One',
      typeCode: 'ANNUAL',
    });
    expect(secondPage.items[0].employeeName).toBe('Employee One');
    expect(mocks.leaveGet).toHaveBeenCalledTimes(2);
    expect(mocks.hrGet).toHaveBeenCalledTimes(1);
    expect(mocks.hrPost).toHaveBeenCalledTimes(1);
  });
});

describe('built-in leave policies', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('returns only the fixed three types and represents a deleted type as restorable', async () => {
    const year = new Date().getFullYear();
    mocks.hrGet.mockImplementation(async (path: string) => {
      if (path === '/api/v1/leave-types') {
        return {
          data: [
            { id: 1, code: 'ANNUAL', name: 'Custom annual', isPaid: true, deductsBalance: true, isActive: true },
            { id: 2, code: 'PARENTAL', name: 'Nghỉ chăm con', isPaid: true, deductsBalance: true, isActive: true },
          ],
        };
      }
      if (path === '/api/v1/leave-policies') {
        return { data: [{ id: 12, leaveTypeId: 1, year, daysPerYear: 15 }] };
      }
      throw new Error(`Unexpected HR API path: ${path}`);
    });

    const result = await getPolicies();

    expect(result.map((policy) => policy.typeCode)).toEqual(['ANNUAL', 'SICK', 'UNPAID']);
    expect(result.find((policy) => policy.typeCode === 'ANNUAL')).toMatchObject({
      typeName: 'Nghỉ phép năm',
      daysPerYear: 15,
      isActive: true,
    });
    expect(result.find((policy) => policy.typeCode === 'SICK')).toMatchObject({
      isActive: false,
      leaveTypeId: undefined,
      daysPerYear: 10,
    });
  });

  it('restores a missing built-in type and creates its default annual policy', async () => {
    const year = new Date().getFullYear();
    mocks.hrGet.mockImplementation(async (path: string) => {
      if (path === '/api/v1/leave-types') return { data: [] };
      if (path === '/api/v1/leave-policies') return { data: [] };
      throw new Error(`Unexpected HR API path: ${path}`);
    });
    mocks.hrPost.mockResolvedValue({ data: { id: 4 } });

    await restoreBuiltInLeaveType({
      id: 'type-SICK',
      typeCode: 'SICK',
      typeName: 'Nghỉ ốm',
      daysPerYear: 10,
      paid: true,
      deductsBalance: true,
      description: 'Nghỉ ốm',
      isActive: false,
      year,
    });

    expect(mocks.hrPost).toHaveBeenNthCalledWith(1, '/api/v1/leave-types', {
      code: 'SICK',
      name: 'Nghỉ ốm',
      isPaid: true,
      deductsBalance: true,
      isActive: true,
    });
    expect(mocks.hrPost).toHaveBeenNthCalledWith(2, '/api/v1/leave-policies', {
      leaveTypeId: 4,
      year,
      daysPerYear: 10,
    });
  });
});
