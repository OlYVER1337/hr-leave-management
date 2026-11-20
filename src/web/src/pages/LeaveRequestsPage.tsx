import { useCallback, useEffect, useState } from 'react';
import { CalendarDays, ChevronDown, Download, Eye, Filter, Search, XCircle } from 'lucide-react';
import { PageHeader, Avatar, EmptyState, formatDate, StatusBadge } from '../components/ui';
import { Modal } from '../components/Modal';
import { useAuth } from '../contexts';
import { useToast } from '../components/ToastProvider';
import { cancelLeaveRequest, getLeaveRequestsPage, type LeavePageQuery } from '../services/leaveService';
import { Pagination } from '../components/Pagination';
import { usePagedBlock } from '../hooks/usePagedBlock';
import { getApiErrorMessage } from '../services/apiClient';
import type { LeaveRequest, LeaveStatus } from '../types';

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

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedSearch(search), 300);
    return () => window.clearTimeout(handle);
  }, [search]);

  const mineOnly = !all && user?.role !== 'HR';
  const queryKey = JSON.stringify({
    search: debouncedSearch,
    status: status === 'All' ? undefined : status,
    typeCode: type === 'All' ? undefined : type,
    mineOnly,
    pageSize,
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



  async function onCancel(item: LeaveRequest) {
    if (!window.confirm(`Bạn có chắc muốn hủy đơn ${item.id} không?`)) return;
    try { await cancelLeaveRequest(item.id); notify('Đã hủy đơn nghỉ phép.'); setRefreshToken((value) => value + 1); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
  }

  const title = all ? 'Tất cả đơn nghỉ phép' : 'Đơn nghỉ phép của tôi';
  return <div className="page-stack">
    <PageHeader eyebrow="NGHỈ PHÉP" title={title} description={all ? 'Theo dõi hoạt động nghỉ phép trong toàn tổ chức.' : 'Theo dõi yêu cầu, thời gian nghỉ và trạng thái phê duyệt tại một nơi.'} actions={<button className="button button-secondary" disabled title="Chưa khả dụng: API xuất báo cáo chưa được triển khai"><Download size={16} /> Xuất báo cáo (chưa khả dụng)</button>} />
    <div className="page-summary-row"><div className="summary-metric"><span>Tổng số đơn</span><strong>{totalCount}</strong></div><div className="summary-metric"><span>Đang hiển thị ở trang này</span><strong>{source.length}</strong></div><div className="summary-metric"><span>Chờ duyệt ở trang này</span><strong>{source.filter((item) => item.status === 'Pending').length}</strong></div><div className="summary-metric"><span>Đã xử lý ở trang này</span><strong>{source.filter((item) => item.status !== 'Pending').length}</strong></div></div>
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <section className="panel page-table-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={17} /><input aria-label="Tìm đơn nghỉ phép" placeholder="Tìm theo mã đơn hoặc lý do..." value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }} /></div><div className="toolbar-filters"><label className="select-wrap"><Filter size={15} /><select aria-label="Lọc theo trạng thái" value={status} onChange={(event) => { setStatus(event.target.value as 'All' | LeaveStatus); setPage(1); }}><option value="All">Tất cả trạng thái</option><option value="Pending">Chờ duyệt</option><option value="Approved">Đã duyệt</option><option value="Rejected">Bị từ chối</option><option value="Cancelled">Đã hủy</option></select><ChevronDown size={14} /></label><label className="select-wrap"><select aria-label="Lọc theo loại nghỉ" value={type} onChange={(event) => { setType(event.target.value); setPage(1); }}><option value="All">Tất cả loại nghỉ</option><option value="ANNUAL">Nghỉ phép năm</option><option value="SICK">Nghỉ ốm</option><option value="UNPAID">Nghỉ không lương</option></select><ChevronDown size={14} /></label></div></div>
      {loading ? <div className="table-loading"><span /> Đang tải danh sách đơn…</div> : filtered.length === 0 ? <EmptyState title="Không tìm thấy đơn nghỉ" description="Thử đổi bộ lọc hoặc tạo yêu cầu nghỉ phép mới." /> : <div className="table-scroll"><table className="data-table requests-table"><thead><tr><th>Mã đơn</th>{(all || user?.role === 'HR') && <th>Nhân viên</th>}<th>Loại nghỉ</th><th>Thời gian</th><th>Số ngày</th><th>Người duyệt</th><th>Trạng thái</th><th /></tr></thead><tbody>{filtered.map((item) => <tr key={item.id}><td><div className="request-id-cell"><strong>{item.id}</strong><small>Gửi ngày {formatDate(item.createdAt.slice(0, 10))}</small></div></td>{(all || user?.role === 'HR') && <td><div className="person-cell"><Avatar name={item.employeeName} size="sm" /><div><strong>{item.employeeName}</strong><small>{item.employeeCode} · {item.departmentName}</small></div></div></td>}<td><span className="leave-type-cell"><span className={`leave-type-dot ${item.typeCode.toLowerCase()}`} />{item.typeName}</span></td><td><div className="date-range-cell">{formatDate(item.startDate)}<span>—</span>{formatDate(item.endDate)}</div></td><td><strong>{item.days}</strong> ngày</td><td>{item.approverName || '—'}</td><td><StatusBadge status={item.status} /></td><td><div className="row-actions">{item.status === 'Pending' && item.employeeId === user?.id && <button className="icon-button danger-icon" title="Hủy đơn" aria-label="Hủy đơn" onClick={() => void onCancel(item)}><XCircle size={17} /></button>}<button className="icon-button" title="Xem chi tiết đơn" aria-label={`Xem chi tiết ${item.id}`} onClick={() => setSelectedRequest(item)}><Eye size={17} /></button></div></td></tr>)}</tbody></table></div>}
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
    {selectedRequest && <Modal title={`Chi tiết đơn ${selectedRequest.id}`} description={`Gửi ngày ${formatDate(selectedRequest.createdAt.slice(0, 10))}`} onClose={() => setSelectedRequest(null)}>
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
      <div className="modal-actions"><button className="button button-secondary" onClick={() => setSelectedRequest(null)}>Đóng</button></div>
    </Modal>}
  </div>;
}
