import { useEffect, useRef, useState } from 'react';
import type { PagedResult } from '../services/hrService';

// Mỗi request tải hai trang UI. Chuyển giữa hai trang dùng cùng block đã cache.
const PAGES_PER_BLOCK = 2;
const MAX_CACHED_BLOCKS = 4;
const MAX_PAGE_SIZE = 100;

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
  blocksRef.current = blocks;
  loadRef.current = load;

  const normalizedPage = Number.isFinite(page) ? Math.max(1, Math.floor(page)) : 1;
  const normalizedPageSize = Number.isFinite(pageSize)
    ? Math.min(MAX_PAGE_SIZE, Math.max(1, Math.floor(pageSize)))
    : 1;
  const blockIndex = Math.floor((normalizedPage - 1) / PAGES_PER_BLOCK);
  const requestPage = blockIndex + 1;
  const requestPageSize = Math.min(normalizedPageSize * PAGES_PER_BLOCK, MAX_PAGE_SIZE);
  const cacheNamespace = `${refreshToken}|${queryKey}|${normalizedPageSize}|`;
  const blockKey = `${cacheNamespace}${blockIndex}`;

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
      request = loadRef.current(requestPage, requestPageSize);
      inFlightRef.current.set(blockKey, request);
    }

    request
      .then((result) => {
        if (!active) return;

        setBlocks((current) => {
          const retainedKeys = Object.keys(current)
            .filter((key) => key.startsWith(cacheNamespace) && key !== blockKey)
            .slice(-(MAX_CACHED_BLOCKS - 1));
          const retained = Object.fromEntries(
            retainedKeys.map((key) => [key, current[key]]),
          );
          return { ...retained, [blockKey]: result };
        });

        // Count is stored per filter/page-size namespace, not inferred from
        // object insertion order or whichever block happened to load last.
        setKnownCounts((current) => ({
          ...Object.fromEntries(
            Object.entries(current).filter(([key]) => key === cacheNamespace),
          ),
          [cacheNamespace]: result.totalCount,
        }));

        setErrors((current) => {
          const next = { ...current };
          delete next[blockKey];
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
  }, [blockKey, cacheNamespace, requestPage, requestPageSize]);

  const block = blocks[blockKey];
  const offsetInBlock = ((normalizedPage - 1) % PAGES_PER_BLOCK) * normalizedPageSize;

  return {
    items: block?.items.slice(offsetInBlock, offsetInBlock + normalizedPageSize) ?? [],
    totalCount: knownCounts[cacheNamespace] ?? 0,
    loading: !block && !errors[blockKey],
    error: errors[blockKey] || '',
  };
}
