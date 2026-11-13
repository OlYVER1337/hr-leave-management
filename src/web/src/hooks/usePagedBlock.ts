import { useEffect, useRef, useState } from 'react';
import type { PagedResult } from '../services/hrService';

// Mỗi request tải một block gồm 2 trang UI. Chuyển trang trong block dùng cache;
// chỉ khi sang block mới mới gọi API.
const PAGES_PER_BLOCK = 2;
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
  const blocksRef = useRef(blocks);
  const loadRef = useRef(load);
  blocksRef.current = blocks;
  loadRef.current = load;

  const blockIndex = Math.floor((page - 1) / PAGES_PER_BLOCK);
  const requestPage = blockIndex + 1;
  const requestPageSize = Math.min(pageSize * PAGES_PER_BLOCK, 100);
  const cacheNamespace = `${refreshToken}|${queryKey}|${pageSize}|`;
  const blockKey = `${cacheNamespace}${blockIndex}`;

  useEffect(() => {
    if (blocksRef.current[blockKey]) return;

    let active = true;
    setErrors((current) => ({ ...current, [blockKey]: '' }));

    loadRef.current(requestPage, requestPageSize)
      .then((result) => {
        if (!active) return;

        setBlocks((current) => {
          const matching = Object.entries(current)
            .filter(([key]) => key.startsWith(cacheNamespace))
            .slice(-(MAX_CACHED_BLOCKS - 1));

          return { ...Object.fromEntries(matching), [blockKey]: result };
        });
        setErrors((current) => {
          const next = { ...current };
          delete next[blockKey];
          return next;
        });
      })
      .catch((reason: unknown) => {
        if (!active) return;
        setErrors((current) => {
          const retained = Object.entries(current).slice(-(MAX_CACHED_BLOCKS - 1));
          return {
            ...Object.fromEntries(retained),
            [blockKey]: reason instanceof Error ? reason.message : 'Không thể tải dữ liệu.',
          };
        });
      })
      .finally(() => {
        // The loading state is derived from the cache/error state above.
      });

    return () => { active = false; };
  }, [blockKey, cacheNamespace, requestPage, requestPageSize]);

  const block = blocks[blockKey];
  const lastKnownBlock = Object.entries(blocks)
    .filter(([key]) => key.startsWith(cacheNamespace))
    .at(-1)?.[1];
  const offsetInBlock = ((page - 1) % PAGES_PER_BLOCK) * pageSize;

  return {
    items: block?.items.slice(offsetInBlock, offsetInBlock + pageSize) ?? [],
    totalCount: block?.totalCount ?? lastKnownBlock?.totalCount ?? 0,
    loading: !block && !errors[blockKey],
    error: errors[blockKey] || '',
  };
}
