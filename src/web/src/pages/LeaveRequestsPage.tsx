import { useEffect, useMemo, useState } from 'react';
import { CalendarDays, ChevronDown, Download, Eye, Filter, Search, XCircle } from 'lucide-react';
import { PageHeader, Avatar, EmptyState, formatDate, StatusBadge } from '../components/ui';
import { useAuth } from '../contexts';
import { useToast } from '../components/ToastProvider';
import { cancelLeaveRequest, getLeaveRequests } from '../services/leaveService';
import { getApiErrorMessage } from '../services/apiClient';
import type { LeaveRequest, LeaveStatus } from '../types';

export function LeaveRequestsPage({ all = false }: { all?: boolean }) {
  const { user } = useAuth();
  const notify = useToast();
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<'All' | LeaveStatus>('All');
  const [type, setType] = useState('All');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  async function reload() {
    setLoading(true); setError('');
    if (!user) { setRequests([]); setLoading(false); return; }
    try { setRequests(await getLeaveRequests(user)); }
    catch (reason) { setError(getApiErrorMessage(reason)); }
    finally { setLoading(false); }
  }
  useEffect(() => { void reload(); }, [user]);

  const source = all || user?.role === 'HR' ? requests : requests.filter((item) => item.employeeId === user?.id);
  const filtered = useMemo(() => source.filter((item) => {
    const query = search.toLowerCase();
    const matchesSearch = [item.id, item.employeeName, item.employeeCode, item.reason, item.typeName].some((value) => value.toLowerCase().includes(query));
    return matchesSearch && (status === 'All' || item.status === status) && (type === 'All' || item.typeCode === type);
  }), [source, search, status, type]);

  async function onCancel(item: LeaveRequest) {
    if (!window.confirm(`Bạn có chắc muốn hủy đơn ${item.id} không?`)) return;
    try { await cancelLeaveRequest(item.id); notify('Đã hủy đơn nghỉ phép.'); await reload(); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
  }

  const title = all ? 'Tất cả đơn nghỉ phép' : 'Đơn nghỉ phép của tôi';
  return <div className="page-stack">
    <PageHeader eyebrow="NGHỈ PHÉP" title={title} description={all ? 'Theo dõi hoạt động nghỉ phép trong toàn tổ chức.' : 'Theo dõi yêu cầu, thời gian nghỉ và trạng thái phê duyệt tại một nơi.'} actions={<button className="button button-secondary" onClick={() => notify('Chức năng xuất báo cáo sẽ khả dụng khi có API báo cáo.', 'info')}><Download size={16} /> Xuất báo cáo</button>} />
    <div className="page-summary-row"><div className="summary-metric"><span>Tổng số đơn</span><strong>{source.length}</strong></div><div className="summary-metric"><span>Chờ duyệt</span><strong>{source.filter((item) => item.status === 'Pending').length}</strong></div><div className="summary-metric"><span>Đã duyệt</span><strong>{source.filter((item) => item.status === 'Approved').length}</strong></div><div className="summary-metric"><span>Bị từ chối</span><strong>{source.filter((item) => item.status === 'Rejected').length}</strong></div></div>
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <section className="panel page-table-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={17} /><input aria-label="Tìm đơn nghỉ phép" placeholder="Tìm theo nhân viên, mã đơn..." value={search} onChange={(event) => setSearch(event.target.value)} /></div><div className="toolbar-filters"><label className="select-wrap"><Filter size={15} /><select aria-label="Lọc theo trạng thái" value={status} onChange={(event) => setStatus(event.target.value as 'All' | LeaveStatus)}><option value="All">Tất cả trạng thái</option><option value="Pending">Chờ duyệt</option><option value="Approved">Đã duyệt</option><option value="Rejected">Bị từ chối</option><option value="Cancelled">Đã hủy</option></select><ChevronDown size={14} /></label><label className="select-wrap"><select aria-label="Lọc theo loại nghỉ" value={type} onChange={(event) => setType(event.target.value)}><option value="All">Tất cả loại nghỉ</option><option value="ANNUAL">Nghỉ phép năm</option><option value="SICK">Nghỉ ốm</option><option value="UNPAID">Nghỉ không lương</option></select><ChevronDown size={14} /></label></div></div>
      {loading ? <div className="table-loading"><span /> Đang tải danh sách đơn…</div> : filtered.length === 0 ? <EmptyState title="Không tìm thấy đơn nghỉ" description="Thử đổi bộ lọc hoặc tạo yêu cầu nghỉ phép mới." /> : <div className="table-scroll"><table className="data-table requests-table"><thead><tr><th>Mã đơn</th>{(all || user?.role === 'HR') && <th>Nhân viên</th>}<th>Loại nghỉ</th><th>Thời gian</th><th>Số ngày</th><th>Người duyệt</th><th>Trạng thái</th><th /></tr></thead><tbody>{filtered.map((item) => <tr key={item.id}><td><div className="request-id-cell"><strong>{item.id}</strong><small>Gửi ngày {formatDate(item.createdAt.slice(0, 10))}</small></div></td>{(all || user?.role === 'HR') && <td><div className="person-cell"><Avatar name={item.employeeName} size="sm" /><div><strong>{item.employeeName}</strong><small>{item.employeeCode} · {item.departmentName}</small></div></div></td>}<td><span className="leave-type-cell"><span className={`leave-type-dot ${item.typeCode.toLowerCase()}`} />{item.typeName}</span></td><td><div className="date-range-cell">{formatDate(item.startDate)}<span>—</span>{formatDate(item.endDate)}</div></td><td><strong>{item.days}</strong> ngày</td><td>{item.approverName || '—'}</td><td><StatusBadge status={item.status} /></td><td><div className="row-actions">{item.status === 'Pending' && item.employeeId === user?.id && <button className="icon-button danger-icon" title="Hủy đơn" aria-label="Hủy đơn" onClick={() => void onCancel(item)}><XCircle size={17} /></button>}<button className="icon-button" title="Xem chi tiết đơn" aria-label="Xem chi tiết" onClick={() => window.alert(`${item.id}\n${item.reason}${item.decisionNote ? `\nGhi chú phê duyệt: ${item.decisionNote}` : ''}`)}><Eye size={17} /></button></div></td></tr>)}</tbody></table></div>}
      <div className="table-footer"><span>Đang hiển thị <strong>{filtered.length}</strong> / <strong>{source.length}</strong> đơn</span><span className="table-footer-note"><CalendarDays size={14} /> Ngày giờ theo múi giờ địa phương</span></div>
    </section>
  </div>;
}
