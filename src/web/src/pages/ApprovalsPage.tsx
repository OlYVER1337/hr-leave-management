import { useEffect, useMemo, useState } from 'react';
import { Check, ChevronRight, Clock3, Filter, Search, X } from 'lucide-react';
import { Avatar, EmptyState, formatDate, PageHeader, StatusBadge } from '../components/ui';
import { useAuth } from '../contexts';
import { useToast } from '../components/ToastProvider';
import { decideLeaveRequest, getApprovalsInbox, getLeaveRequests } from '../services/leaveService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { LeaveRequest } from '../types';

export function ApprovalsPage() {
  const { user } = useAuth();
  const notify = useToast();
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [inbox, setInbox] = useState<LeaveRequest[]>([]);
  const [query, setQuery] = useState('');
  const [showPendingOnly, setShowPendingOnly] = useState(true);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [decisionId, setDecisionId] = useState<string | null>(null);
  const [decisionNote, setDecisionNote] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function reload() {
    setLoading(true); setError('');
    const results = await Promise.allSettled([user ? getLeaveRequests(user) : Promise.resolve([]), user ? getApprovalsInbox(user) : Promise.resolve([])]);
    if (results[0].status === 'fulfilled') setRequests(results[0].value); else setError(getApiErrorMessage(getRejectedReason(results[0])));
    if (results[1].status === 'fulfilled') setInbox(results[1].value); else setError((current) => current || getApiErrorMessage(getRejectedReason(results[1])));
    setLoading(false);
  }
  useEffect(() => { void reload(); }, [user]);

  const pending = useMemo(() => inbox, [inbox]);
  const filtered = useMemo(() => (showPendingOnly ? pending : requests).filter((item) => [item.employeeName, item.employeeCode, item.typeName, item.id, item.reason].some((value) => value.toLowerCase().includes(query.toLowerCase()))), [pending, requests, showPendingOnly, query]);

  async function decide(id: string, decision: 'approve' | 'reject') {
    setSubmitting(true);
    try { await decideLeaveRequest(id, decision, decisionNote.trim()); notify(decision === 'approve' ? 'Đã phê duyệt đơn nghỉ.' : 'Đã từ chối đơn nghỉ.'); setDecisionId(null); setDecisionNote(''); await reload(); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
    finally { setSubmitting(false); }
  }

  return <div className="page-stack">
    <PageHeader eyebrow="QUY TRÌNH ĐỘI NHÓM" title="Hộp thư phê duyệt" description="Xem xét yêu cầu nghỉ và đưa ra quyết định để đội nhóm phối hợp hiệu quả." actions={<div className="approval-count-pill"><Clock3 size={16} /><strong>{pending.length}</strong><span>đơn chờ duyệt</span></div>} />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <div className="approval-summary-grid"><div className="approval-summary approval-summary-pending"><span className="approval-summary-icon"><Clock3 size={18} /></span><div><strong>{pending.length}</strong><span>Chờ bạn xem xét</span></div></div><div className="approval-summary"><span className="approval-summary-icon green"><Check size={18} /></span><div><strong>{requests.filter((item) => item.status === 'Approved').length}</strong><span>Đơn đã duyệt</span></div></div><div className="approval-summary"><span className="approval-summary-icon violet"><Filter size={18} /></span><div><strong>{requests.length}</strong><span>Tổng số đơn</span></div></div></div>
    <section className="panel approval-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={17} /><input placeholder="Tìm theo nhân viên hoặc loại nghỉ…" aria-label="Tìm đơn cần duyệt" value={query} onChange={(event) => setQuery(event.target.value)} /></div><label className="toggle-filter"><input type="checkbox" checked={showPendingOnly} onChange={(event) => setShowPendingOnly(event.target.checked)} /><span className="toggle-track" /><span>Chỉ đơn chờ duyệt</span></label></div>
      {loading ? <div className="table-loading"><span /> Đang tải danh sách phê duyệt…</div> : filtered.length === 0 ? <EmptyState title={showPendingOnly ? 'Bạn đã xử lý hết' : 'Không tìm thấy đơn'} description={showPendingOnly ? 'Hiện không có đơn nào được giao cho bạn phê duyệt.' : 'Thử tìm kiếm bằng từ khóa khác.'} /> : <div className="approval-list">{filtered.map((item) => <article className="approval-card" key={item.id}><Avatar name={item.employeeName} size="lg" /><div className="approval-main"><div className="approval-person-line"><strong>{item.employeeName}</strong><span>{item.employeeCode}</span><StatusBadge status={item.status} /></div><p>{item.reason}</p><div className="approval-meta"><span><span className={`leave-type-dot ${item.typeCode.toLowerCase()}`} />{item.typeName}</span><span>{formatDate(item.startDate)} <span>→</span> {formatDate(item.endDate)}</span><span>{item.days} ngày</span></div></div><div className="approval-action-area">{item.status === 'Pending' ? <><button className="button button-approve" onClick={() => { setDecisionId(item.id); setDecisionNote(''); }}>Xem xét đơn <ChevronRight size={16} /></button><small>Gửi ngày {formatDate(item.createdAt.slice(0, 10))}</small></> : <span className="approval-final-label">Đã có quyết định</span>}</div></article>)}</div>}
    </section>

    {decisionId && <div className="modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget && !submitting) setDecisionId(null); }}><section className="modal" role="dialog" aria-modal="true" aria-label="Xem xét đơn nghỉ phép"><div className="modal-heading"><div><h2>Xem xét đơn nghỉ phép</h2><p>{requests.find((item) => item.id === decisionId)?.id}</p></div><button className="icon-button" aria-label="Đóng" onClick={() => setDecisionId(null)}><X size={18} /></button></div>{(() => { const item = requests.find((request) => request.id === decisionId); if (!item) return null; return <div className="decision-summary"><div className="person-cell"><Avatar name={item.employeeName} /><div><strong>{item.employeeName}</strong><small>{item.departmentName} · {item.employeeCode}</small></div></div><div className="decision-date-row"><span>{item.typeName}</span><strong>{item.days} ngày</strong></div><div className="decision-date-row"><span>{formatDate(item.startDate)} – {formatDate(item.endDate)}</span><span className="muted-copy">{item.id}</span></div><div className="decision-reason"><small>Lý do nghỉ</small><p>{item.reason}</p></div><div className="field-group"><label htmlFor="decisionNote">Ghi chú phê duyệt <span className="muted-copy">(không bắt buộc)</span></label><textarea id="decisionNote" rows={3} maxLength={300} value={decisionNote} onChange={(event) => setDecisionNote(event.target.value)} placeholder="Nhập ghi chú cho nhân viên…" /></div></div>; })()}<div className="modal-actions"><button className="button button-secondary" disabled={submitting} onClick={() => void decide(decisionId, 'reject')}><X size={16} /> Từ chối</button><button className="button button-primary" disabled={submitting} onClick={() => void decide(decisionId, 'approve')}><Check size={16} /> {submitting ? 'Đang lưu…' : 'Phê duyệt đơn'}</button></div></section></div>}
  </div>;
}
