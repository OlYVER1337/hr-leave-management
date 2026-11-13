import { ChevronLeft, ChevronRight } from 'lucide-react';

export function Pagination({
  page,
  pageCount,
  onPageChange,
}: {
  page: number;
  pageCount: number;
  onPageChange: (page: number) => void;
}) {
  if (pageCount <= 1) return null;
  return (
    <nav className="page-number-pagination" aria-label="Phân trang">
      <button type="button" className="icon-button" aria-label="Trang trước" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        <ChevronLeft size={16} />
      </button>
      <span className="page-number-count" aria-live="polite">Trang {page} / {pageCount}</span>
      <div className="page-number-list">
        {Array.from({ length: pageCount }, (_, index) => index + 1).map((number) => (
          <button
            type="button"
            key={number}
            className={number === page ? 'page-number is-current' : 'page-number'}
            aria-label={`Trang ${number}`}
            aria-current={number === page ? 'page' : undefined}
            onClick={() => onPageChange(number)}
          >
            {number}
          </button>
        ))}
      </div>
      <button type="button" className="icon-button" aria-label="Trang sau" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>
        <ChevronRight size={16} />
      </button>
    </nav>
  );
}
