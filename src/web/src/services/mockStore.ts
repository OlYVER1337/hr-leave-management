import type { Department, Employee, Holiday, LeavePolicy, LeaveRequest } from '../types';

const STORAGE_KEY = 'hr_leave_demo_state_v1';

export interface DemoState {
  departments: Department[];
  employees: Employee[];
  requests: LeaveRequest[];
  holidays: Holiday[];
  policies: LeavePolicy[];
}

const seed: DemoState = {
  departments: [
    { id: 1, name: 'Kỹ thuật' },
    { id: 2, name: 'Nhân sự' },
    { id: 3, name: 'Sản phẩm & Thiết kế' },
    { id: 4, name: 'Tài chính' },
  ],
  employees: [
    { id: 'emp-hr-01', code: 'HR001', fullName: 'Nguyễn Nhật Minh', email: 'hr@company.com', role: 'HR', hireDate: '2022-01-10', isActive: true, departmentId: 2, departmentName: 'Nhân sự', managerId: null, managerName: null },
    { id: 'emp-mgr-01', code: 'ENG001', fullName: 'Trần Minh Anh', email: 'manager@company.com', role: 'Manager', hireDate: '2021-04-12', isActive: true, departmentId: 1, departmentName: 'Kỹ thuật', managerId: null, managerName: null },
    { id: 'emp-employee-01', code: 'ENG014', fullName: 'Lê Hoàng Nam', email: 'employee@company.com', role: 'Employee', hireDate: '2023-08-01', isActive: true, departmentId: 1, departmentName: 'Kỹ thuật', managerId: 'emp-mgr-01', managerName: 'Trần Minh Anh' },
    { id: 'emp-employee-02', code: 'PD008', fullName: 'Phạm Khánh Linh', email: 'linh@company.com', role: 'Employee', hireDate: '2024-02-19', isActive: true, departmentId: 3, departmentName: 'Sản phẩm & Thiết kế', managerId: 'emp-mgr-01', managerName: 'Trần Minh Anh' },
    { id: 'emp-employee-03', code: 'FIN006', fullName: 'Vũ Đức Thành', email: 'thanh@company.com', role: 'Employee', hireDate: '2024-06-03', isActive: false, departmentId: 4, departmentName: 'Tài chính', managerId: null, managerName: null },
  ],
  requests: [
    { id: 'LR-2026-014', employeeId: 'emp-employee-01', employeeName: 'Lê Hoàng Nam', employeeCode: 'ENG014', departmentName: 'Kỹ thuật', typeCode: 'ANNUAL', typeName: 'Nghỉ phép năm', startDate: '2026-10-29', endDate: '2026-10-30', days: 2, reason: 'Dự định đi du lịch cùng gia đình.', status: 'Pending', approverName: 'Trần Minh Anh', createdAt: '2026-10-20T08:20:00' },
    { id: 'LR-2026-013', employeeId: 'emp-employee-02', employeeName: 'Phạm Khánh Linh', employeeCode: 'PD008', departmentName: 'Sản phẩm & Thiết kế', typeCode: 'SICK', typeName: 'Nghỉ ốm', startDate: '2026-10-22', endDate: '2026-10-22', days: 1, reason: 'Lịch khám sức khỏe.', status: 'Approved', approverName: 'Trần Minh Anh', createdAt: '2026-10-18T10:05:00' },
    { id: 'LR-2026-012', employeeId: 'emp-employee-01', employeeName: 'Lê Hoàng Nam', employeeCode: 'ENG014', departmentName: 'Kỹ thuật', typeCode: 'ANNUAL', typeName: 'Nghỉ phép năm', startDate: '2026-10-09', endDate: '2026-10-09', days: 1, reason: 'Việc cá nhân.', status: 'Approved', approverName: 'Trần Minh Anh', createdAt: '2026-10-05T14:10:00' },
    { id: 'LR-2026-011', employeeId: 'emp-employee-03', employeeName: 'Vũ Đức Thành', employeeCode: 'FIN006', departmentName: 'Tài chính', typeCode: 'UNPAID', typeName: 'Nghỉ không lương', startDate: '2026-10-14', endDate: '2026-10-15', days: 2, reason: 'Sắp xếp việc cá nhân.', status: 'Rejected', approverName: 'Bộ phận nhân sự', createdAt: '2026-10-02T09:40:00', decisionNote: 'Vui lòng phối hợp với nhóm để sắp xếp công việc.' },
    { id: 'LR-2026-010', employeeId: 'emp-employee-02', employeeName: 'Phạm Khánh Linh', employeeCode: 'PD008', departmentName: 'Sản phẩm & Thiết kế', typeCode: 'ANNUAL', typeName: 'Nghỉ phép năm', startDate: '2026-11-02', endDate: '2026-11-03', days: 2, reason: 'Nghỉ ngắn ngày.', status: 'Pending', approverName: 'Trần Minh Anh', createdAt: '2026-10-19T16:15:00' },
  ],
  holidays: [
    { id: 'hol-1', name: 'Tết Dương lịch', date: '2027-01-01' },
    { id: 'hol-2', name: 'Giỗ Tổ Hùng Vương', date: '2027-04-16' },
    { id: 'hol-3', name: 'Ngày Giải phóng miền Nam', date: '2027-04-30' },
    { id: 'hol-4', name: 'Ngày Quốc tế Lao động', date: '2027-05-01' },
  ],
  policies: [
    { id: 'ANNUAL', typeCode: 'ANNUAL', typeName: 'Nghỉ phép năm', daysPerYear: 12, paid: true, deductsBalance: true, description: 'Hạn mức nghỉ phép năm hưởng lương theo năm dương lịch.', isActive: true },
    { id: 'SICK', typeCode: 'SICK', typeName: 'Nghỉ ốm', daysPerYear: 10, paid: true, deductsBalance: true, description: 'Nghỉ ốm hưởng lương với hạn mức năm riêng.', isActive: true },
    { id: 'UNPAID', typeCode: 'UNPAID', typeName: 'Nghỉ không lương', daysPerYear: null, paid: false, deductsBalance: false, description: 'Nghỉ không lương, không trừ vào số dư phép.', isActive: true },
  ],
};

export function loadDemoState(): DemoState {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) return JSON.parse(raw) as DemoState;
  } catch {
    localStorage.removeItem(STORAGE_KEY);
  }
  saveDemoState(seed);
  return structuredClone(seed);
}

export function saveDemoState(state: DemoState): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
}

export function updateDemoState(updater: (state: DemoState) => DemoState): DemoState {
  const next = updater(loadDemoState());
  saveDemoState(next);
  return next;
}

export function resetDemoState(): void {
  saveDemoState(structuredClone(seed));
}

export function makeId(prefix: string): string {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
}
