import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { usePagedBlock } from './usePagedBlock';
import type { PagedResult } from '../services/hrService';

function createPage(page: number, pageSize: number): PagedResult<number> {
  const first = (page - 1) * pageSize + 1;
  return {
    items: Array.from({ length: pageSize }, (_, index) => first + index),
    totalCount: 45,
  };
}

describe('usePagedBlock', () => {
  it('reuses one API block for two UI pages', async () => {
    const load = vi.fn((page: number, pageSize: number) =>
      Promise.resolve(createPage(page, pageSize)),
    );

    const { result, rerender } = renderHook(
      ({ page }) => usePagedBlock({
        page,
        pageSize: 10,
        queryKey: 'all-employees',
        refreshToken: 0,
        load,
      }),
      { initialProps: { page: 1 } },
    );

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.items).toEqual(Array.from({ length: 10 }, (_, i) => i + 1));
    expect(result.current.totalCount).toBe(45);
    expect(result.current.hasTotalCount).toBe(true);
    expect(load).toHaveBeenCalledTimes(1);
    expect(load).toHaveBeenNthCalledWith(1, 1, 20);

    rerender({ page: 2 });
    expect(result.current.items).toEqual(Array.from({ length: 10 }, (_, i) => i + 11));
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('loads the next API block when entering UI page three', async () => {
    const load = vi.fn((page: number, pageSize: number) =>
      Promise.resolve(createPage(page, pageSize)),
    );

    const { result, rerender } = renderHook(
      ({ page }) => usePagedBlock({
        page,
        pageSize: 10,
        queryKey: 'all-employees',
        refreshToken: 0,
        load,
      }),
      { initialProps: { page: 1 } },
    );

    await waitFor(() => expect(result.current.loading).toBe(false));
    rerender({ page: 3 });

    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(load).toHaveBeenNthCalledWith(2, 2, 20);
    expect(result.current.items).toEqual(Array.from({ length: 10 }, (_, i) => i + 21));
    expect(result.current.totalCount).toBe(45);
  });

  it('does not request more than 100 records for a UI page size of 100', async () => {
    const load = vi.fn((page: number, pageSize: number) =>
      Promise.resolve(createPage(page, pageSize)),
    );

    const { result, rerender } = renderHook(
      ({ page }) => usePagedBlock({
        page,
        pageSize: 100,
        queryKey: 'large-page',
        refreshToken: 0,
        load,
      }),
      { initialProps: { page: 1 } },
    );

    await waitFor(() => expect(result.current.loading).toBe(false));
    rerender({ page: 2 });

    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    expect(load).toHaveBeenNthCalledWith(1, 1, 100);
    expect(load).toHaveBeenNthCalledWith(2, 2, 100);
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.items[0]).toBe(101);
  });

  it('does not clamp the selected page before the new query has a count', async () => {
    const load = vi.fn((page: number, pageSize: number) =>
      Promise.resolve(createPage(page, pageSize)),
    );
    const { result, rerender } = renderHook(
      ({ queryKey }) => usePagedBlock({
        page: 3,
        pageSize: 10,
        queryKey,
        refreshToken: 0,
        load,
      }),
      { initialProps: { queryKey: 'first-filter' } },
    );

    await waitFor(() => expect(result.current.hasTotalCount).toBe(true));
    rerender({ queryKey: 'second-filter' });
    expect(result.current.hasTotalCount).toBe(false);
    expect(result.current.totalCount).toBe(0);
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(result.current.hasTotalCount).toBe(true));
  });

  it('exposes a failed block request without remaining in loading state', async () => {
    const load = vi.fn(() => Promise.reject(new Error('Không thể tải nhân viên.')));
    const { result } = renderHook(() => usePagedBlock({
      page: 1,
      pageSize: 10,
      queryKey: 'all-employees',
      refreshToken: 0,
      load,
    }));

    await waitFor(() => expect(result.current.error).toBe('Không thể tải nhân viên.'));
    expect(result.current.loading).toBe(false);
    expect(result.current.items).toEqual([]);
  });
});
