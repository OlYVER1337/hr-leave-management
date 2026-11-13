import { useCallback, useEffect, useState } from 'react';
import { Check, Clock3, Filter, Search, X } from 'lucide-react';
import { Avatar, EmptyState, formatDate, PageHeader, StatusBadge } from '../components/ui';
import { useAuth } from '../contexts';
import { useToast } from '../components/ToastProvider';
import {
  decideLeaveRequest,
  getApprovalsInboxPage,
  getLeaveRequestsPage,
} from '../services/leaveService';
import { getApiErrorMessage } from '../services/apiClient';
import type { LeaveRequest } from '../types';
import { Pagination } from '../components/Pagination';
import { usePagedBlock } from '../hooks/usePagedBlock';

const pageSizeOptions = [10, 25, 50];

export function ApprovalsPage() {
  const { user } = useAuth();
  const notify = useToast();
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [showPendingOnly, setShowPendingOnly] = useState(true);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(pageSizeOptions[0]);
  const [refreshToken, setRefreshToken] = useState(0);
  const [summary, setSummary] = useState({ pending: 0, approved: 0, total: 0 });
  const [summaryError, setSummaryError] = useState('');
  const [decisionRequest, setDecisionRequest] = useState<LeaveRequest | null>(null);
  const [decisionNote, setDecisionNote] = useState('');
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedQuery(query), 300);
    return () => window.clearTimeout(handle);
  }, [query]);

  const queryKey = JSON.stringify({
    search: debouncedQuery,
    pendingOnly: showPendingOnly,
    pageSize,
  });

  const loadBlock = useCallback((requestPage: number, requestPageSize: number) => {
    if (!user) return Promise.resolve({ items: [], totalCount: 0 });
    const pageQuery = {
      page: requestPage,
      pageSize: requestPageSize,
      search: debouncedQuery || undefined,
    };
    return showPendingOnly
      ? getApprovalsInboxPage(user, pageQuery)
      : getLeaveRequestsPage(user, pageQuery);
  }, [user, queryKey]);

  const {
    items: pageRequests,
    totalCount,
    loading,
    error,
  } = usePagedBlock<LeaveRequest>({
    page,
    pageSize,
    queryKey,
    refreshToken,
    load: loadBlock,
  });
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  const currentPage = Math.min(page, pageCount);
  const firstVisibleRequest = totalCount === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const lastVisibleRequest = Math.min(currentPage * pageSize, totalCount);

  useEffect(() => {
    if (page !== currentPage) setPage(currentPage);
  }, [page, currentPage]);

  useEffect(() => {
    if (!user) return;
    let active = true;
    setSummaryError('');
    Promise.allSettled([
      getApprovalsInboxPage(user, { page: 1, pageSize: 1 }),
      getLeaveRequestsPage(user, { page: 1, pageSize: 1, status: 'Approved' }),
      getLeaveRequestsPage(user, { page: 1, pageSize: 1 }),
    ]).then((results) => {
      if (!active) return;
      const [pendingResult, approvedResult, totalResult] = results;
      if (pendingResult.status === 'fulfilled') {
        setSummary((value) => ({ ...value, pending: pendingResult.value.totalCount }));
      } else {
        setSummaryError(getApiErrorMessage(pendingResult.reason));
      }
      if (approvedResult.status === 'fulfilled') {
        setSummary((value) => ({ ...value, approved: approvedResult.value.totalCount }));
      } else {
        setSummaryError((value) => value || getApiErrorMessage(approvedResult.reason));
      }
      if (totalResult.status === 'fulfilled') {
        setSummary((value) => ({ ...value, total: totalResult.value.totalCount }));
      } else {
        setSummaryError((value) => value || getApiErrorMessage(totalResult.reason));
      }
    });
    return () => { active = false; };
  }, [user, refreshToken]);

  async function decide(id: string, decision: 'approve' | 'reject') {
    setSubmitting(true);
    try {
      await decideLeaveRequest(id, decision, decisionNote.trim());
      notify(decision === 'approve' ? 'Đã phê duyệt đơn nghỉ.' : 'Đã từ chối đơn nghỉ.');
      setDecisionRequest(null);
      setDecisionNote('');
      setRefreshToken((value) => value + 1);
    } catch (reason) {
      notify(getApiErrorMessage(reason), 'error');
    } finally {
      setSubmitting(false);
    }
  }

  return <div className="page-stack">
    <PageHeader
      eyebrow="QUY TRÌNH ĐỘI NHÓM"
      title="Hộp thư phê duyệt"
      description="Xem xét yêu cầu nghỉ và đưa ra quyết định để đội nhóm phối hợp hiệu quả."
      actions={<div className="approval-count-pill"><Clock3 size={16} /><strong>{summary.pending}</strong><span>đơn chờ duyệt</span></div>}
    />
    {summaryError && <div className="inline-alert alert-warning">{summaryError}</div>}
    <div className="approval-summary-grid">
      <div className="approval-summary approval-summary-pending"><span className="approval-summary-icon"><Clock3 size={18} /></span><div><strong>{summary.pending}</strong><span>Chờ bạn xem xét</span></div></div>
      <div className="approval-summary"><span className="approval-summary-icon green"><Check size={18} /></span><div><strong>{summary.approved}</strong><span>Đơn đã duyệt</span></div></div>
      <div className="approval-summary"><span className="approval-summary-icon violet"><Filter size={18} /></span><div><strong>{summary.total}</strong><span>Tổng số đơn trong phạm vi</span></div></div>
    </div>
    <section className="panel approval-panel">
      <div className="table-toolbar">
        <div className="search-field">
          <Search size={17} />
          <input
            placeholder="Tìm theo mã đơn hoặc lý do…"
            aria-label="Tìm đơn cần duyệt"
            value={query}
            onChange={(event) => { setQuery(event.target.value); setPage(1); }}
          />
        </div>
        <label className="toggle-filter">
          <input type="checkbox" checked={showPendingOnly} onChange={(event) => { setShowPendingOnly(event.target.checked); setPage(1); }} />
          <span className="toggle-track" />
          <span>Chỉ đơn chờ duyệt</span>
        </label>
      </div>
      {loading ? (
        <div className="table-loading"><span /> Đang tải danh sách phê duyệt…</div>
      ) : error ? (
        <div className="inline-alert alert-warning">{error}</div>
      ) : pageRequests.length === 0 ? (
        <EmptyState
          title={showPendingOnly ? 'Bạn đã xử lý hết' : 'Không tìm thấy đơn'}
          description={showPendingOnly ? 'Hiện không có đơn nào được giao cho bạn phê duyệt.' : 'Thử tìm theo mã đơn hoặc lý do khác.'}
        />
      ) : (
        <div className="approval-list">
          {pageRequests.map((item) => (
            <article className="approval-card" key={item.id}>
              <Avatar name={item.employeeName} size="lg" />
              <div className="approval-main">
                <div className="approval-person-line"><strong>{item.employeeName}</strong><span>{item.employeeCode}</span><StatusBadge status={item.status} /></div>
                <p>{item.reason}</p>
                <div className="approval-meta">
                  <span><span className={`leave-type-dot ${item.typeCode.toLowerCase()}`} />{item.typeName}</span>
                  <span>{formatDate(item.startDate)} <span>→</span> {formatDate(item.endDate)}</span>
                  <span>{item.days} ngày</span>
                </div>
              </div>
              <div className="approval-action-area">
                {item.status === 'Pending'
                  ? <><button className="button button-approve" onClick={() => { setDecisionRequest(item); setDecisionNote(''); }}>Xem xét đơn <span aria-hidden="true">›</span></button><small>Gửi ngày {formatDate(item.createdAt.slice(0, 10))}</small></>
                  : <span className="approval-final-label">Đã có quyết định</span>}
              </div>
            </article>
          ))}
        </div>
      )}
      <div className="table-footer approval-pagination-footer">
        <span aria-live="polite">Đang hiển thị <strong>{firstVisibleRequest}–{lastVisibleRequest}</strong> / <strong>{totalCount}</strong> đơn</span>
        <div className="employee-pagination">
          <label htmlFor="approval-page-size">Số dòng</label>
          <select id="approval-page-size" aria-label="Số đơn mỗi trang" value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}>
            {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
          </select>
          <Pagination page={currentPage} pageCount={pageCount} onPageChange={setPage} />
        </div>
      </div>
    </section>

    {decisionRequest && (
      <div className="modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting) setDecisionRequest(null); }}>
        <section className="modal" role="dialog" aria-modal="true" aria-label="Xem xét đơn nghỉ phép">
          <div className="modal-heading">
            <div><h2>Xem xét đơn nghỉ phép</h2><p>{decisionRequest.id}</p></div>
            <button className="icon-button" aria-label="Đóng" onClick={() => setDecisionRequest(null)}><X size={18} /></button>
          </div>
          <div className="decision-summary">
            <div className="person-cell"><Avatar name={decisionRequest.employeeName} /><div><strong>{decisionRequest.employeeName}</strong><small>{decisionRequest.departmentName} · {decisionRequest.employeeCode}</small></div></div>
            <div className="decision-date-row"><span>{decisionRequest.typeName}</span><strong>{decisionRequest.days} ngày</strong></div>
            <div className="decision-date-row"><span>{formatDate(decisionRequest.startDate)} – {formatDate(decisionRequest.endDate)}</span><span className="muted-copy">{decisionRequest.id}</span></div>
            <div className="decision-reason"><small>Lý do nghỉ</small><p>{decisionRequest.reason}</p></div>
            <div className="field-group"><label htmlFor="decisionNote">Ghi chú phê duyệt <span className="muted-copy">(không bắt buộc)</span></label><textarea id="decisionNote" rows={3} maxLength={300} value={decisionNote} onChange={(event) => setDecisionNote(event.target.value)} placeholder="Nhập ghi chú cho nhân viên…" /></div>
          </div>
          <div className="modal-actions">
            <button className="button button-secondary" disabled={submitting} onClick={() => void decide(decisionRequest.id, 'reject')}><X size={16} /> Từ chối</button>
            <button className="button button-primary" disabled={submitting} onClick={() => void decide(decisionRequest.id, 'approve')}><Check size={16} /> {submitting ? 'Đang lưu…' : 'Phê duyệt đơn'}</button>
          </div>
        </section>
      </div>
    )}
  </div>;
}
