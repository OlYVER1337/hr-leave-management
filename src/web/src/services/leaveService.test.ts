import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AuthUser } from '../types';
import { decideLeaveRequest, getBalances, getLeaveRequestSummary, getLeaveRequests, getLeaveRequestsPage, getPolicies, getPolicyVersions, restoreBuiltInLeaveType, savePolicies } from './leaveService';

const mocks = vi.hoisted(() => ({
  hrGet: vi.fn(),
  hrPost: vi.fn(),
  hrPut: vi.fn(),
  leaveGet: vi.fn(),
  leavePost: vi.fn(),
  leavePatch: vi.fn(),
}));

vi.mock('./config', () => ({ DEMO_MODE: false }));
vi.mock('./apiClient', () => ({
  getApiErrorMessage: (error: unknown) => String(error),
  hrApi: { get: mocks.hrGet, post: mocks.hrPost, put: mocks.hrPut },
  leaveApi: { get: mocks.leaveGet, post: mocks.leavePost, patch: mocks.leavePatch },
}));
vi.mock('./mockStore', () => ({
  loadDemoState: vi.fn(),
  updateDemoState: vi.fn(),
}));

function getVietnamBusinessYear(): number {
  return Number(new Intl.DateTimeFormat('en', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
  }).format(new Date()));
}

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

