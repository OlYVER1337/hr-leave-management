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

import { getEmployeeOverviewPage, getEmployeePage } from './hrService';

describe('getEmployeeOverviewPage', () => {
  beforeEach(() => {
    hrGetMock.mockReset();
  });

  it('uses the combined overview endpoint for page data and summary in API mode', async () => {
    const overview = {
      items: [],
      totalCount: 0,
      summary: { total: 25, active: 23, managers: 4 },
    };
    hrGetMock.mockResolvedValue({ data: overview, headers: {} });

    await expect(getEmployeeOverviewPage({
      page: 1,
      pageSize: 10,
      search: 'Minh',
    })).resolves.toEqual(overview);

    expect(hrGetMock).toHaveBeenCalledTimes(1);
    expect(hrGetMock).toHaveBeenCalledWith('/api/v1/employees/overview', {
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
        directReportsOnly: undefined,
        sortBy: 'fullName',
        sortDirection: 'asc',
      },
    });
  });


  it('uses the shared employees endpoint for direct reports and never sends a manager ID', async () => {
    await getEmployeePage(
      { page: 1, pageSize: 10, search: 'An', directReportsOnly: true },
      { currentEmployeeId: 'manager-1' },
    );

    expect(hrGetMock).toHaveBeenCalledWith('/api/v1/employees', {
      params: expect.objectContaining({
        page: 1,
        pageSize: 10,
        search: 'An',
        directReportsOnly: true,
      }),
    });
    expect(hrGetMock.mock.calls[0][1].params).not.toHaveProperty('managerId');
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
