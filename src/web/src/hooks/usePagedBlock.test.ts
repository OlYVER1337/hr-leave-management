import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { usePagedBlock } from './usePagedBlock';
import type { PagedResult } from '../services/hrService';

function createPage(page: number, pageSize: number, totalCount = 45): PagedResult<number> {
  const first = (page - 1) * pageSize + 1;
  const itemCount = Math.max(0, Math.min(pageSize, totalCount - first + 1));
  return {
    items: Array.from({ length: itemCount }, (_, index) => first + index),
    totalCount,
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
      Promise.resolve(createPage(page, pageSize, 250)),
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

  it('does not reuse the count from a different filter', async () => {
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

  it('keeps the known count when the UI page size changes', async () => {
    let resolveRefresh: ((value: PagedResult<number>) => void) | undefined;
    const load = vi.fn((page: number, pageSize: number) => {
      if (load.mock.calls.length === 1) {
        return Promise.resolve(createPage(page, pageSize, 45));
      }
      return new Promise<PagedResult<number>>((resolve) => {
        resolveRefresh = resolve;
      });
    });

    const { result, rerender } = renderHook(
      ({ pageSize }) => usePagedBlock({
        page: 1,
        pageSize,
        queryKey: 'all-employees',
        refreshToken: 0,
        load,
      }),
      { initialProps: { pageSize: 10 } },
    );

    await waitFor(() => expect(result.current.hasTotalCount).toBe(true));
    rerender({ pageSize: 25 });

    expect(result.current.hasTotalCount).toBe(true);
    expect(result.current.totalCount).toBe(45);
    await waitFor(() => expect(load).toHaveBeenCalledTimes(2));
    expect(load).toHaveBeenNthCalledWith(2, 1, 50);

    await act(async () => {
      resolveRefresh?.(createPage(1, 50, 40));
    });

    await waitFor(() => expect(result.current.totalCount).toBe(40));
  });

  it('evicts the least recently used block after four blocks have been cached', async () => {
    const load = vi.fn((page: number, pageSize: number) =>
      Promise.resolve(createPage(page, pageSize, 100)),
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
    for (const page of [3, 5, 7]) {
      rerender({ page });
      await waitFor(() => expect(result.current.loading).toBe(false));
    }

    // Revisit block zero so it becomes the most recently used block.
    rerender({ page: 1 });
    expect(result.current.items[0]).toBe(1);
    expect(load).toHaveBeenCalledTimes(4);

    // Adding block four should evict block one, not the recently used block zero.
    rerender({ page: 9 });
    await waitFor(() => expect(load).toHaveBeenCalledTimes(5));
    await waitFor(() => expect(result.current.loading).toBe(false));

    rerender({ page: 1 });
    expect(result.current.items[0]).toBe(1);
    expect(load).toHaveBeenCalledTimes(5);

    rerender({ page: 3 });
    await waitFor(() => expect(load).toHaveBeenCalledTimes(6));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(load).toHaveBeenNthCalledWith(6, 2, 20);
    expect(result.current.items[0]).toBe(21);
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
