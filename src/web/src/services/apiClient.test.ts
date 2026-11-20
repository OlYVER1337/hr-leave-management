import axios, { AxiosError, AxiosHeaders, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { describe, expect, it } from 'vitest';
import { getApiErrorMessage } from './apiClient';

describe('getApiErrorMessage', () => {
  it('prefers field validation errors over the generic Problem Details detail', () => {
    const config = {} as InternalAxiosRequestConfig;
    const response = {
      data: {
        title: 'Validation failed',
        detail: 'Invalid request data',
        status: 422,
        errors: {
          effectiveFrom: ['A policy version cannot take effect before 2026-11-18.'],
          daysPerYear: ['Days per year must be between 0 and 366.'],
        },
      },
      status: 422,
      statusText: 'Unprocessable Entity',
      headers: new AxiosHeaders(),
      config,
    } satisfies AxiosResponse;

    const error = new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, undefined, response);

    expect(axios.isAxiosError(error)).toBe(true);
    expect(getApiErrorMessage(error)).toBe(
      'Dữ liệu không hợp lệ: Ngày hiệu lực: A policy version cannot take effect before 2026-11-18.; Số ngày mỗi năm: Days per year must be between 0 and 366.',
    );
  });

  it('uses the detail when the API has no field validation errors', () => {
    const config = {} as InternalAxiosRequestConfig;
    const response = {
      data: { title: 'Validation failed', detail: 'Invalid request data', status: 422 },
      status: 422,
      statusText: 'Unprocessable Entity',
      headers: new AxiosHeaders(),
      config,
    } satisfies AxiosResponse;
    const error = new AxiosError('Request failed', 'ERR_BAD_REQUEST', config, undefined, response);

    expect(getApiErrorMessage(error)).toBe('Invalid request data');
  });
});