describe('leave request summary and decisions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // Khi Manager approve, frontend gửi đúng request body đến route PATCH decision mới.
  it('sends an approval decision through the PATCH endpoint', async () => {
    mocks.leavePatch.mockResolvedValue({ data: {} });

    await decideLeaveRequest('request-1', 'approve', 'Đã kiểm tra');

    expect(mocks.leavePatch).toHaveBeenCalledTimes(1);
    expect(mocks.leavePatch).toHaveBeenCalledWith(
      '/api/v1/leave-requests/request-1/decision',
      { decision: 'approve', note: 'Đã kiểm tra' },
    );
  });

  // Khi reject, frontend vẫn dùng chung route PATCH decision và truyền note rỗng nếu không nhập.
  it('sends a rejection decision through the PATCH endpoint', async () => {
    mocks.leavePatch.mockResolvedValue({ data: {} });

    await decideLeaveRequest('request-2', 'reject');

    expect(mocks.leavePatch).toHaveBeenCalledTimes(1);
    expect(mocks.leavePatch).toHaveBeenCalledWith(
      '/api/v1/leave-requests/request-2/decision',
      { decision: 'reject', note: '' },
    );
  });

  // Summary được lấy từ Leave Service theo scope quyền trong một request, không tự tính từ dữ liệu hiển thị.
  it('loads the permission-scoped leave summary in one request', async () => {
    const manager: AuthUser = {
      id: 'manager-1',
      fullName: 'Trần Minh Anh',
      email: 'manager@example.com',
      role: 'Manager',
      accessToken: 'test-token',
    };
    mocks.leaveGet.mockResolvedValue({
      data: { pending: 4, approved: 8, total: 16 },
    });

    await expect(getLeaveRequestSummary(manager)).resolves.toEqual({
      pending: 4,
      approved: 8,
      total: 16,
    });

    expect(mocks.leaveGet).toHaveBeenCalledTimes(1);
    expect(mocks.leaveGet).toHaveBeenCalledWith('/api/v1/leave-requests/summary');
  });

  // Balance được khởi tạo/đồng bộ bằng POST; HR catalog cung cấp tên và metadata để hiển thị.
  it('loads balances and catalog with two requests instead of three', async () => {
    // Dùng năm nghiệp vụ Việt Nam để test không phụ thuộc múi giờ máy chạy test.
    const year = getVietnamBusinessYear();
    mocks.leavePost.mockResolvedValue({
      data: [{
        leaveTypeId: 1,
        year,
        entitled: 15,
        policyDays: 15,
        used: 2,
        pending: 1,
        available: 12,
      }],
    });
    mocks.hrGet.mockResolvedValue({
      data: [{
        id: 1,
        code: 'ANNUAL',
        name: 'Annual leave',
        isPaid: true,
        deductsBalance: true,
        isActive: true,
      }],
    });

    // Hàm getBalances đồng bộ balance ở Leave Service rồi lấy danh sách loại nghỉ từ HR.
    const result = await getBalances(employee);

    expect(result).toHaveLength(1);
    expect(result[0]).toMatchObject({
      code: 'ANNUAL',
      entitled: 15,
      policyDays: 15,
      used: 2,
      pending: 1,
      available: 12,
    });
    // Chỉ có một POST đồng bộ balance và một GET danh sách loại nghỉ từ HR.
    expect(mocks.leavePost).toHaveBeenCalledTimes(1);
    expect(mocks.leavePost).toHaveBeenCalledWith(
      '/api/v1/leave-balances/mine/synchronize',
      null,
      { params: { year } },
    );
    expect(mocks.hrGet).toHaveBeenCalledTimes(1);
    expect(mocks.hrGet).toHaveBeenCalledWith('/api/v1/leave-types');
  });
});

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

  // Tên người duyệt được đối chiếu với Manager hiện tại lấy từ HR, không dùng tên giả định.
  it('resolves the signed-in employee’s manager as the actual approver', async () => {
    const [request] = await getLeaveRequests(employee);

    expect(request.approverName).toBe('Trần Minh Anh');
  });

  // Nếu approver không nằm trong directory caller được phép xem, UI không tiết lộ thêm thông tin cá nhân.
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

  // Chuyển trang liên tiếp phải tái sử dụng cache lookup trong TTL, tránh gọi HR lặp lại.
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

  // Màn hình policy chỉ hiển thị ba loại nghỉ chuẩn; loại đã xóa được biểu diễn ở trạng thái có thể khôi phục.
  it('returns only the fixed three types and represents a deleted type as restorable', async () => {
    const year = getVietnamBusinessYear() + 1;
    mocks.hrGet.mockImplementation(async (path: string) => {
      if (path === '/api/v1/leave-catalog') {
        return {
          data: {
            leaveTypes: [
              { id: 1, code: 'ANNUAL', name: 'Custom annual', isPaid: true, deductsBalance: true, isActive: true },
              { id: 2, code: 'PARENTAL', name: 'Nghỉ chăm con', isPaid: true, deductsBalance: true, isActive: true },
            ],
            leavePolicies: [
              {
                id: 11,
                leaveTypeId: 1,
                year,
                daysPerYear: 12,
                effectiveFrom: `${year}-01-01`,
                createdAtUtc: `${year}-01-01T00:00:00Z`,
                reason: 'Phiên bản đầu năm',
              },
              {
                id: 12,
                leaveTypeId: 1,
                year,
                daysPerYear: 15,
                effectiveFrom: `${year}-06-01`,
                createdAtUtc: `${year}-05-20T00:00:00Z`,
                reason: 'Điều chỉnh giữa năm',
              },
            ],
          },
        };
      }
      throw new Error('Unexpected HR API path: ' + path);
    });

    const result = await getPolicies(year);

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
    expect(mocks.hrGet).toHaveBeenCalledTimes(1);
    expect(mocks.hrGet).toHaveBeenCalledWith('/api/v1/leave-catalog', {
      params: { year, includeInactive: true },
    });
  });

  it('resolves the policy version that is effective on the requested date', async () => {
    const year = getVietnamBusinessYear() + 1;
    mocks.hrGet.mockResolvedValue({
      data: {
        leaveTypes: [
          { id: 1, code: 'ANNUAL', name: 'Annual leave', isPaid: true, deductsBalance: true, isActive: true },
        ],
        leavePolicies: [
          {
            id: 11,
            leaveTypeId: 1,
            year,
            daysPerYear: 12,
            effectiveFrom: `${year}-01-01`,
            createdAtUtc: `${year}-01-01T00:00:00Z`,
          },
          {
            id: 12,
            leaveTypeId: 1,
            year,
            daysPerYear: 15,
            effectiveFrom: `${year}-07-01`,
            createdAtUtc: `${year}-06-20T00:00:00Z`,
          },
        ],
      },
    });

    const firstHalf = (await getPolicies(year, `${year}-06-30`))
      .find((policy) => policy.typeCode === 'ANNUAL');
    const secondHalf = (await getPolicies(year, `${year}-12-31`))
      .find((policy) => policy.typeCode === 'ANNUAL');

    expect(firstHalf).toMatchObject({
      policyId: 11,
      daysPerYear: 12,
      effectiveFrom: `${year}-01-01`,
    });
    expect(secondHalf).toMatchObject({
      policyId: 12,
      daysPerYear: 15,
      effectiveFrom: `${year}-07-01`,
    });
  });

  it('returns policy versions ordered by effective date and excludes legacy dates outside the policy year', async () => {
    const year = getVietnamBusinessYear();
    mocks.hrGet.mockResolvedValue({
      data: {
        leaveTypes: [
          { id: 1, code: 'ANNUAL', name: 'Annual leave', isPaid: true, deductsBalance: true, isActive: true },
          { id: 2, code: 'SICK', name: 'Sick leave', isPaid: true, deductsBalance: true, isActive: true },
          { id: 3, code: 'UNPAID', name: 'Unpaid leave', isPaid: false, deductsBalance: false, isActive: true },
        ],
        leavePolicies: [
          { id: 10, leaveTypeId: 1, year, daysPerYear: 12, effectiveFrom: `${year}-01-01` },
          { id: 11, leaveTypeId: 1, year, daysPerYear: 15, effectiveFrom: `${year}-06-01` },
          { id: 12, leaveTypeId: 1, year, daysPerYear: 20, effectiveFrom: `${year - 1}-11-18` },
          { id: 20, leaveTypeId: 2, year, daysPerYear: 10, effectiveFrom: `${year}-02-01` },
        ],
      },
    });

    const versions = await getPolicyVersions(year);

    expect(versions.map((policy) => ({
      typeCode: policy.typeCode,
      versionNumber: policy.versionNumber,
      policyId: policy.policyId,
      daysPerYear: policy.daysPerYear,
    }))).toEqual([
      { typeCode: 'ANNUAL', versionNumber: 1, policyId: 10, daysPerYear: 12 },
      { typeCode: 'ANNUAL', versionNumber: 2, policyId: 11, daysPerYear: 15 },
      { typeCode: 'SICK', versionNumber: 1, policyId: 20, daysPerYear: 10 },
    ]);
    expect(mocks.hrGet).toHaveBeenCalledWith('/api/v1/leave-catalog', {
      params: { year, includeInactive: true },
    });
  });

  it('keeps the legacy policy id but hides an effective date from the previous year', async () => {
    const year = getVietnamBusinessYear() + 1;
    mocks.hrGet.mockImplementation(async (path: string) => {
      if (path === '/api/v1/leave-catalog') {
        return {
          data: {
            leaveTypes: [
              { id: 1, code: 'ANNUAL', name: 'Annual leave', isPaid: true, deductsBalance: true, isActive: true },
            ],
            leavePolicies: [
              {
                id: 99,
                leaveTypeId: 1,
                year,
                daysPerYear: 15,
                effectiveFrom: `${year - 1}-11-18`,
                createdAtUtc: '2026-11-18T00:00:00Z',
                reason: 'Legacy version',
              },
            ],
          },
        };
      }
      throw new Error('Unexpected HR API path: ' + path);
    });

    const annual = (await getPolicies(year)).find((policy) => policy.typeCode === 'ANNUAL');

    expect(annual).toMatchObject({
      policyId: 99,
      daysPerYear: 15,
      effectiveFrom: undefined,
    });
  });

  // Khôi phục leave type chuẩn phải tạo loại nghỉ trước rồi mới tạo policy mặc định cho năm tương lai.
  it('restores a missing built-in type and creates its default annual policy', async () => {
    const year = getVietnamBusinessYear() + 1;
    mocks.hrGet.mockImplementation(async (path: string) => {
      if (path === '/api/v1/leave-catalog') {
        return { data: { leaveTypes: [], leavePolicies: [] } };
      }
      throw new Error('Unexpected HR API path: ' + path);
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

    expect(mocks.hrGet).toHaveBeenCalledTimes(1);
    expect(mocks.hrGet).toHaveBeenCalledWith('/api/v1/leave-catalog', {
      params: { year, includeInactive: true },
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

  // Thay đổi quota chỉ gọi Leave Policies API, không chạm tới Leave Types API.
  it('appends a policy version with PUT without updating the leave type', async () => {
    const year = getVietnamBusinessYear() + 1;
    mocks.hrPut.mockResolvedValue({ data: {} });

    await savePolicies([{
      id: 'type-1',
      policyId: 11,
      leaveTypeId: 1,
      year,
      effectiveFrom: `${year}-03-01`,
      reason: 'Điều chỉnh hạn mức',
      typeCode: 'ANNUAL',
      typeName: 'Nghỉ phép năm',
      daysPerYear: 15,
      paid: true,
      deductsBalance: true,
      description: 'Nghỉ phép năm',
      isActive: true,
    }]);

    expect(mocks.hrPut).toHaveBeenCalledTimes(1);
    expect(mocks.hrPut).toHaveBeenCalledWith('/api/v1/leave-policies/11', {
      leaveTypeId: 1,
      year,
      daysPerYear: 15,
      effectiveFrom: `${year}-03-01`,
      reason: 'Điều chỉnh hạn mức',
    });
    expect(mocks.hrPost).not.toHaveBeenCalled();
  });

  // Trường hợp loại nghỉ đã được khôi phục nhưng chưa có policy trong năm được chọn.
  it('creates the first policy for a future year with POST', async () => {
    const year = getVietnamBusinessYear() + 1;
    mocks.hrPost.mockResolvedValue({ data: {} });

    await savePolicies([{
      id: 'type-1',
      leaveTypeId: 1,
      year,
      effectiveFrom: `${year}-01-01`,
      reason: 'Khởi tạo chính sách',
      typeCode: 'ANNUAL',
      typeName: 'Nghỉ phép năm',
      daysPerYear: 15,
      paid: true,
      deductsBalance: true,
      description: 'Nghỉ phép năm',
      isActive: true,
    }]);

    expect(mocks.hrPost).toHaveBeenCalledTimes(1);
    expect(mocks.hrPost).toHaveBeenCalledWith('/api/v1/leave-policies', {
      leaveTypeId: 1,
      year,
      daysPerYear: 15,
      effectiveFrom: `${year}-01-01`,
      reason: 'Khởi tạo chính sách',
    });
    expect(mocks.hrPut).not.toHaveBeenCalled();
  });

  it('allows policy changes for the current business year from today onward', async () => {
    const year = getVietnamBusinessYear();
    const today = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Ho_Chi_Minh',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    }).format(new Date());
    mocks.hrPut.mockResolvedValue({ data: {} });

    await savePolicies([{
      id: 'type-1',
      policyId: 11,
      leaveTypeId: 1,
      year,
      effectiveFrom: today,
      typeCode: 'ANNUAL',
      typeName: 'Nghỉ phép năm',
      daysPerYear: 15,
      paid: true,
      deductsBalance: true,
      description: 'Nghỉ phép năm',
      isActive: true,
    }]);

    expect(mocks.hrPut).toHaveBeenCalledWith('/api/v1/leave-policies/11', expect.objectContaining({
      year,
      effectiveFrom: today,
    }));
    expect(mocks.hrPost).not.toHaveBeenCalled();
  });

  it('rejects an effective date in the previous year when editing a future policy', async () => {
    const year = getVietnamBusinessYear() + 1;

    await expect(savePolicies([{
      id: 'type-1',
      policyId: 11,
      leaveTypeId: 1,
      year,
      effectiveFrom: `${year - 1}-11-18`,
      typeCode: 'ANNUAL',
      typeName: 'Nghỉ phép năm',
      daysPerYear: 15,
      paid: true,
      deductsBalance: true,
      description: 'Nghỉ phép năm',
      isActive: true,
    }])).rejects.toThrow(`Ngày hiệu lực phải nằm trong năm ${year}`);

    expect(mocks.hrPut).not.toHaveBeenCalled();
    expect(mocks.hrPost).not.toHaveBeenCalled();
  });
});
