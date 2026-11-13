import type { Department, Employee, UserRole } from '../types';
import { DEMO_MODE } from './config';
import { getApiErrorMessage, hrApi } from './apiClient';
import { loadDemoState, makeId, updateDemoState } from './mockStore';

export interface EmployeeInput {
  code?: string;
  fullName: string;
  email: string;
  password?: string;
  role: UserRole;
  hireDate: string;
  departmentId: number;
  managerId: string | null;
}

export interface EmployeePageQuery {
  page: number;
  pageSize: number;
  search?: string;
  role?: UserRole;
  isActive?: boolean;
  departmentId?: number;
  sortBy?: 'fullName' | 'code' | 'departmentName' | 'role' | 'managerName' | 'hireDate' | 'isActive';
  sortDirection?: 'asc' | 'desc';
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
}

export interface EmployeeSummary {
  total: number;
  active: number;
  managers: number;
}

export async function getEmployeeSummary(): Promise<EmployeeSummary> {
  if (DEMO_MODE) {
    const employees = loadDemoState().employees;
    const activeEmployees = employees.filter((employee) => employee.isActive);
    return {
      total: employees.length,
      active: activeEmployees.length,
      managers: activeEmployees.filter((employee) => employee.role === 'Manager').length,
    };
  }

  try {
    const { data } = await hrApi.get<EmployeeSummary>('/api/v1/employees/summary');
    return data;
  } catch (error) {
    throw new Error(getApiErrorMessage(error));
  }
}

export interface EmployeeOverview extends PagedResult<Employee> {
  summary: EmployeeSummary;
}

