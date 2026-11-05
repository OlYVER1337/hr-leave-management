import type { AuthUser, Holiday, LeaveBalance, LeavePolicy, LeaveRequest } from '../types';
import { DEMO_MODE } from './config';
import { getApiErrorMessage, hrApi, leaveApi } from './apiClient';
import { loadDemoState, updateDemoState } from './mockStore';

export interface CreateLeaveInput {
  typeCode: LeaveRequest['typeCode'];
  startDate: string;
  endDate: string;
  reason: string;
}

export interface RequestLeaveType {
  code: string;
  name: string;
  description: string;
  deductsBalance: boolean;
}

interface LeaveTypeRecord {
  id: number;
  code: string;
  name: string;
  isPaid: boolean;
  deductsBalance: boolean;
  isActive?: boolean;
}
interface LeavePolicyRecord { id: number; leaveTypeId: number; year: number; daysPerYear: number; }
interface LeaveBalanceRecord {
  leaveTypeId: number;
  year: number;
  entitled: number;
  used: number;
  pending: number;
  available: number;
}
interface HolidayRecord { id: number; name: string; holidayDate: string; }
interface EmployeeRecord {
  id: string;
  code: string;
  fullName: string;
  role: string;
  isActive: boolean;
  departmentId: number;
  departmentName: string;
  managerId: string | null;
  managerName: string | null;
}
interface LeaveRequestRecord {
  id: string;
  employeeId: string;
  approverId: string | null;
  departmentId: number;
  leaveTypeId: number;
  startDate: string;
  endDate: string;
  totalDays: number;
  reason: string;
  status: LeaveRequest['status'] | number;
  createdAtUtc: string;
}

function mapLeaveStatus(status: LeaveRequestRecord['status']): LeaveRequest['status'] {
  if (typeof status === 'string') {
    switch (status) {
      case 'Pending':
      case 'Approved':
      case 'Rejected':
      case 'Cancelled':
        return status;
      default:
        throw new Error(`Unknown leave request status: ${status}`);
    }
  }
  switch (status) {
    case 0: return 'Pending';
    case 1: return 'Approved';
    case 2: return 'Rejected';
    case 3: return 'Cancelled';
    default: throw new Error(`Unknown leave request status: ${status}`);
  }
}

const leaveColor = (code: string): string => code.toUpperCase() === 'ANNUAL' ? 'blue' : code.toUpperCase() === 'SICK' ? 'violet' : 'amber';
const leaveDescription = (code: string): string => {
  if (code.toUpperCase() === 'ANNUAL') return 'Nghỉ hưởng lương để nghỉ ngơi hoặc giải quyết việc cá nhân.';
  if (code.toUpperCase() === 'SICK') return 'Nghỉ để điều trị bệnh hoặc khám sức khỏe.';
  if (code.toUpperCase() === 'UNPAID') return 'Nghỉ không lương, không trừ vào số dư phép có lương.';
  return 'Nghỉ phép theo chính sách của tổ chức.';
};

const builtInLeaveTypes = [
  { code: 'ANNUAL', name: 'Nghỉ phép năm', isPaid: true, deductsBalance: true, defaultDays: 12 },
  { code: 'SICK', name: 'Nghỉ ốm', isPaid: true, deductsBalance: true, defaultDays: 10 },
  { code: 'UNPAID', name: 'Nghỉ không lương', isPaid: false, deductsBalance: false, defaultDays: null },
] as const;
type BuiltInLeaveTypeCode = typeof builtInLeaveTypes[number]['code'];

function getLocalizedLeaveTypeName(code: string, fallback: string): string {
  switch (code.toUpperCase()) {
    case 'ANNUAL': return 'Nghỉ phép năm';
    case 'SICK': return 'Nghỉ ốm';
    case 'UNPAID': return 'Nghỉ không lương';
    default: return fallback;
  }
}

function localizeLeaveRequest(request: LeaveRequest): LeaveRequest {
  return { ...request, typeName: getLocalizedLeaveTypeName(request.typeCode, request.typeName) };
}

