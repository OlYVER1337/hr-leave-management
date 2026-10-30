export const DEMO_MODE = (import.meta.env.VITE_DEMO_MODE ?? 'true').toLowerCase() === 'true';
export const HR_API_BASE_URL = import.meta.env.VITE_HR_API_BASE_URL || '/hr-api';
export const LEAVE_API_BASE_URL = import.meta.env.VITE_LEAVE_API_BASE_URL || '/leave-api';

export const TOKEN_STORAGE_KEY = 'hr_leave_access_token';
export const USER_STORAGE_KEY = 'hr_leave_user';
