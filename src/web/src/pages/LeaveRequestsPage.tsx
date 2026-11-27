import { useCallback, useEffect, useRef, useState } from 'react';
import { CalendarDays, ChevronDown, Download, Eye, Filter, History, Search, XCircle } from 'lucide-react';
import { PageHeader, Avatar, EmptyState, formatDate, StatusBadge } from '../components/ui';
import { Modal } from '../components/Modal';
import { useAuth } from '../contexts';
import { useToast } from '../components/ToastProvider';
import { cancelLeaveRequest, getLeaveRequestDetails, getLeaveRequestsPage, type LeavePageQuery } from '../services/leaveService';
import { Pagination } from '../components/Pagination';
import { usePagedBlock } from '../hooks/usePagedBlock';
import { getApiErrorMessage } from '../services/apiClient';
import type { LeaveRequest, LeaveRequestHistory, LeaveStatus } from '../types';
import './LeaveRequestHistory.css';
import { hasPermission } from '../utils/authorization';

function historyActionLabel(action: string): string {
  const labels: Record<string, string> = {
    Created: 'Tạo đơn',
    Approved: 'Phê duyệt đơn',
    Rejected: 'Từ chối đơn',
    Cancelled: 'Hủy đơn',
  };
  return labels[action] ?? action;
}

function historyStatusLabel(status: string | null): string {
  if (!status) return 'Khởi tạo';
  const labels: Record<string, string> = {
    Pending: 'Chờ duyệt',
    Approved: 'Đã duyệt',
    Rejected: 'Bị từ chối',
    Cancelled: 'Đã hủy',
  };
  return labels[status] ?? status;
}

function formatHistoryTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat('vi-VN', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

function historyActorLabel(
  actorId: string,
  request: LeaveRequest,
  currentUser: { id: string; fullName: string } | null | undefined,
): string {
  if (actorId === request.employeeId) return `${request.employeeName} (nhân viên)`;
  if (actorId === currentUser?.id) return `${currentUser.fullName} (bạn)`;
  if (actorId === request.approverId && request.approverName && request.approverId) {
    return `${request.approverName} (người duyệt)`;
  }
  return `Mã người thực hiện: ${actorId}`;
}

export function LeaveRequestsPage({ all = false }: { all?: boolean }) {
  const { user } = useAuth();
  const notify = useToast();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<'All' | LeaveStatus>('All');
  const [type, setType] = useState('All');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [refreshToken, setRefreshToken] = useState(0);
  const [selectedRequest, setSelectedRequest] = useState<LeaveRequest | null>(null);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [historyItems, setHistoryItems] = useState<LeaveRequestHistory[]>([]);
  const [historyLoadedRequestId, setHistoryLoadedRequestId] = useState<string | null>(null);
  const historyLoadSequence = useRef(0);

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedSearch(search), 300);
    return () => window.clearTimeout(handle);
  }, [search]);

  const canReadTeamOrAll = hasPermission(user?.permissions, 'leaves.read', ['Team', 'All']);
  const mineOnly = !all && !canReadTeamOrAll;
  const showEmployeeColumn = all || canReadTeamOrAll;
  const queryKey = JSON.stringify({
    search: debouncedSearch,
    status: status === 'All' ? undefined : status,
    typeCode: type === 'All' ? undefined : type,
    mineOnly,
  });
  const loadBlock = useCallback(
    (requestPage: number, requestPageSize: number) => {
      if (!user) return Promise.resolve({ items: [], totalCount: 0 });
      const query: LeavePageQuery = {
        page: requestPage,
        pageSize: requestPageSize,
        search: debouncedSearch || undefined,
        status: status === 'All' ? undefined : status,
        typeCode: type === 'All' ? undefined : type,
        mineOnly,
      };
      return getLeaveRequestsPage(user, query);
    },
    [user, queryKey],
  );
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
  const source = pageRequests;
  const filtered = pageRequests;
  const firstVisible = totalCount === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const lastVisible = Math.min(currentPage * pageSize, totalCount);
  useEffect(() => {
    if (page !== currentPage) setPage(currentPage);
  }, [page, currentPage]);



  async function loadRequestDetails(requestId: string, sequence: number) {
    if (!user) {
      setHistoryLoading(false);
      return;
    }
    setHistoryLoading(true);
    setHistoryError('');
    try {
      // API chi tiết trả cả request và lịch sử trong một lần gọi.
      const details = await getLeaveRequestDetails(requestId, user);
      if (historyLoadSequence.current !== sequence) return;
      setSelectedRequest(details.request);
      setHistoryItems(details.history);
      setHistoryLoadedRequestId(requestId);
    } catch (reason) {
      if (historyLoadSequence.current !== sequence) return;
      setHistoryError(getApiErrorMessage(reason));
      setHistoryOpen(true);
    } finally {
      if (historyLoadSequence.current === sequence) setHistoryLoading(false);
    }
  }

  function openRequestDetails(item: LeaveRequest) {
    const sequence = ++historyLoadSequence.current;
    setSelectedRequest(item);
    setHistoryOpen(false);
    setHistoryLoading(true);
    setHistoryError('');
    setHistoryItems([]);
    setHistoryLoadedRequestId(null);
    void loadRequestDetails(item.id, sequence);
  }

  function closeRequestDetails() {
    historyLoadSequence.current += 1;
    setSelectedRequest(null);
    setHistoryOpen(false);
    setHistoryLoading(false);
  }

  function toggleRequestHistory() {
    if (!selectedRequest) return;
    if (historyOpen) {
      setHistoryOpen(false);
      return;
    }

    setHistoryOpen(true);
    if (historyLoadedRequestId === selectedRequest.id || historyLoading) return;

    const sequence = ++historyLoadSequence.current;
    void loadRequestDetails(selectedRequest.id, sequence);
  }

  async function onCancel(item: LeaveRequest) {
    if (!window.confirm(`Bạn có chắc muốn hủy đơn ${item.id} không?`)) return;
    try { await cancelLeaveRequest(item.id); notify('Đã hủy đơn nghỉ phép.'); setRefreshToken((value) => value + 1); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
  }

  const title = all ? 'Tất cả đơn nghỉ phép' : mineOnly ? 'Đơn nghỉ phép của tôi' : 'Đơn nghỉ trong phạm vi được cấp';
  return <div className="page-stack">
    <PageHeader eyebrow="NGHỈ PHÉP" title={title} description={all ? 'Theo dõi hoạt động nghỉ phép trong toàn tổ chức.' : mineOnly ? 'Theo dõi yêu cầu, thời gian nghỉ và trạng thái phê duyệt của bạn.' : 'Theo dõi các yêu cầu nghỉ phép trong phạm vi quyền được cấp.'} actions={<button className="button button-secondary" disabled title="Chưa khả dụng: API xuất báo cáo chưa được triển khai"><Download size={16} /> Xuất báo cáo (chưa khả dụng)</button>} />
    <div className="page-summary-row"><div className="summary-metric"><span>Tổng số đơn</span><strong>{totalCount}</strong></div><div className="summary-metric"><span>Đang hiển thị ở trang này</span><strong>{source.length}</strong></div><div className="summary-metric"><span>Chờ duyệt ở trang này</span><strong>{source.filter((item) => item.status === 'Pending').length}</strong></div><div className="summary-metric"><span>Đã xử lý ở trang này</span><strong>{source.filter((item) => item.status !== 'Pending').length}</strong></div></div>
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <section className="panel page-table-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={17} /><input aria-label="Tìm đơn nghỉ phép" placeholder="Tìm theo mã đơn hoặc lý do..." value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /></div><div className="toolbar-filters"><label className="select-wrap"><Filter size={15} /><select aria-label="Lọc theo trạng thái" value={status} onChange={(event) => { setStatus(event.target.value as 'All' | LeaveStatus); setPage(1); }}><option value="All">Tất cả trạng thái</option><option value="Pending">Chờ duyệt</option><option value="Approved">Đã duyệt</option><option value="Rejected">Bị từ chối</option><option value="Cancelled">Đã hủy</option></select><ChevronDown size={14} /></label><label className="select-wrap"><select aria-label="Lọc theo loại nghỉ" value={type} onChange={(event) => { setType(event.target.value); setPage(1); }}><option value="All">Tất cả loại nghỉ</option><option value="ANNUAL">Nghỉ phép năm</option><option value="SICK">Nghỉ ốm</option><option value="UNPAID">Nghỉ không lương</option></select><ChevronDown size={14} /></label></div></div>
      {loading ? <div className="table-loading"><span /> Đang tải danh sách đơn…</div> : filtered.length === 0 ? <EmptyState title="Không tìm thấy đơn nghỉ" description="Thử đổi bộ lọc hoặc tạo yêu cầu nghỉ phép mới." /> : <div className="table-scroll"><table className="data-table requests-table"><thead><tr><th>Mã đơn</th>{showEmployeeColumn && <th>Nhân viên</th>}<th>Loại nghỉ</th><th>Thời gian</th><th>Số ngày</th><th>Người duyệt</th><th>Trạng thái</th><th /></tr></thead><tbody>{filtered.map((item) => <tr key={item.id}><td><div className="request-id-cell"><strong>{item.id}</strong><small>Gửi ngày {formatDate(item.createdAt.slice(0, 10))}</small></div></td>{showEmployeeColumn && <td><div className="person-cell"><Avatar name={item.employeeName} size="sm" /><div><strong>{item.employeeName}</strong><small>{item.employeeCode} · {item.departmentName}</small></div></div></td>}<td><span className="leave-type-cell"><span className={`leave-type-dot ${item.typeCode.toLowerCase()}`} />{item.typeName}</span></td><td><div className="date-range-cell">{formatDate(item.startDate)}<span>—</span>{formatDate(item.endDate)}</div></td><td><strong>{item.days}</strong> ngày</td><td>{item.approverName || '—'}</td><td><StatusBadge status={item.status} /></td><td><div className="row-actions">{hasPermission(user?.permissions, 'leaves.cancel', ['Self']) && item.status === 'Pending' && item.employeeId === user?.id && <button className="icon-button danger-icon" title="Hủy đơn" aria-label="Hủy đơn" onClick={() => void onCancel(item)}><XCircle size={17} /></button>}<button className="icon-button" title="Xem chi tiết đơn" aria-label={`Xem chi tiết ${item.id}`} onClick={() => openRequestDetails(item)}><Eye size={17} /></button></div></td></tr>)}</tbody></table></div>}
      <div className="table-footer">
        <span>Đang hiển thị <strong>{firstVisible}–{lastVisible}</strong> / <strong>{totalCount}</strong> đơn</span>
        <div className="employee-pagination">
          <label htmlFor="leave-page-size">Số dòng</label>
          <select id="leave-page-size" aria-label="Số đơn mỗi trang" value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}>
            {[10, 25, 50].map((size) => <option value={size} key={size}>{size}</option>)}
          </select>
          <Pagination page={currentPage} pageCount={pageCount} onPageChange={setPage} />
        </div>
        <span className="table-footer-note"><CalendarDays size={14} /> Ngày giờ theo múi giờ địa phương</span>
      </div>
    </section>
    {selectedRequest && <Modal title={`Chi tiết đơn ${selectedRequest.id}`} description={`Gửi ngày ${formatDate(selectedRequest.createdAt.slice(0, 10))}`} onClose={closeRequestDetails}>
      <div className="request-details">
        <div><span>Nhân viên</span><strong>{selectedRequest.employeeName} · {selectedRequest.employeeCode}</strong></div>
        <div><span>Phòng ban</span><strong>{selectedRequest.departmentName}</strong></div>
        <div><span>Loại nghỉ</span><strong>{selectedRequest.typeName}</strong></div>
        <div><span>Thời gian</span><strong>{formatDate(selectedRequest.startDate)} – {formatDate(selectedRequest.endDate)}</strong></div>
        <div><span>Số ngày</span><strong>{selectedRequest.days} ngày</strong></div>
        <div><span>Người duyệt</span><strong>{selectedRequest.approverName || '—'}</strong></div>
        <div><span>Trạng thái</span><StatusBadge status={selectedRequest.status} /></div>
        <div className="request-details-full"><span>Lý do</span><p>{selectedRequest.reason}</p></div>
        {selectedRequest.decisionNote && <div className="request-details-full"><span>Ghi chú phê duyệt</span><p>{selectedRequest.decisionNote}</p></div>}
      </div>
      <section className="request-history-panel" aria-label="Lịch sử xử lý đơn nghỉ">
        <div className="request-history-toolbar">
          <div>
            <h3>Lịch sử xử lý</h3>
            <p>Các thao tác đã được ghi nhận cho đơn nghỉ này.</p>
          </div>
          <button
            className="button button-secondary"
            type="button"
            onClick={() => void toggleRequestHistory()}
            disabled={historyLoading}
            aria-expanded={historyOpen}
            aria-controls="leave-request-history"
          >
            <History size={15} />
            {historyLoading ? 'Đang tải…' : historyOpen ? 'Ẩn lịch sử' : historyError ? 'Thử lại' : 'Xem lịch sử'}
          </button>
        </div>
        {historyOpen && (
          <div id="leave-request-history" className="request-history-content" aria-live="polite">
            {historyLoading && <div className="request-history-state">Đang tải thông tin đơn và lịch sử…</div>}
            {historyError && <div className="inline-alert alert-warning" role="alert">{historyError}</div>}
            {!historyLoading && !historyError && historyItems.length === 0 && (
              <div className="request-history-state">Chưa có bản ghi lịch sử cho đơn này.</div>
            )}
            {!historyLoading && !historyError && historyItems.length > 0 && (
              <ol className="leave-history-timeline">
                {historyItems.map((entry) => (
                  <li className="leave-history-item" key={entry.id}>
                    <span className="leave-history-marker" aria-hidden="true" />
                    <div className="leave-history-entry">
                      <div className="leave-history-entry-heading">
                        <strong>{historyActionLabel(entry.action)}</strong>
                        <time dateTime={entry.createdAtUtc}>{formatHistoryTimestamp(entry.createdAtUtc)}</time>
                      </div>
                      <p className="leave-history-actor">
                        Người thực hiện: {historyActorLabel(entry.actorId, selectedRequest, user)}
                      </p>
                      <p className="leave-history-status">
                        {historyStatusLabel(entry.fromStatus)} <span aria-hidden="true">→</span> {historyStatusLabel(entry.toStatus)}
                      </p>
                      {entry.note && <p className="leave-history-note">Ghi chú: {entry.note}</p>}
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </div>
        )}
      </section>
      <div className="modal-actions"><button className="button button-secondary" onClick={closeRequestDetails}>Đóng</button></div>
    </Modal>}
  </div>;
}
