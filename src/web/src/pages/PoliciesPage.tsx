import { useEffect, useState, type FormEvent } from 'react';
import { CalendarDays, CheckCircle2, Edit3, Plus, ShieldCheck, Trash2 } from 'lucide-react';
import { PageHeader, EmptyState, formatDate } from '../components/ui';
import { Modal } from '../components/Modal';
import { useToast } from '../components/ToastProvider';
import { createLeaveType, getHolidays, getPolicies, saveHolidays, savePolicies } from '../services/leaveService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Holiday, LeavePolicy } from '../types';

const newHoliday = () => ({ id: '', name: '', date: '' });
const newLeaveType = () => ({ code: '', name: '', isPaid: true, deductsBalance: true });
export function PoliciesPage() {
  const notify = useToast();
  const [tab, setTab] = useState<'policies' | 'holidays'>('policies');
  const [policies, setPoliciesState] = useState<LeavePolicy[]>([]);
  const [holidays, setHolidaysState] = useState<Holiday[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [policyModal, setPolicyModal] = useState<LeavePolicy | null>(null);
  const [holidayModal, setHolidayModal] = useState<Holiday | null>(null);
  const [holidayForm, setHolidayForm] = useState(newHoliday());
  const [leaveTypeModal, setLeaveTypeModal] = useState(false);
  const [leaveTypeForm, setLeaveTypeForm] = useState(newLeaveType());
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let active = true;
    Promise.allSettled([getPolicies(), getHolidays()]).then((results) => {
      if (!active) return;
      if (results[0].status === 'fulfilled') setPoliciesState(results[0].value); else setError(getApiErrorMessage(getRejectedReason(results[0])));
      if (results[1].status === 'fulfilled') setHolidaysState(results[1].value); else setError((value) => value || getApiErrorMessage(getRejectedReason(results[1])));
      setLoading(false);
    });
    return () => { active = false; };
  }, []);

  async function updatePolicy(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!policyModal) return; setSaving(true);
    try {
      await savePolicies([policyModal]);
      setPoliciesState((current) => current.map((item) => item.id === policyModal.id ? policyModal : item));
      setPolicyModal(null);
      notify('Đã lưu chính sách nghỉ phép.');
    }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
    finally { setSaving(false); }
  }
  async function saveLeaveType(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    try {
      await createLeaveType({
        ...leaveTypeForm,
        code: leaveTypeForm.code.trim().toUpperCase(),
        name: leaveTypeForm.name.trim(),
        isPaid: leaveTypeForm.code.trim().toUpperCase() !== 'UNPAID' && leaveTypeForm.isPaid,
      });
      setLeaveTypeModal(false);
      setLeaveTypeForm(newLeaveType());
      try {
        setPoliciesState(await getPolicies());
        notify(leaveTypeForm.deductsBalance
          ? 'Đã tạo loại nghỉ. Hãy cấu hình hạn mức năm trong chính sách.'
          : 'Đã tạo loại nghỉ.');
      } catch (reason) {
        setError(`Đã tạo loại nghỉ nhưng không thể tải lại danh sách. ${getApiErrorMessage(reason)}`);
      }
    } catch (reason) {
      notify(getApiErrorMessage(reason), 'error');
    } finally {
      setSaving(false);
    }
  }
  function openCreateHoliday() { const created = { ...newHoliday(), id: `hol-${Date.now()}` }; setHolidayModal(created); setHolidayForm(created); }
  function openEditHoliday(holiday: Holiday) { setHolidayModal(holiday); setHolidayForm({ name: holiday.name, date: holiday.date, id: holiday.id }); }
  async function saveHoliday(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!holidayModal) return; setSaving(true);
    const record = { ...holidayForm, name: holidayForm.name.trim() };
    const updated = holidays.some((item) => item.id === record.id) ? holidays.map((item) => item.id === record.id ? record : item) : [...holidays, record];
    try { const saved = await saveHolidays(updated); setHolidaysState(saved); setHolidayModal(null); notify('Đã lưu ngày nghỉ lễ.'); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
    finally { setSaving(false); }
  }
  async function deleteHoliday(holiday: Holiday) {
    if (!window.confirm(`Bạn có chắc muốn xóa ngày lễ “${holiday.name}” không?`)) return;
    const updated = holidays.filter((item) => item.id !== holiday.id);
    try { const saved = await saveHolidays(updated); setHolidaysState(saved); notify('Đã xóa ngày nghỉ lễ.'); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
  }

  return <div className="page-stack">
    <PageHeader eyebrow="QUẢN TRỊ" title="Chính sách nghỉ phép" description="Cấu hình loại nghỉ, hạn mức phép và ngày nghỉ lễ của tổ chức." actions={tab === 'holidays' ? <button className="button button-primary" onClick={openCreateHoliday}><Plus size={17} /> Thêm ngày lễ</button> : <button className="button button-primary" onClick={() => { setLeaveTypeForm(newLeaveType()); setLeaveTypeModal(true); }}><Plus size={17} /> Thêm loại nghỉ</button>} />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <div className="policy-tabs"><button className={tab === 'policies' ? 'active' : ''} onClick={() => setTab('policies')}><ShieldCheck size={17} /> Loại nghỉ & hạn mức <span>{policies.length}</span></button><button className={tab === 'holidays' ? 'active' : ''} onClick={() => setTab('holidays')}><CalendarDays size={17} /> Ngày nghỉ lễ <span>{holidays.length}</span></button></div>
    {loading ? <div className="table-loading"><span /> Đang tải chính sách…</div> : tab === 'policies' ? <section className="policy-card-grid">{policies.length === 0 ? <EmptyState title="Chưa có loại nghỉ phép" description="Tạo loại nghỉ đang hoạt động trong HR Service trước khi cấu hình chính sách." /> : policies.map((policy) => <article className="panel policy-card" key={policy.id}><div className="policy-card-top"><span className={`policy-icon policy-${policy.typeCode.toLowerCase()}`}><ShieldCheck size={20} /></span><span className={`employee-status ${policy.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{policy.isActive ? 'Đang hoạt động' : 'Ngừng hoạt động'}</span></div><h2>{policy.typeName}</h2><p className="policy-description">{policy.description}</p><div className="policy-entitlement"><strong>{policy.daysPerYear === null ? 'Không giới hạn' : policy.daysPerYear}</strong><span>{policy.daysPerYear === null ? 'Không có hạn mức năm cố định' : 'ngày mỗi năm'}</span></div><div className="policy-properties"><span><i className={policy.paid ? 'property-yes' : 'property-no'} />{policy.paid ? 'Nghỉ hưởng lương' : 'Nghỉ không lương'}</span><span><i className={policy.deductsBalance ? 'property-yes' : 'property-no'} />{policy.deductsBalance ? 'Trừ vào số dư phép' : 'Không trừ số dư phép'}</span></div><button className="button button-secondary button-full" onClick={() => setPolicyModal({ ...policy })}><Edit3 size={15} /> Sửa chính sách</button></article>)}</section> : <section className="panel holiday-panel"><div className="panel-header"><div><h2>Lịch nghỉ lễ</h2><p>Ngày lễ không được tính vào số ngày làm việc khi tạo đơn nghỉ.</p></div><span className="holiday-count"><CalendarDays size={15} /> {holidays.length} ngày</span></div>{holidays.length === 0 ? <EmptyState title="Chưa cấu hình ngày lễ" description="Thêm ngày nghỉ lễ cho năm sắp tới." /> : <div className="table-scroll"><table className="data-table"><thead><tr><th>Ngày lễ</th><th>Ngày</th><th>Thứ</th><th /></tr></thead><tbody>{[...holidays].sort((a, b) => a.date.localeCompare(b.date)).map((holiday) => <tr key={holiday.id}><td><div className="holiday-name-cell"><span><CalendarDays size={17} /></span><strong>{holiday.name}</strong></div></td><td>{formatDate(holiday.date)}</td><td>{new Intl.DateTimeFormat('vi-VN', { weekday: 'long' }).format(new Date(`${holiday.date}T12:00:00`))}</td><td><div className="row-actions"><button className="icon-button" aria-label={`Sửa ${holiday.name}`} onClick={() => openEditHoliday(holiday)}><Edit3 size={16} /></button><button className="icon-button danger-icon" aria-label={`Xóa ${holiday.name}`} onClick={() => void deleteHoliday(holiday)}><Trash2 size={16} /></button></div></td></tr>)}</tbody></table></div>}</section>}
    <div className="policy-reminder"><CheckCircle2 size={18} /><p><strong>Chính sách cần được theo dõi.</strong> Ở chế độ dùng thử, dữ liệu được lưu cục bộ. Khi sử dụng thật, thay đổi sẽ được lưu qua HR Service và làm mới bộ nhớ đệm liên quan.</p></div>

    {leaveTypeModal && <Modal title="Thêm loại nghỉ" description="Tạo loại nghỉ mới. Hạn mức năm có thể cấu hình sau khi tạo." onClose={() => setLeaveTypeModal(false)}><form className="modal-form" onSubmit={(event) => void saveLeaveType(event)}>
      <div className="field-group"><label htmlFor="leave-type-code">Mã loại nghỉ *</label><input id="leave-type-code" required maxLength={20} pattern="[A-Za-z0-9_]+" value={leaveTypeForm.code} onChange={(event) => setLeaveTypeForm({ ...leaveTypeForm, code: event.target.value })} placeholder="Ví dụ: PARENTAL" /><small className="field-help">Tối đa 20 ký tự; chỉ dùng chữ không dấu, số và dấu gạch dưới.</small></div>
      <div className="field-group"><label htmlFor="leave-type-name">Tên loại nghỉ *</label><input id="leave-type-name" required maxLength={100} value={leaveTypeForm.name} onChange={(event) => setLeaveTypeForm({ ...leaveTypeForm, name: event.target.value })} placeholder="Ví dụ: Nghỉ chăm con" /></div>
      <label className="check-field"><input type="checkbox" checked={leaveTypeForm.isPaid && leaveTypeForm.code.trim().toUpperCase() !== 'UNPAID'} disabled={leaveTypeForm.code.trim().toUpperCase() === 'UNPAID'} onChange={(event) => setLeaveTypeForm({ ...leaveTypeForm, isPaid: event.target.checked })} /> Nghỉ hưởng lương</label>
      <label className="check-field"><input type="checkbox" checked={leaveTypeForm.deductsBalance} onChange={(event) => setLeaveTypeForm({ ...leaveTypeForm, deductsBalance: event.target.checked })} /> Trừ vào số dư phép</label>
      <div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setLeaveTypeModal(false)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang tạo…' : 'Tạo loại nghỉ'}</button></div>
    </form></Modal>}
    {policyModal && <Modal title={`Sửa ${policyModal.typeName}`} description="Cập nhật quy định hạn mức cho loại nghỉ này." onClose={() => setPolicyModal(null)}><form className="modal-form" onSubmit={updatePolicy}><div className="field-group"><label htmlFor="policy-name">Tên hiển thị *</label><input id="policy-name" required value={policyModal.typeName} onChange={(event) => setPolicyModal({ ...policyModal, typeName: event.target.value })} /></div><div className="field-group"><label htmlFor="policy-days">Số ngày mỗi năm</label><input id="policy-days" type="number" min={0} max={366} disabled={!policyModal.deductsBalance} value={policyModal.daysPerYear ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, daysPerYear: event.target.value === '' ? null : Number(event.target.value) })} /><small className="field-help">Để loại nghỉ trừ vào số dư, hãy nhập hạn mức ngày phép năm.</small></div><label className="check-field"><input type="checkbox" checked={policyModal.typeCode === 'UNPAID' ? false : policyModal.paid} disabled={policyModal.typeCode === 'UNPAID'} onChange={(event) => setPolicyModal({ ...policyModal, paid: event.target.checked })} /> Nghỉ hưởng lương{policyModal.typeCode === 'UNPAID' && <small className="field-help">Loại nghỉ UNPAID luôn là nghỉ không lương.</small>}</label><label className="check-field"><input type="checkbox" checked={policyModal.deductsBalance} onChange={(event) => setPolicyModal({ ...policyModal, deductsBalance: event.target.checked, daysPerYear: event.target.checked ? (policyModal.daysPerYear ?? 0) : null })} /> Trừ vào số dư phép</label><label className="check-field"><input type="checkbox" checked={policyModal.isActive} onChange={(event) => setPolicyModal({ ...policyModal, isActive: event.target.checked })} /> Chính sách đang hoạt động</label><div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setPolicyModal(null)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : 'Lưu chính sách'}</button></div></form></Modal>}
    {holidayModal && <Modal title={holidayForm.name ? 'Sửa ngày nghỉ lễ' : 'Thêm ngày nghỉ lễ'} description="Ngày lễ được loại trừ khi tính số ngày làm việc." onClose={() => setHolidayModal(null)}><form className="modal-form" onSubmit={saveHoliday}><div className="field-group"><label htmlFor="holiday-name">Tên ngày lễ *</label><input id="holiday-name" required maxLength={120} value={holidayForm.name} onChange={(event) => setHolidayForm({ ...holidayForm, name: event.target.value })} placeholder="Ví dụ: Ngày Giải phóng miền Nam" /></div><div className="field-group"><label htmlFor="holiday-date">Ngày *</label><input id="holiday-date" type="date" required value={holidayForm.date} onChange={(event) => setHolidayForm({ ...holidayForm, date: event.target.value })} /></div><div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setHolidayModal(null)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : 'Lưu ngày lễ'}</button></div></form></Modal>}
  </div>;
}
