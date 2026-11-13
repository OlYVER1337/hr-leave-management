import { useEffect, useRef, useState } from 'react';
import type { PagedResult } from '../services/hrService';

// Mỗi request tải tối đa 2 trang UI nhưng không vượt giới hạn 100 bản ghi của API.
const MAX_PAGES_PER_BLOCK = 2;
const MAX_API_PAGE_SIZE = 100;
const MAX_CACHED_BLOCKS = 4;

export function usePagedBlock<T>({
  page,
  pageSize,
  queryKey,
  refreshToken,
  load,
}: {
  page: number;
  pageSize: number;
  queryKey: string;
  refreshToken: number;
  load: (page: number, pageSize: number) => Promise<PagedResult<T>>;
}) {
  const [blocks, setBlocks] = useState<Record<string, PagedResult<T>>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [knownCounts, setKnownCounts] = useState<Record<string, number>>({});
  const blocksRef = useRef(blocks);
  const loadRef = useRef(load);
  const inFlightRef = useRef(new Map<string, Promise<PagedResult<T>>>());
  // Keys are ordered from least recently used to most recently used.
  const accessOrderRef = useRef<string[]>([]);
  blocksRef.current = blocks;
  loadRef.current = load;

  const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.floor(page)) : 1;
  const normalizedPageSize = Number.isFinite(pageSize)
    ? Math.min(MAX_API_PAGE_SIZE, Math.max(1, Math.floor(pageSize)))
    : 1;
  // A UI page size of 100 must use one page per block; otherwise the second
  // UI page would require 200 records and be silently truncated by the API.
  const pagesPerBlock = Math.min(
    MAX_PAGES_PER_BLOCK,
    Math.max(1, Math.floor(MAX_API_PAGE_SIZE / normalizedPageSize)),
  );
  const blockIndex = Math.floor((normalizedPage - 1) / pagesPerBlock);
  const requestPage = blockIndex + 1;
  const requestPageSize = normalizedPageSize * pagesPerBlock;

  // Total count depends on the filters, not the selected UI page size.
  const countNamespace = queryKey;
  // The row cache does depend on page size because block boundaries change.
  const cacheNamespace = `${refreshToken}|${queryKey}|${normalizedPageSize}|`;
  const blockKey = `${cacheNamespace}${blockIndex}`;

  const touchBlock = (key: string) => {
    accessOrderRef.current = [
      ...accessOrderRef.current.filter((item) => item !== key),
      key,
    ];
  };

  useEffect(() => {
    accessOrderRef.current = accessOrderRef.current.filter((key) =>
      key.startsWith(cacheNamespace),
    );
  }, [cacheNamespace]);

  const block = blocks[blockKey];

  // Promote a cached block when the user navigates back to it.
  useEffect(() => {
    if (block) touchBlock(blockKey);
  }, [blockKey, block]);

  useEffect(() => {
    if (blocksRef.current[blockKey]) return;

    let active = true;
    setErrors((current) => {
      const next = { ...current };
      delete next[blockKey];
      return next;
    });

    let request = inFlightRef.current.get(blockKey);
    if (!request) {
      request = Promise.resolve().then(() =>
        loadRef.current(requestPage, requestPageSize),
      );
      inFlightRef.current.set(blockKey, request);
    }

    request
      .then((result) => {
        if (!active) return;

        touchBlock(blockKey);
        setBlocks((current) => {
          const retainedKeys = Object.keys(current)
            .filter((key) => key.startsWith(cacheNamespace) && key !== blockKey)
            .sort((left, right) =>
              accessOrderRef.current.indexOf(left) -
              accessOrderRef.current.indexOf(right),
            )
            .slice(-(MAX_CACHED_BLOCKS - 1));
          const retained = Object.fromEntries(
            retainedKeys.map((key) => [key, current[key]]),
          );
          const retainedSet = new Set([...retainedKeys, blockKey]);
          accessOrderRef.current = accessOrderRef.current.filter((key) =>
            retainedSet.has(key),
          );
          return { ...retained, [blockKey]: result };
        });

        // Keep the count for the active filter namespace independently from
        // block insertion order, cache eviction, page size, or current page.
        setKnownCounts((current) => ({
          ...Object.fromEntries(
            Object.entries(current).filter(([key]) => key === countNamespace),
          ),
          [countNamespace]: result.totalCount,
        }));

        setErrors((current) => {
          const next = Object.fromEntries(
            Object.entries(current).filter(([key]) =>
              key.startsWith(cacheNamespace) && key !== blockKey,
            ),
          );
          return next;
        });
      })
      .catch((reason: unknown) => {
        if (!active) return;
        setErrors((current) => ({
          ...Object.fromEntries(
            Object.entries(current)
              .filter(([key]) => key.startsWith(cacheNamespace) && key !== blockKey)
              .slice(-(MAX_CACHED_BLOCKS - 1)),
          ),
          [blockKey]: reason instanceof Error ? reason.message : 'Không thể tải dữ liệu.',
        }));
      })
      .finally(() => {
        if (inFlightRef.current.get(blockKey) === request) {
          inFlightRef.current.delete(blockKey);
        }
      });

    return () => {
      active = false;
    };
  }, [blockKey, cacheNamespace, countNamespace, requestPage, requestPageSize]);

  const hasTotalCount = Object.prototype.hasOwnProperty.call(knownCounts, countNamespace);

  return {
    items: block?.items.slice(offsetInBlock(normalizedPage, pagesPerBlock, normalizedPageSize),
      offsetInBlock(normalizedPage, pagesPerBlock, normalizedPageSize) + normalizedPageSize) ?? [],
    totalCount: knownCounts[countNamespace] ?? 0,
    hasTotalCount,
    loading: !block && !errors[blockKey],
    error: errors[blockKey] || '',
  };
}

function offsetInBlock(page: number, pagesPerBlock: number, pageSize: number): number {
  return ((page - 1) % pagesPerBlock) * pageSize;
}