export async function getRequestLeaveTypes(): Promise<RequestLeaveType[]> {
  if (DEMO_MODE) {
    return loadDemoState().policies
      .filter((policy) => policy.isActive && builtInLeaveTypes.some((type) => type.code === policy.typeCode.toUpperCase()))
      .map((policy) => ({
        code: policy.typeCode,
        name: getLocalizedLeaveTypeName(policy.typeCode, policy.typeName),
        description: policy.description,
        deductsBalance: policy.deductsBalance,
      }));
  }
  try {
    const { data } = await hrApi.get<LeaveTypeRecord[]>('/api/v1/leave-types');
    return data.map((type) => ({
      code: type.code.toUpperCase(),
      name: getLocalizedLeaveTypeName(type.code, type.name),
      description: leaveDescription(type.code),
      deductsBalance: type.deductsBalance,
    }));
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

function countWeekdays(startDate: string, endDate: string, holidays: Holiday[]): number {
  const skip = new Set(holidays.map((holiday) => holiday.date));
  const start = new Date(`${startDate}T12:00:00`);
  const end = new Date(`${endDate}T12:00:00`);
  let count = 0;
  for (const day = new Date(start); day <= end; day.setDate(day.getDate() + 1)) {
    const weekday = day.getDay();
    const iso = `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, '0')}-${String(day.getDate()).padStart(2, '0')}`;
    if (weekday !== 0 && weekday !== 6 && !skip.has(iso)) count++;
  }
  return count;
}

async function getEmployeeDirectory(user: AuthUser): Promise<Map<string, EmployeeRecord>> {
  if (user.role === 'HR') {
    const { data } = await hrApi.get<EmployeeRecord[]>('/api/v1/employees');
    return new Map(data.map((employee) => [employee.id, employee]));
  }

  const { data: currentEmployee } = await hrApi.get<EmployeeRecord>('/api/v1/me');
  const employees = [currentEmployee];
  if (user.role === 'Manager') {
    const { data: team } = await hrApi.get<EmployeeRecord[]>('/api/v1/me/team');
    employees.push(...team);
  }
  const directory = new Map(employees.map((employee) => [employee.id, employee]));
  if (currentEmployee.managerId && currentEmployee.managerName && !directory.has(currentEmployee.managerId)) {
    directory.set(currentEmployee.managerId, {
      ...currentEmployee,
      id: currentEmployee.managerId,
      fullName: currentEmployee.managerName,
    });
  }
  return directory;
}

async function mapApiRequests(records: LeaveRequestRecord[], user: AuthUser): Promise<LeaveRequest[]> {
  const [{ data: leaveTypes }, employees] = await Promise.all([
    hrApi.get<LeaveTypeRecord[]>('/api/v1/leave-types', { params: { includeInactive: user.role === 'HR' } }),
    getEmployeeDirectory(user),
  ]);
  return records.map((record) => {
    const employee = employees.get(record.employeeId);
    const approver = record.approverId ? employees.get(record.approverId) : undefined;
    const leaveType = leaveTypes.find((item) => item.id === record.leaveTypeId);
    const typeCode = leaveType?.code ?? `TYPE-${record.leaveTypeId}`;
    return {
      id: record.id,
      employeeId: record.employeeId,
      employeeName: employee?.fullName ?? record.employeeId,
      employeeCode: employee?.code ?? record.employeeId,
      departmentName: employee?.departmentName ?? String(record.departmentId),
      typeCode,
      typeName: getLocalizedLeaveTypeName(typeCode, leaveType?.name ?? typeCode),
      startDate: record.startDate,
      endDate: record.endDate,
      days: record.totalDays,
      reason: record.reason,
      status: mapLeaveStatus(record.status),
      approverName: approver?.fullName ?? (record.approverId ? 'Người duyệt không xác định' : '—'),
      createdAt: record.createdAtUtc,
    };
  });
}

export async function getLeaveRequests(user: AuthUser): Promise<LeaveRequest[]> {
  if (DEMO_MODE) return loadDemoState().requests.map(localizeLeaveRequest);
  try {
    const { data } = await leaveApi.get<LeaveRequestRecord[]>('/api/v1/leave-requests');
    return await mapApiRequests(data, user);
  }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getApprovalsInbox(user: AuthUser): Promise<LeaveRequest[]> {
  if (user.role === 'Employee') return [];
  if (DEMO_MODE) return loadDemoState().requests.filter((item) => item.status === 'Pending' && (user.role === 'HR' || item.approverName === user.fullName)).map(localizeLeaveRequest);
  try {
    const { data } = await leaveApi.get<LeaveRequestRecord[]>('/api/v1/leave-requests/pending-approval');
    return await mapApiRequests(data, user);
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getCalendarLeaves(user: AuthUser, from: string, to: string): Promise<LeaveRequest[]> {
  if (DEMO_MODE) return loadDemoState().requests.filter((item) => ['Pending', 'Approved'].includes(item.status) && item.startDate <= to && item.endDate >= from).map(localizeLeaveRequest);
  try {
    const { data } = await leaveApi.get<LeaveRequestRecord[]>('/api/v1/leave-requests/calendar', { params: { from, to } });
    return await mapApiRequests(data, user);
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getBalances(user: AuthUser): Promise<LeaveBalance[]> {
  if (DEMO_MODE) {
    const requests = loadDemoState().requests.filter((item) => item.employeeId === user.id);
    const used = (code: LeaveRequest['typeCode']) => requests.filter((item) => item.typeCode === code && item.status === 'Approved').reduce((sum, item) => sum + item.days, 0);
    const pending = (code: LeaveRequest['typeCode']) => requests.filter((item) => item.typeCode === code && item.status === 'Pending').reduce((sum, item) => sum + item.days, 0);
    const annualUsed = used('ANNUAL'); const annualPending = pending('ANNUAL');
    const sickUsed = used('SICK'); const sickPending = pending('SICK');
    return [
      { code: 'ANNUAL', name: 'Nghỉ phép năm', entitled: 12, used: annualUsed, pending: annualPending, available: 12 - annualUsed - annualPending, color: 'blue' },
      { code: 'SICK', name: 'Nghỉ ốm', entitled: 10, used: sickUsed, pending: sickPending, available: 10 - sickUsed - sickPending, color: 'violet' },
      { code: 'UNPAID', name: 'Nghỉ không lương', entitled: null, used: used('UNPAID'), pending: pending('UNPAID'), available: null, color: 'amber' },
    ];
  }
  try {
    const year = new Date().getFullYear();
    const [{ data: response }, { data: types }, { data: policies }] = await Promise.all([
      leaveApi.get<LeaveBalanceRecord[]>('/api/v1/leave-balances/mine', { params: { year } }),
      hrApi.get<LeaveTypeRecord[]>('/api/v1/leave-types'),
      hrApi.get<LeavePolicyRecord[]>('/api/v1/leave-policies', { params: { year } }),
    ]);
    return types.map((type) => {
      const balance = response.find((item) => item.leaveTypeId === type.id);
      const policy = policies.find((item) => item.leaveTypeId === type.id);
      const code = type.code.toUpperCase();
      const entitled = type.deductsBalance ? (balance?.entitled ?? policy?.daysPerYear ?? 0) : null;
      const used = balance?.used ?? 0;
      const pending = balance?.pending ?? 0;
      return {
        code,
        name: getLocalizedLeaveTypeName(type.code, type.name),
        entitled,
        used,
        pending,
        available: type.deductsBalance ? (balance?.available ?? Math.max(0, (entitled ?? 0) - used - pending)) : null,
        color: leaveColor(code),
      };
    });
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function createLeaveRequest(user: AuthUser, input: CreateLeaveInput): Promise<void> {
  const today = new Date();
  const todayDate = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
  if (input.startDate < todayDate) throw new Error('Ngày bắt đầu nghỉ phải từ hôm nay trở đi.');
  if (input.endDate < input.startDate) throw new Error('Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.');
  if (!input.reason.trim()) throw new Error('Vui lòng nhập lý do nghỉ phép.');
  if (DEMO_MODE) {
    updateDemoState((state) => {
      const days = countWeekdays(input.startDate, input.endDate, state.holidays);
      if (days < 1) throw new Error('Khoảng thời gian không có ngày làm việc hợp lệ.');
      const employee = state.employees.find((item) => item.id === user.id);
      if (!employee?.isActive) throw new Error('Tài khoản nhân viên không hoạt động.');
      const overlap = state.requests.some((item) => item.employeeId === user.id && ['Pending', 'Approved'].includes(item.status) && input.startDate <= item.endDate && input.endDate >= item.startDate);
      if (overlap) throw new Error('Khoảng thời gian bị trùng với đơn nghỉ đang chờ hoặc đã duyệt.');
      const policy = state.policies.find((item) => item.typeCode === input.typeCode && item.isActive);
      if (!policy) throw new Error('Loại nghỉ phép không khả dụng.');
      if (policy.deductsBalance && policy.daysPerYear !== null) {
        const taken = state.requests.filter((item) => item.employeeId === user.id && item.typeCode === input.typeCode && ['Pending', 'Approved'].includes(item.status)).reduce((sum, item) => sum + item.days, 0);
        if (taken + days > policy.daysPerYear) throw new Error(`Không đủ số dư ${policy.typeName.toLowerCase()}.`);
      }
      const approver = employee.managerName || 'HR Team';
      const request: LeaveRequest = { id: `LR-${new Date().getFullYear()}-${String(state.requests.length + 15).padStart(3, '0')}`, employeeId: user.id, employeeName: employee.fullName, employeeCode: employee.code, departmentName: employee.departmentName, typeCode: input.typeCode, typeName: getLocalizedLeaveTypeName(policy.typeCode, policy.typeName), startDate: input.startDate, endDate: input.endDate, days, reason: input.reason.trim(), status: 'Pending', approverName: approver, createdAt: new Date().toISOString() };
      return { ...state, requests: [request, ...state.requests] };
    });
    return;
  }
  try {
    const { data: leaveTypes } = await hrApi.get<LeaveTypeRecord[]>('/api/v1/leave-types');
    const leaveType = leaveTypes.find((item) => item.code.toUpperCase() === input.typeCode && item.isActive !== false);
    if (!leaveType) throw new Error(`Leave type ${input.typeCode} is not available in HR Service.`);
    await leaveApi.post<LeaveRequestRecord>('/api/v1/leave-requests', { leaveTypeId: leaveType.id, startDate: input.startDate, endDate: input.endDate, reason: input.reason.trim() });
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function cancelLeaveRequest(id: string): Promise<void> {
  if (DEMO_MODE) {
    updateDemoState((state) => {
      const current = state.requests.find((item) => item.id === id);
      if (!current) throw new Error('Không tìm thấy đơn nghỉ phép.');
      if (current.status !== 'Pending') throw new Error('Chỉ có thể hủy đơn đang chờ duyệt.');
      return { ...state, requests: state.requests.map((item) => item.id === id ? { ...item, status: 'Cancelled' } : item) };
    });
    return;
  }
  try { await leaveApi.post(`/api/v1/leave-requests/${encodeURIComponent(id)}/cancel`, { note: null }); }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function decideLeaveRequest(id: string, decision: 'approve' | 'reject', note = ''): Promise<void> {
  if (DEMO_MODE) {
    updateDemoState((state) => {
      const current = state.requests.find((item) => item.id === id);
      if (!current) throw new Error('Không tìm thấy đơn nghỉ phép.');
      if (current.status !== 'Pending') throw new Error('Đơn này không còn ở trạng thái chờ duyệt.');
      return { ...state, requests: state.requests.map((item) => item.id === id ? { ...item, status: decision === 'approve' ? 'Approved' : 'Rejected', decisionNote: note || undefined } : item) };
    });
    return;
  }
  try { await leaveApi.post(`/api/v1/leave-requests/${encodeURIComponent(id)}/${decision}`, { note }); }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getPolicies(year = new Date().getFullYear() + 1): Promise<LeavePolicy[]> {
  if (DEMO_MODE) {
    const savedPolicies = new Map(
      loadDemoState().policies.map((policy) => [policy.typeCode.toUpperCase(), policy]),
    );
    if ([...savedPolicies.keys()].some((code) => !builtInLeaveTypes.some((type) => type.code === code))) {
      updateDemoState((state) => ({
        ...state,
        policies: state.policies.filter((policy) =>
          builtInLeaveTypes.some((type) => type.code === policy.typeCode.toUpperCase())),
      }));
    }
    return builtInLeaveTypes.map((type) => {
      const policy = savedPolicies.get(type.code);
      return {
        id: policy?.id ?? `type-${type.code}`,
        year,
        typeCode: type.code,
        typeName: type.name,
        daysPerYear: type.deductsBalance ? policy?.daysPerYear ?? type.defaultDays : null,
        paid: type.isPaid,
        deductsBalance: type.deductsBalance,
        description: leaveDescription(type.code),
        isActive: policy?.isActive ?? false,
        leaveTypeId: policy?.leaveTypeId,
        policyId: policy?.policyId,
      };
    });
  }
  try {
    const [{ data: types }, { data: policies }] = await Promise.all([
      hrApi.get<LeaveTypeRecord[]>('/api/v1/leave-types', { params: { includeInactive: true } }),
      hrApi.get<LeavePolicyRecord[]>('/api/v1/leave-policies', { params: { year } }),
    ]);
    return builtInLeaveTypes.map((definition) => {
      const type = types.find((candidate) => candidate.code.toUpperCase() === definition.code);
      const policy = type && policies.find((candidate) => candidate.leaveTypeId === type.id && candidate.year === year);
      return {
        id: type ? `type-${type.id}` : `type-${definition.code}`,
        leaveTypeId: type?.id,
        policyId: policy?.id,
        year,
        typeCode: definition.code,
        typeName: definition.name,
        daysPerYear: definition.deductsBalance ? policy?.daysPerYear ?? definition.defaultDays : null,
        paid: definition.isPaid,
        deductsBalance: definition.deductsBalance,
        description: leaveDescription(definition.code),
        isActive: type?.isActive ?? false,
      };
    });
  } catch (error) { throw new Error(`Không tải được loại nghỉ/chính sách. ${getApiErrorMessage(error)}`); }
}

export async function restoreBuiltInLeaveType(policy: LeavePolicy): Promise<void> {
  const definition = builtInLeaveTypes.find((type) => type.code === policy.typeCode);
  if (!definition) throw new Error(`Unsupported built-in leave type: ${policy.typeCode}.`);
  const year = policy.year ?? new Date().getFullYear() + 1;
  if (DEMO_MODE) {
    updateDemoState((state) => {
      const existing = state.policies.find((item) => item.typeCode === policy.typeCode);
      const restored: LeavePolicy = {
        ...policy,
        id: existing?.id ?? policy.id,
        typeName: definition.name,
        paid: definition.isPaid,
        deductsBalance: definition.deductsBalance,
        daysPerYear: definition.deductsBalance
          ? existing?.daysPerYear ?? definition.defaultDays
          : null,
        description: leaveDescription(definition.code),
        isActive: true,
        year,
      };
      return {
        ...state,
        policies: existing
          ? state.policies.map((item) => item.typeCode === policy.typeCode ? restored : item)
          : [...state.policies, restored],
      };
    });
    return;
  }

  try {
    const [{ data: types }, { data: policies }] = await Promise.all([
      hrApi.get<LeaveTypeRecord[]>('/api/v1/leave-types', { params: { includeInactive: true } }),
      hrApi.get<LeavePolicyRecord[]>('/api/v1/leave-policies', { params: { year } }),
    ]);
    const existingType = types.find((item) => item.code.toUpperCase() === definition.code);
    let leaveTypeId: number;
    const typePayload = {
      name: definition.name,
      isPaid: definition.isPaid,
      deductsBalance: definition.deductsBalance,
      isActive: true,
    };
    if (existingType) {
      leaveTypeId = existingType.id;
      await hrApi.put(`/api/v1/leave-types/${leaveTypeId}`, typePayload);
    } else {
      const { data: createdType } = await hrApi.post<LeaveTypeRecord>('/api/v1/leave-types', {
        code: definition.code,
        ...typePayload,
      });
      leaveTypeId = createdType.id;
    }

    if (definition.deductsBalance && !policies.some((item) =>
      item.leaveTypeId === leaveTypeId && item.year === year)) {
      await hrApi.post('/api/v1/leave-policies', {
        leaveTypeId,
        year,
        daysPerYear: definition.defaultDays,
      });
    }
  } catch (error) { throw new Error(`Không thể thêm lại chính sách nghỉ. ${getApiErrorMessage(error)}`); }
}

export async function savePolicies(policies: LeavePolicy[]): Promise<void> {
  if (DEMO_MODE) {
    const definitions = new Map(builtInLeaveTypes.map((type) => [type.code, type]));
    updateDemoState((state) => ({
      ...state,
      policies: state.policies.map((existing) => {
        const updated = policies.find((policy) => policy.typeCode === existing.typeCode);
        const definition = definitions.get(existing.typeCode as BuiltInLeaveTypeCode);
        if (!updated || !definition) return existing;
        return {
          ...existing,
          ...updated,
          typeName: definition.name,
          paid: definition.isPaid,
          deductsBalance: definition.deductsBalance,
          description: leaveDescription(definition.code),
        };
      }),
    }));
    return;
  }
  try {
    const defaultYear = new Date().getFullYear() + 1;
    await Promise.all(policies.map(async (policy) => {
      if (!policy.leaveTypeId) throw new Error(`Missing leaveTypeId for ${policy.typeName}.`);
      const definition = builtInLeaveTypes.find((type) => type.code === policy.typeCode);
      if (!definition) throw new Error(`Unsupported leave type: ${policy.typeCode}.`);
      await hrApi.put(`/api/v1/leave-types/${policy.leaveTypeId}`, {
        name: definition.name,
        isPaid: definition.isPaid,
        deductsBalance: definition.deductsBalance,
        isActive: policy.isActive,
      });
      if (definition.deductsBalance && policy.daysPerYear !== null) {
        const body = { leaveTypeId: policy.leaveTypeId, year: policy.year ?? defaultYear, daysPerYear: policy.daysPerYear };
        if (policy.policyId) await hrApi.put(`/api/v1/leave-policies/${policy.policyId}`, body);
        else await hrApi.post('/api/v1/leave-policies', body);
      }
    }));
  } catch (error) { throw new Error(`Không lưu được chính sách. ${getApiErrorMessage(error)}`); }
}

export async function getHolidays(from?: string, to?: string): Promise<Holiday[]> {
  const currentYear = new Date().getFullYear();
  const start = from ?? `${currentYear}-01-01`;
  const end = to ?? `${currentYear + 1}-12-31`;
  if (DEMO_MODE) {
    const holidays = loadDemoState().holidays;
    return holidays.filter((holiday) => holiday.date >= start && holiday.date <= end);
  }
  try {
    const params: Record<string, string> = { from: start, to: end };
    const { data } = await hrApi.get<HolidayRecord[]>('/api/v1/holidays', { params });
    return data.map((item) => ({ id: String(item.id), name: item.name, date: item.holidayDate }));
  } catch (error) { throw new Error(`Không tải được ngày lễ. ${getApiErrorMessage(error)}`); }
}

export async function saveHolidays(holidays: Holiday[]): Promise<Holiday[]> {
  if (DEMO_MODE) { updateDemoState((state) => ({ ...state, holidays })); return holidays; }
  try {
    // The policy screen edits the visible holiday list as a whole, so diff against the server list.
    const existing = await getHolidays();
    const incomingIds = new Set(holidays.filter((item) => /^\d+$/.test(item.id)).map((item) => item.id));
    const existingIds = new Set(existing.map((item) => item.id));
    await Promise.all([
      ...holidays.map(async (holiday) => {
        const body = { name: holiday.name.trim(), holidayDate: holiday.date };
        if (/^\d+$/.test(holiday.id) && existingIds.has(holiday.id)) {
          await hrApi.put(`/api/v1/holidays/${holiday.id}`, body);
        } else {
          await hrApi.post('/api/v1/holidays', body);
        }
      }),
      ...existing.filter((holiday) => !incomingIds.has(holiday.id)).map((holiday) => hrApi.delete(`/api/v1/holidays/${holiday.id}`)),
    ]);
    return await getHolidays();
  } catch (error) { throw new Error(`Không lưu được ngày lễ. ${getApiErrorMessage(error)}`); }
}
