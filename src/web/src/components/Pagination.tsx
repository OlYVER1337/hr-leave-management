import { ChevronLeft, ChevronRight } from 'lucide-react';

const SIBLING_COUNT = 2;

function getPageItems(page: number, pageCount: number): Array<number | 'ellipsis-left' | 'ellipsis-right'> {
  if (pageCount <= 9) {
    return Array.from({ length: pageCount }, (_, index) => index + 1);
  }

  const start = Math.max(2, page - SIBLING_COUNT);
  const end = Math.min(pageCount - 1, page + SIBLING_COUNT);
  const items: Array<number | 'ellipsis-left' | 'ellipsis-right'> = [1];

  if (start > 2) items.push('ellipsis-left');
  for (let number = start; number <= end; number++) items.push(number);
  if (end < pageCount - 1) items.push('ellipsis-right');

  items.push(pageCount);
  return items;
}

export function Pagination({
  page,
  pageCount,
  onPageChange,
}: {
  page: number;
  pageCount: number;
  onPageChange: (page: number) => void;
}) {
  if (pageCount < 1) return null;

  const items = getPageItems(page, pageCount);

  return (
    <nav className="page-number-pagination" aria-label="Phân trang">
      <button type="button" className="icon-button" aria-label="Trang trước" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        <ChevronLeft size={16} />
      </button>
      <span className="page-number-count" aria-live="polite">Trang {page} / {pageCount}</span>
      <div className="page-number-list">
        {items.map((item) => {
          if (typeof item !== 'number') {
            return <span key={item} className="page-number-ellipsis" aria-hidden="true">…</span>;
          }

          return (
            <button
              type="button"
              key={item}
              className={item === page ? 'page-number is-current' : 'page-number'}
              aria-label={`Trang ${item}`}
              aria-current={item === page ? 'page' : undefined}
              onClick={() => onPageChange(item)}
            >
              {item}
            </button>
          );
        })}
      </div>
      <button type="button" className="icon-button" aria-label="Trang sau" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>
        <ChevronRight size={16} />
      </button>
    </nav>
  );
}
