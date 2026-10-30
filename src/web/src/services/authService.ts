import type { AuthUser, UserRole } from '../types';
import { DEMO_MODE, TOKEN_STORAGE_KEY, USER_STORAGE_KEY } from './config';
import { hrApi } from './apiClient';
import { loadDemoState } from './mockStore';

interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  fullName: string;
  email: string;
  userId: string;
  role: string;
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
    };
    sessionStorage.setItem(TOKEN_STORAGE_KEY, user.accessToken);
    sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
    return user;
  }

  const { data } = await hrApi.post<LoginResponse>('/api/v1/auth/login', { email, password });
  const user: AuthUser = {
    id: data.userId,
    fullName: data.fullName,
    email: data.email,
    role: data.role as UserRole,
    accessToken: data.accessToken,
    expiresAtUtc: data.expiresAtUtc,
  };
  sessionStorage.setItem(TOKEN_STORAGE_KEY, user.accessToken);
  sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
  return user;
}

export function getStoredUser(): AuthUser | null {
  try {
    const raw = sessionStorage.getItem(USER_STORAGE_KEY);
    if (!raw) return null;
    const user = JSON.parse(raw) as AuthUser;
    if (user.expiresAtUtc && new Date(user.expiresAtUtc) < new Date()) {
      clearSession();
      return null;
    }
    return user;
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
  const { hrApi } = await import('./apiClient');
  try { await hrApi.put('/api/v1/me/password', { currentPassword, newPassword }); }
  catch (error) {
    const { getApiErrorMessage } = await import('./apiClient');
    throw new Error(getApiErrorMessage(error));
  }
}
