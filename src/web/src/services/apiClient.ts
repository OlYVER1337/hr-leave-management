import axios from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { HR_API_BASE_URL, LEAVE_API_BASE_URL, TOKEN_STORAGE_KEY, USER_STORAGE_KEY } from './config';
import type { AuthUser, UserRole } from '../types';

interface AuthenticationResponse {
  accessToken: string;
  expiresAtUtc: string;
  fullName: string;
  email: string;
  userId: string;
  role: string;
}

type RetryableRequestConfig = InternalAxiosRequestConfig & { _retry?: boolean };

let refreshPromise: Promise<string> | null = null;

function refreshAccessToken(): Promise<string> {
  if (!refreshPromise) {
    refreshPromise = axios
      .post<AuthenticationResponse>(
        `${HR_API_BASE_URL}/api/v1/auth/refresh`,
        null,
        { withCredentials: true, timeout: 12000 },
      )
      .then(({ data }) => {
        sessionStorage.setItem(TOKEN_STORAGE_KEY, data.accessToken);
        const user: AuthUser = {
          id: data.userId,
          fullName: data.fullName,
          email: data.email,
          role: data.role as UserRole,
          accessToken: data.accessToken,
          expiresAtUtc: data.expiresAtUtc,
        };
        sessionStorage.setItem(USER_STORAGE_KEY, JSON.stringify(user));
        window.dispatchEvent(new CustomEvent('auth:session-updated', {
          detail: { accessToken: user.accessToken, expiresAtUtc: user.expiresAtUtc },
        }));
        return data.accessToken;
      })
      .finally(() => {
        refreshPromise = null;
      });
  }
  return refreshPromise;
}

function createClient(baseURL: string) {
  const client = axios.create({
    baseURL,
    timeout: 12000,
    withCredentials: true,
    headers: { 'Content-Type': 'application/json' },
  });
  client.interceptors.request.use((config) => {
    const token = sessionStorage.getItem(TOKEN_STORAGE_KEY);
    if (token) config.headers.Authorization = `Bearer ${token}`;
    return config;
  });
  client.interceptors.response.use(undefined, async (error: unknown) => {
    if (!axios.isAxiosError(error) || error.response?.status !== 401 || !error.config) {
      return Promise.reject(error);
    }

    const config = error.config as RetryableRequestConfig;
    if (config._retry || config.url?.includes('/api/v1/auth/')) {
      return Promise.reject(error);
    }

    if (!sessionStorage.getItem(TOKEN_STORAGE_KEY)) {
      sessionStorage.removeItem(USER_STORAGE_KEY);
      window.dispatchEvent(new Event('auth:session-expired'));
      return Promise.reject(error);
    }

    config._retry = true;
    try {
      const token = await refreshAccessToken();
      config.headers.Authorization = `Bearer ${token}`;
      return await client.request(config);
    } catch (refreshError) {
      sessionStorage.removeItem(TOKEN_STORAGE_KEY);
      sessionStorage.removeItem(USER_STORAGE_KEY);
      window.dispatchEvent(new Event('auth:session-expired'));
      return Promise.reject(refreshError);
    }
  });
  return client;
}

export const hrApi = createClient(HR_API_BASE_URL);
export const leaveApi = createClient(LEAVE_API_BASE_URL);

export function getRejectedReason<T>(result: PromiseSettledResult<T>): unknown {
  return 'reason' in result ? result.reason : undefined;
}

export function getApiErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data as {
      detail?: string;
      title?: string;
      message?: string;
      errors?: Record<string, string[]>;
    } | undefined;
    if (data?.detail) return data.detail;
    if (data?.message) return data.message;
    if (data?.errors) {
      const details = Object.entries(data.errors)
        .flatMap(([field, messages]) => messages.map((message) => `${field}: ${message}`))
        .join(' ');
      if (details) return details;
    }
    if (data?.title) return data.title;
    if (error.code === 'ECONNABORTED') return 'API phản hồi quá lâu. Vui lòng thử lại.';
    if (!error.response) return 'Không thể kết nối API. Hãy kiểm tra backend và Docker Compose.';
    if (error.response.status === 401) return 'Phiên đăng nhập đã hết hạn hoặc không hợp lệ.';
    if (error.response.status === 403) return 'Bạn không có quyền thực hiện thao tác này.';
    if (error.response.status === 409) return 'Dữ liệu bị trùng hoặc đang có xung đột.';
  }
  return error instanceof Error ? error.message : 'Đã xảy ra lỗi không xác định.';
}
