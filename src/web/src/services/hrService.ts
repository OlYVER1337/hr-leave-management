import type { Department, Employee, UserRole } from '../types';
import { DEMO_MODE } from './config';
import { getApiErrorMessage, hrApi } from './apiClient';
import { loadDemoState, makeId, updateDemoState } from './mockStore';

export interface EmployeeInput {
  code: string;
  fullName: string;
  email: string;
  password?: string;
  role: UserRole;
  hireDate: string;
  departmentId: number;
  managerId: string | null;
}

export async function getEmployees(): Promise<Employee[]> {
  if (DEMO_MODE) return loadDemoState().employees;
  try {
    const { data } = await hrApi.get<Employee[]>('/api/v1/employees');
    return data;
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
}

export async function saveEmployee(input: EmployeeInput, id?: string): Promise<Employee> {
  if (DEMO_MODE) {
    let saved!: Employee;
    updateDemoState((state) => {
      const department = state.departments.find((item) => item.id === Number(input.departmentId));
      const manager = input.managerId ? state.employees.find((item) => item.id === input.managerId) : null;
      if (!department) throw new Error('Vui lòng chọn phòng ban hợp lệ.');
      const duplicate = state.employees.some((item) => (item.email.toLowerCase() === input.email.toLowerCase() || item.code.toLowerCase() === input.code.toLowerCase()) && item.id !== id);
      if (duplicate) throw new Error('Email hoặc mã nhân viên đã tồn tại.');
      if (id) {
        const current = state.employees.find((item) => item.id === id);
        if (!current) throw new Error('Không tìm thấy nhân viên.');
        saved = { ...current, ...input, managerId: input.managerId, managerName: manager?.fullName ?? null, departmentId: department.id, departmentName: department.name };
        return { ...state, employees: state.employees.map((item) => item.id === id ? saved : item) };
      }
      if (!input.password || input.password.length < 12) throw new Error('Mật khẩu phải có ít nhất 12 ký tự.');
      const { password: _temporaryPassword, ...employeeFields } = input;
      void _temporaryPassword;
      saved = { id: makeId('emp'), isActive: true, ...employeeFields, managerId: input.managerId, managerName: manager?.fullName ?? null, departmentId: department.id, departmentName: department.name };
      return { ...state, employees: [...state.employees, saved] };
    });
    return saved;
  }
  try {
    if (id) {
      const body = { code: input.code, fullName: input.fullName, email: input.email, role: input.role, hireDate: input.hireDate, departmentId: Number(input.departmentId), managerId: input.managerId || null };
      const { data } = await hrApi.put<Employee>(`/api/v1/employees/${id}`, body);
      return data;
    }
    const body = { code: input.code, fullName: input.fullName, email: input.email, password: input.password, role: input.role, hireDate: input.hireDate, departmentId: Number(input.departmentId), managerId: input.managerId || null };
    const { data } = await hrApi.post<Employee>('/api/v1/employees', body);
    return data;
  } catch (error) { throw new Error(getApiErrorMessage(error)); }
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

export async function getCurrentEmployee(id: string): Promise<Employee | null> {
  if (DEMO_MODE) return loadDemoState().employees.find((employee) => employee.id === id) ?? null;
  try { const { data } = await hrApi.get<Employee>('/api/v1/me'); return data; }
  catch (error) { throw new Error(getApiErrorMessage(error)); }
}