export async function getEmployeeOverviewPage(
  query: EmployeePageQuery,
): Promise<EmployeeOverview> {
  const page = Math.max(1, query.page);
  const pageSize = Math.min(100, Math.max(1, query.pageSize));

  if (DEMO_MODE) {
    const [result, summary] = await Promise.all([
      getEmployeePage({ ...query, page, pageSize }),
      getEmployeeSummary(),
    ]);
    return { ...result, summary };
  }

  try {
    const { data } = await hrApi.get<EmployeeOverview>('/api/v1/employees/overview', {
      params: {
        page,
        pageSize,
        search: query.search || undefined,
        role: query.role,
        isActive: query.isActive,
        departmentId: query.departmentId,
        sortBy: query.sortBy ?? 'fullName',
        sortDirection: query.sortDirection ?? 'asc',
      },
    });
    return data;
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getEmployeePage(query: EmployeePageQuery): Promise<PagedResult<Employee>> {
  const page = Math.max(1, query.page);
  const pageSize = Math.min(100, Math.max(1, query.pageSize));
  if (DEMO_MODE) {
    const state = loadDemoState();
    const search = query.search?.trim().toLocaleLowerCase('vi-VN') ?? '';
    const filtered = state.employees.filter((employee) => {
      const matchesSearch = !search || [
        employee.fullName, employee.code, employee.email, employee.departmentName ?? '',
      ].some((value) => value.toLocaleLowerCase('vi-VN').includes(search));
      return matchesSearch &&
        (!query.role || employee.role === query.role) &&
        (query.isActive === undefined || employee.isActive === query.isActive) &&
        (query.departmentId === undefined || employee.departmentId === query.departmentId);
    });
    const collator = new Intl.Collator('vi', { sensitivity: 'base', numeric: true });
    const direction = query.sortDirection === 'desc' ? -1 : 1;
    const sortBy = query.sortBy ?? 'fullName';
    const sorted = [...filtered].sort((left, right) => {
      const values: Record<typeof sortBy, [string, string]> = {
        fullName: [left.fullName, right.fullName],
        code: [left.code, right.code],
        departmentName: [left.departmentName ?? '', right.departmentName ?? ''],
        role: [left.role, right.role],
        managerName: [left.managerName ?? '', right.managerName ?? ''],
        hireDate: [left.hireDate, right.hireDate],
        isActive: [String(left.isActive), String(right.isActive)],
      };
      return collator.compare(values[sortBy][0], values[sortBy][1]) * direction || left.id.localeCompare(right.id);
    });
    const offset = (page - 1) * pageSize;
    return { items: sorted.slice(offset, offset + pageSize), totalCount: sorted.length };
  }
  try {
    const { data, headers } = await hrApi.get<Employee[]>('/api/v1/employees', {
      params: {
        page, pageSize, search: query.search || undefined, role: query.role,
        isActive: query.isActive, departmentId: query.departmentId,
        sortBy: query.sortBy ?? 'fullName', sortDirection: query.sortDirection ?? 'asc',
      },
    });
    return { items: data, totalCount: Number(headers['x-total-count'] ?? data.length) };
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

// Compatibility helper for small dropdowns and summary widgets: the query is still bounded.
export async function getEmployees(): Promise<Employee[]> {
  return (await getEmployeePage({ page: 1, pageSize: 100 })).items;
}

export async function saveEmployee(input: EmployeeInput, id?: string): Promise<Employee> {
  if (DEMO_MODE) {
    let saved!: Employee;
    updateDemoState((state) => {
      const department = state.departments.find((item) => item.id === Number(input.departmentId));
      const manager = input.managerId ? state.employees.find((item) => item.id === input.managerId) : null;
      if (!department) throw new Error('Vui lòng chọn phòng ban hợp lệ.');
      const code = id ? input.code?.trim() : undefined;
      const duplicate = state.employees.some((item) => (item.email.toLowerCase() === input.email.toLowerCase() || (code && item.code.toLowerCase() === code.toLowerCase())) && item.id !== id);
      if (duplicate) throw new Error('Email hoặc mã nhân viên đã tồn tại.');
      if (id) {
        const current = state.employees.find((item) => item.id === id);
        if (!current) throw new Error('Không tìm thấy nhân viên.');
        saved = { ...current, ...input, code: code ?? current.code, managerId: input.managerId, managerName: manager?.fullName ?? null, departmentId: department.id, departmentName: department.name };
        return { ...state, employees: state.employees.map((item) => item.id === id ? saved : item) };
      }
      if (!input.password || input.password.length < 12) throw new Error('Mật khẩu phải có ít nhất 12 ký tự.');
      const generatedCode = generateEmployeeCode(state.employees);
      const { password: _temporaryPassword, code: _providedCode, ...employeeFields } = input;
      void _temporaryPassword;
      void _providedCode;
      saved = { id: makeId('emp'), isActive: true, ...employeeFields, code: generatedCode, managerId: input.managerId, managerName: manager?.fullName ?? null, departmentId: department.id, departmentName: department.name };
      return { ...state, employees: [...state.employees, saved] };
    });
    return saved;
  }
  try {
    if (id) {
      const body = { code: input.code ?? '', fullName: input.fullName, email: input.email, role: input.role, hireDate: input.hireDate, departmentId: Number(input.departmentId), managerId: input.managerId || null };
      const { data } = await hrApi.put<Employee>(`/api/v1/employees/${id}`, body);
      return data;
    }
    const body = { fullName: input.fullName, email: input.email, password: input.password, role: input.role, hireDate: input.hireDate, departmentId: Number(input.departmentId), managerId: input.managerId || null };
    const { data } = await hrApi.post<Employee>('/api/v1/employees', body);
    return data;
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

function generateEmployeeCode(employees: Employee[]): string {
  let code: string;
  do {
    code = `EMP-${crypto.randomUUID().replaceAll('-', '').slice(0, 16).toUpperCase()}`;
  } while (employees.some((employee) => employee.code.toUpperCase() === code));
  return code;
}

export async function deactivateEmployee(id: string): Promise<void> {
  if (DEMO_MODE) {
    updateDemoState((state) => ({ ...state, employees: state.employees.map((employee) => employee.id === id ? { ...employee, isActive: false } : employee) }));
    return;
  }
  try { await hrApi.post(`/api/v1/employees/${id}/deactivate`); }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getDepartments(): Promise<Department[]> {
  if (DEMO_MODE) return loadDemoState().departments;
  try { const { data } = await hrApi.get<Department[]>('/api/v1/departments'); return data; }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function saveDepartment(name: string, id?: number): Promise<Department> {
  if (DEMO_MODE) {
    let saved!: Department;
    updateDemoState((state) => {
      const normalized = name.trim();
      if (normalized.length < 2) throw new Error('Tên phòng ban phải có ít nhất 2 ký tự.');
      if (state.departments.some((item) => item.name.toLowerCase() === normalized.toLowerCase() && item.id !== id)) throw new Error('Tên phòng ban đã tồn tại.');
      if (id) {
        saved = { id, name: normalized };
        return { ...state, departments: state.departments.map((item) => item.id === id ? saved : item), employees: state.employees.map((employee) => employee.departmentId === id ? { ...employee, departmentName: normalized } : employee) };
      }
      saved = { id: Math.max(0, ...state.departments.map((item) => item.id)) + 1, name: normalized };
      return { ...state, departments: [...state.departments, saved] };
    });
    return saved;
  }
  try {
    if (id !== undefined) { const { data } = await hrApi.put<Department>(`/api/v1/departments/${id}`, { name }); return data; }
    const { data } = await hrApi.post<Department>('/api/v1/departments', { name }); return data;
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function deleteDepartment(id: number): Promise<void> {
  if (DEMO_MODE) {
    const state = loadDemoState();
    if (state.employees.some((employee) => employee.departmentId === id)) throw new Error('Không thể xóa phòng ban đang có nhân viên.');
    updateDemoState((current) => ({ ...current, departments: current.departments.filter((item) => item.id !== id) }));
    return;
  }
  try { await hrApi.delete(`/api/v1/departments/${id}`); }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getMyTeamPage(
  managerId: string,
  page: number,
  pageSize: number,
  search?: string,
): Promise<PagedResult<Employee>> {
  const normalizedPage = Math.max(1, page);
  const normalizedSize = Math.min(100, Math.max(1, pageSize));
  if (DEMO_MODE) {
    const state = loadDemoState();
    const term = search?.trim().toLocaleLowerCase('vi-VN') ?? '';
    const visible = state.employees
      .filter((employee) => employee.managerId === managerId && employee.isActive)
      .filter((employee) => !term || [
        employee.fullName, employee.code, employee.email, employee.departmentName ?? '',
      ].some((value) => value.toLocaleLowerCase('vi-VN').includes(term)))
      .sort((left, right) => left.fullName.localeCompare(right.fullName));
    const offset = (normalizedPage - 1) * normalizedSize;
    return { items: visible.slice(offset, offset + normalizedSize), totalCount: visible.length };
  }
  try {
    const { data, headers } = await hrApi.get<Employee[]>('/api/v1/me/team', {
      params: { page: normalizedPage, pageSize: normalizedSize, search: search || undefined },
    });
    return { items: data, totalCount: Number(headers?.['x-total-count'] ?? data.length) };
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getMyTeam(managerId: string): Promise<Employee[]> {
  if (DEMO_MODE) {
    return loadDemoState().employees
      .filter((employee) => employee.managerId === managerId && employee.isActive)
      .sort((a, b) => a.fullName.localeCompare(b.fullName));
  }
  try {
    const { data } = await hrApi.get<Employee[]>('/api/v1/me/team', { params: { page: 1, pageSize: 100 } });
    return data;
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function getCurrentEmployee(id: string): Promise<Employee | null> {
  if (DEMO_MODE) return loadDemoState().employees.find((employee) => employee.id === id) ?? null;
  try { const { data } = await hrApi.get<Employee>('/api/v1/me'); return data; }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}
