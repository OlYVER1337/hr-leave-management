import { beforeEach, describe, expect, it, vi } from 'vitest';

const { hrGetMock } = vi.hoisted(() => ({
  hrGetMock: vi.fn(),
}));

vi.mock('./config', () => ({
  DEMO_MODE: false,
}));

vi.mock('./apiClient', () => ({
  hrApi: { get: hrGetMock },
  getApiErrorMessage: (error: unknown) =>
    error instanceof Error ? error.message : 'API error',
}));

vi.mock('./mockStore', () => ({
  loadDemoState: vi.fn(),
  makeId: vi.fn(),
  updateDemoState: vi.fn(),
}));

import { getEmployeePage, getEmployeeSummary } from './hrService';

describe('getEmployeeSummary', () => {
  beforeEach(() => {
    hrGetMock.mockReset();
  });

  it('requests the permission-scoped employee summary endpoint', async () => {
    hrGetMock.mockResolvedValue({
      data: { total: 25, active: 23, managers: 4 },
      headers: {},
    });

    await expect(getEmployeeSummary()).resolves.toEqual({
      total: 25,
      active: 23,
      managers: 4,
    });

    expect(hrGetMock).toHaveBeenCalledTimes(1);
    expect(hrGetMock).toHaveBeenCalledWith('/api/v1/employees/summary');
  });
});

describe('getEmployeePage', () => {
  beforeEach(() => {
    hrGetMock.mockReset();
    hrGetMock.mockResolvedValue({
      data: [],
      headers: { 'x-total-count': '0' },
    });
  });

  it('sends page and pageSize as query parameters', async () => {
    await getEmployeePage({
      page: 1,
      pageSize: 10,
      search: 'Minh',
      sortBy: 'fullName',
      sortDirection: 'asc',
    });

    expect(hrGetMock).toHaveBeenCalledTimes(1);
    expect(hrGetMock).toHaveBeenCalledWith('/api/v1/employees', {
      params: {
        page: 1,
        pageSize: 10,
        search: 'Minh',
        role: undefined,
        isActive: undefined,
        departmentId: undefined,
        sortBy: 'fullName',
        sortDirection: 'asc',
      },
    });
  });

  it('normalizes page and pageSize to the API limits', async () => {
    await getEmployeePage({ page: 0, pageSize: 200 });

    expect(hrGetMock).toHaveBeenCalledWith('/api/v1/employees', {
      params: expect.objectContaining({
        page: 1,
        pageSize: 100,
      }),
    });
  });
});
