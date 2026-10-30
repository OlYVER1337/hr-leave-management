export type UserRole = 'HR' | 'Manager' | 'Employee';
export type LeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';

export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
  role: UserRole;
  accessToken: string;
  expiresAtUtc?: string;
}

export interface Department {
  id: number;
  name: string;
}

export interface Employee {
  id: string;
  code: string;
  fullName: string;
  email: string;
  role: UserRole;
  hireDate: string;
  isActive: boolean;
  departmentId: number;
  departmentName: string;
  managerId: string | null;
  managerName: string | null;
}

export interface LeaveRequest {
  id: string;
  employeeId: string;
  employeeName: string;
  employeeCode: string;
  departmentName: string;
  typeCode: string;
  typeName: string;
  startDate: string;
  endDate: string;
  days: number;
  reason: string;
  status: LeaveStatus;
  approverName: string;
  createdAt: string;
  decisionNote?: string;
}

export interface LeaveBalance {
  code: string;
  name: string;
  entitled: number | null;
  used: number;
  pending: number;
  available: number | null;
  color: string;
}

export interface Holiday {
  id: string;
  name: string;
  date: string;
}

export interface LeavePolicy {
  id: string;
  typeCode: string;
  typeName: string;
  daysPerYear: number | null;
  paid: boolean;
  deductsBalance: boolean;
  description: string;
  isActive: boolean;
  leaveTypeId?: number;
  policyId?: number;
  year?: number;
}
