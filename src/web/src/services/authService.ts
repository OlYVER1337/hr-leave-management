import type { AuthUser, PermissionGrant, UserRole } from '../types';
import { readPermissionGrants } from '../utils/authorization';
import { DEMO_MODE, TOKEN_STORAGE_KEY, USER_STORAGE_KEY } from './config';
import { getApiErrorMessage, hrApi } from './apiClient';
import { loadDemoState } from './mockStore';

interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  fullName: string;
  email: string;
  userId: string;
  role: string;
}

const demoPermissionGrants: Record<UserRole, PermissionGrant[]> = {
  Employee: [
    { p: 'employees.read', s: ['Self'] },
    { p: 'leaves.read', s: ['Self'] },
    { p: 'leaves.create', s: ['Self'] },
    { p: 'leaves.cancel', s: ['Self'] },
  ],
  Manager: [
    { p: 'employees.read', s: ['Self', 'Team'] },
    { p: 'leaves.read', s: ['Self', 'Team'] },
    { p: 'leaves.create', s: ['Self'] },
    { p: 'leaves.cancel', s: ['Self'] },
    { p: 'leaves.decide', s: ['Team'] },
  ],
  HR: [
    { p: 'employees.read', s: ['All'] },
    { p: 'employees.create', s: ['All'] },
    { p: 'employees.update', s: ['All'] },
    { p: 'employees.deactivate', s: ['All'] },
    { p: 'employees.password.reset', s: ['All'] },
    { p: 'leaves.read', s: ['All'] },
    { p: 'leaves.create', s: ['Self'] },
    { p: 'leaves.decide', s: ['All'] },
    { p: 'leaves.cancel', s: ['Self'] },
    { p: 'roles.manage', s: ['All'] },
    { p: 'departments.create', s: ['All'] },
    { p: 'departments.update', s: ['All'] },
    { p: 'departments.delete', s: ['All'] },
    { p: 'holidays.create', s: ['All'] },
    { p: 'holidays.update', s: ['All'] },
    { p: 'holidays.delete', s: ['All'] },
    { p: 'leave-policies.create', s: ['All'] },
    { p: 'leave-policies.update', s: ['All'] },
    { p: 'leave-types.create', s: ['All'] },
    { p: 'leave-types.update', s: ['All'] },
  ],
};

function getSessionPermissions(role: string, accessToken: string): PermissionGrant[] {
  if (DEMO_MODE) {
    return (demoPermissionGrants[role as UserRole] ?? []).map((grant) => ({
      p: grant.p,
      s: [...grant.s],
    }));
  }

  return readPermissionGrants(accessToken);
}

function saveUser(data: LoginResponse): AuthUser {
  const user: AuthUser = {
    id: data.userId,
    fullName: data.fullName,
    email: data.email,
    role: data.role,
    accessToken: data.accessToken,
    expiresAtUtc: data.expiresAtUtc,
    permissions: getSessionPermissions(data.role, data.accessToken),
  };
  sessionStorage.setItem(TOKEN_STORAGE_KEY, user.accessToken);
  sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
  return user;
}

export async function login(email: string, password: string): Promise<AuthUser> {
  if (DEMO_MODE) {
    const normalizedEmail = email.trim().toLowerCase();
    const demoAccounts: Record<string, { role: UserRole; id: string; fullName: string }> = {
      'hr@company.com': { role: 'HR', id: 'emp-hr-01', fullName: 'Nguyễn Nhật Minh' },
      'manager@company.com': { role: 'Manager', id: 'emp-mgr-01', fullName: 'Trần Minh Anh' },
      'employee@company.com': { role: 'Employee', id: 'emp-employee-01', fullName: 'Lê Hoàng Nam' },
    };
    const account = demoAccounts[normalizedEmail];
    if (!account || password !== 'Demo123!') throw new Error('Tài khoản demo hoặc mật khẩu không đúng.');
    const employee = loadDemoState().employees.find((item) => item.id === account.id);
    if (!employee?.isActive) throw new Error('Tài khoản đã bị vô hiệu hóa.');
    const user: AuthUser = {
      id: account.id,
      fullName: account.fullName,
      email: normalizedEmail,
      role: account.role,
      accessToken: `demo-token-${account.role.toLowerCase()}`,
      expiresAtUtc: new Date(Date.now() + 8 * 60 * 60 * 1000).toISOString(),
      permissions: getSessionPermissions(account.role, `demo-token-${account.role.toLowerCase()}`),
    };
    sessionStorage.setItem(TOKEN_STORAGE_KEY, user.accessToken);
    sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
    return user;
  }

  const { data } = await hrApi.post<LoginResponse>('/api/v1/auth/login', { email, password });
  return saveUser(data);
}

export async function restoreSession(): Promise<AuthUser> {
  if (DEMO_MODE) {
    const user = getStoredUser();
    if (!user) throw new Error('Không có phiên đăng nhập.');
    return user;
  }
  const { data } = await hrApi.post<LoginResponse>('/api/v1/auth/refresh');
  return saveUser(data);
}

export async function logout(): Promise<void> {
  try {
    if (!DEMO_MODE) await hrApi.post('/api/v1/auth/logout');
  } finally {
    clearSession();
  }
}

export function getStoredUser(): AuthUser | null {
  try {
    const raw = sessionStorage.getItem(USER_STORAGE_KEY);
    if (!raw) return null;

    const user = JSON.parse(raw) as AuthUser;
    if (
      !user ||
      typeof user.id !== 'string' ||
      typeof user.accessToken !== 'string' ||
      typeof user.role !== 'string' || !user.role.trim()
    ) {
      clearSession();
      return null;
    }

    if (user.expiresAtUtc && new Date(user.expiresAtUtc) < new Date()) {
      clearSession();
      return null;
    }

    // Không tin quyền lưu trong sessionStorage; dựng lại từ access token hiện tại.
    const restoredUser: AuthUser = {
      ...user,
      permissions: getSessionPermissions(user.role, user.accessToken),
    };
    sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(restoredUser));
    return restoredUser;
  } catch {
    clearSession();
    return null;
  }
}
export function clearSession(): void {
  sessionStorage.removeItem(TOKEN_STORAGE_KEY);
  sessionStorage.removeItem(USER_STORAGE_KEY);
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  if (DEMO_MODE) {
    if (currentPassword !== 'Demo123!') throw new Error('Mật khẩu hiện tại không chính xác.');
    if (newPassword.length < 12 || newPassword.length > 30) throw new Error('Mật khẩu phải có từ 12 đến 30 ký tự.');
    throw new Error('Chế độ dùng thử không lưu thay đổi mật khẩu. Hãy kết nối HR API để cập nhật thông tin tài khoản.');
  }
  try {
    await hrApi.put('/api/v1/me/password', { currentPassword, newPassword });
  } catch (error) {
    throw new Error(getApiErrorMessage(error));
  }
}
