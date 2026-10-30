import axios from 'axios';
import { HR_API_BASE_URL, LEAVE_API_BASE_URL, TOKEN_STORAGE_KEY } from './config';

function createClient(baseURL: string) {
  const client = axios.create({ baseURL, timeout: 12000, headers: { 'Content-Type': 'application/json' } });
  client.interceptors.request.use((config) => {
    const token = sessionStorage.getItem(TOKEN_STORAGE_KEY);
    if (token) config.headers.Authorization = `Bearer ${token}`;
    return config;
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
