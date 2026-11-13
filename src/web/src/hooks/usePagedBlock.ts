import { useEffect, useRef, useState } from 'react';
import type { PagedResult } from '../services/hrService';

const BUFFER_PAGES = 2;

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
  const [loadingKey, setLoadingKey] = useState<string | null>(null);
  const loadRef = useRef(load);
  loadRef.current = load;

  const blockIndex = Math.floor((page - 1) / BUFFER_PAGES);
  const requestPage = blockIndex + 1;
  const requestPageSize = Math.min(pageSize * BUFFER_PAGES, 100);
  const blockKey = `${refreshToken}|${queryKey}|${pageSize}|${blockIndex}`;
  const prefix = `${refreshToken}|${queryKey}|${pageSize}|`;

  useEffect(() => {
    if (blocks[blockKey]) return;
    let active = true;
    setLoadingKey(blockKey);
    setErrors((current) => ({ ...current, [blockKey]: '' }));
    loadRef.current(requestPage, requestPageSize)
      .then((result) => {
        if (!active) return;
        setBlocks((current) => {
          const retained = Object.entries(current)
            .filter(([key]) => key.startsWith(prefix))
            .slice(-3);
          return { ...Object.fromEntries(retained), [blockKey]: result };
        });
      })
      .catch((reason: unknown) => {
        if (active) setErrors((current) => ({
          ...current,
          [blockKey]: reason instanceof Error ? reason.message : 'Không thể tải dữ liệu.',
        }));
      })
      .finally(() => {
        if (active) setLoadingKey((current) => current === blockKey ? null : current);
      });
    return () => { active = false; };
  }, [blockKey, blocks, prefix, requestPage, requestPageSize]);

  const block = blocks[blockKey];
  const offsetInBlock = ((page - 1) % BUFFER_PAGES) * pageSize;
  return {
    items: block?.items.slice(offsetInBlock, offsetInBlock + pageSize) ?? [],
    totalCount: block?.totalCount ?? 0,
    loading: !block && loadingKey === blockKey,
    error: errors[blockKey] || '',
  };
}
