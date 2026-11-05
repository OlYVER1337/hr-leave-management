import { useEffect, useState, type FormEvent } from 'react';
import { CalendarDays, CheckCircle2, Edit3, Plus, ShieldCheck, Trash2 } from 'lucide-react';
import { PageHeader, EmptyState, formatDate } from '../components/ui';
import { Modal } from '../components/Modal';
import { useToast } from '../components/ToastProvider';
import { getHolidays, getPolicies, restoreBuiltInLeaveType, saveHolidays, savePolicies } from '../services/leaveService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Holiday, LeavePolicy } from '../types';

const newHoliday = () => ({ id: '', name: '', date: '' });
export function PoliciesPage() {
  const currentYear = new Date().getFullYear();
  const policyYearOptions = Array.from({ length: 8 }, (_, index) => currentYear - 2 + index);
  const notify = useToast();
  const [tab, setTab] = useState<'policies' | 'holidays'>('policies');
  const [selectedYear, setSelectedYear] = useState(currentYear + 1);
  const canEditSelectedYear = selectedYear > currentYear;
  const [policies, setPoliciesState] = useState<LeavePolicy[]>([]);
  const [holidays, setHolidaysState] = useState<Holiday[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [policyModal, setPolicyModal] = useState<LeavePolicy | null>(null);
  const [holidayModal, setHolidayModal] = useState<Holiday | null>(null);
  const [holidayForm, setHolidayForm] = useState(newHoliday());
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError('');
    Promise.allSettled([getPolicies(selectedYear), getHolidays()]).then((results) => {
      if (!active) return;
      if (results[0].status === 'fulfilled') setPoliciesState(results[0].value); else setError(getApiErrorMessage(getRejectedReason(results[0])));
      if (results[1].status === 'fulfilled') setHolidaysState(results[1].value); else setError((value) => value || getApiErrorMessage(getRejectedReason(results[1])));
      setLoading(false);
    });
    return () => { active = false; };
  }, [selectedYear]);

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
  async function restorePolicy(policy: LeavePolicy) {
    setSaving(true);
    try {
      await restoreBuiltInLeaveType({ ...policy, year: selectedYear });
      setPoliciesState(await getPolicies());
      notify('Đã thêm lại chính sách nghỉ.');
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
    <PageHeader eyebrow="QUẢN TRỊ" title="Chính sách nghỉ phép" description="Quản lý ba loại nghỉ cố định, hạn mức phép và ngày nghỉ lễ của tổ chức." actions={tab === 'holidays' ? <button className="button button-primary" onClick={openCreateHoliday}><Plus size={17} /> Thêm ngày lễ</button> : undefined} />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    {tab === 'policies' && <div className="policy-year-toolbar" style={{ display: 'flex', alignItems: 'end', gap: 14, flexWrap: 'wrap' }}>
      <div className="field-group" style={{ width: 190 }}>
        <label htmlFor="policy-year">Năm áp dụng</label>
        <select id="policy-year" value={selectedYear} onChange={(event) => { setSelectedYear(Number(event.target.value)); setPolicyModal(null); }}>
          {policyYearOptions.map((year) => <option key={year} value={year}>{year}</option>)}
        </select>
      </div>
      <span style={{ color: canEditSelectedYear ? '#23845b' : '#9a6b22', fontSize: 11, paddingBottom: 10 }}>
        {canEditSelectedYear ? 'Chỉ có thể chỉnh sửa chính sách của năm tương lai.' : 'Chính sách năm hiện tại và các năm trước chỉ được xem.'}
      </span>
    </div>}
    <div className="policy-tabs"><button className={tab === 'policies' ? 'active' : ''} onClick={() => setTab('policies')}><ShieldCheck size={17} /> Loại nghỉ & hạn mức <span>{policies.length}</span></button><button className={tab === 'holidays' ? 'active' : ''} onClick={() => setTab('holidays')}><CalendarDays size={17} /> Ngày nghỉ lễ <span>{holidays.length}</span></button></div>
    {loading ? <div className="table-loading"><span /> Đang tải chính sách…</div> : tab === 'policies' ? <section className="policy-card-grid">{policies.map((policy) => <article className="panel policy-card" key={policy.id}><div className="policy-card-top"><span className={`policy-icon policy-${policy.typeCode.toLowerCase()}`}><ShieldCheck size={20} /></span><span className={`employee-status ${policy.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{policy.isActive ? 'Đang hoạt động' : 'Chưa được cấu hình'}</span></div><h2>{policy.typeName}</h2><p className="policy-description">{policy.description}</p><div className="policy-entitlement"><strong>{policy.daysPerYear === null ? 'Không giới hạn' : policy.daysPerYear}</strong><span>{policy.daysPerYear === null ? 'Không có hạn mức năm cố định' : 'ngày mỗi năm'}</span></div><div className="policy-properties"><span><i className={policy.paid ? 'property-yes' : 'property-no'} />{policy.paid ? 'Nghỉ hưởng lương' : 'Nghỉ không lương'}</span><span><i className={policy.deductsBalance ? 'property-yes' : 'property-no'} />{policy.deductsBalance ? 'Trừ vào số dư phép' : 'Không trừ số dư phép'}</span></div>{policy.isActive ? <button className="button button-secondary button-full" onClick={() => setPolicyModal({ ...policy, year: selectedYear })} disabled={saving || !policy.leaveTypeId || !canEditSelectedYear} title={!canEditSelectedYear ? 'Chỉ được sửa chính sách của năm tương lai.' : undefined}>{canEditSelectedYear ? <><Edit3 size={15} /> Sửa chính sách</> : 'Chính sách đã khóa'}</button> : <button className="button button-primary button-full" aria-label={canEditSelectedYear ? `Thêm lại chính sách ${policy.typeName}` : `Chính sách ${policy.typeName} đã khóa`} onClick={() => void restorePolicy({ ...policy, year: selectedYear })} disabled={saving || !canEditSelectedYear}><Plus size={15} /> {canEditSelectedYear ? 'Thêm lại chính sách' : 'Chính sách đã khóa'}</button>}</article>)}</section> : <section className="panel holiday-panel"><div className="panel-header"><div><h2>Lịch nghỉ lễ</h2><p>Ngày lễ không được tính vào số ngày làm việc khi tạo đơn nghỉ.</p></div><span className="holiday-count"><CalendarDays size={15} /> {holidays.length} ngày</span></div>{holidays.length === 0 ? <EmptyState title="Chưa cấu hình ngày lễ" description="Thêm ngày nghỉ lễ cho năm sắp tới." /> : <div className="table-scroll"><table className="data-table"><thead><tr><th>Ngày lễ</th><th>Ngày</th><th>Thứ</th><th /></tr></thead><tbody>{[...holidays].sort((a, b) => a.date.localeCompare(b.date)).map((holiday) => <tr key={holiday.id}><td><div className="holiday-name-cell"><span><CalendarDays size={17} /></span><strong>{holiday.name}</strong></div></td><td>{formatDate(holiday.date)}</td><td>{new Intl.DateTimeFormat('vi-VN', { weekday: 'long' }).format(new Date(`${holiday.date}T12:00:00`))}</td><td><div className="row-actions"><button className="icon-button" aria-label={`Sửa ${holiday.name}`} onClick={() => openEditHoliday(holiday)}><Edit3 size={16} /></button><button className="icon-button danger-icon" aria-label={`Xóa ${holiday.name}`} onClick={() => void deleteHoliday(holiday)}><Trash2 size={16} /></button></div></td></tr>)}</tbody></table></div>}</section>}
    <div className="policy-reminder"><CheckCircle2 size={18} /><p><strong>Chính sách cần được theo dõi.</strong> Ở chế độ dùng thử, dữ liệu được lưu cục bộ. Khi sử dụng thật, thay đổi sẽ được lưu qua HR Service và làm mới bộ nhớ đệm liên quan.</p></div>

    {policyModal && <Modal title={`Sửa hạn mức ${policyModal.typeName}`} description={`Cập nhật số ngày nghỉ cho năm ${selectedYear}.`} onClose={() => setPolicyModal(null)}><form className="modal-form" onSubmit={updatePolicy}>{policyModal.deductsBalance ? <div className="field-group"><label htmlFor="policy-days">Số ngày mỗi năm</label><input id="policy-days" type="number" min={0} max={366} required value={policyModal.daysPerYear ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, daysPerYear: event.target.value === '' ? null : Number(event.target.value) })} /></div> : <p>Loại nghỉ này không áp dụng hạn mức ngày phép năm.</p>}<div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setPolicyModal(null)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : 'Lưu chính sách'}</button></div></form></Modal>}
    {holidayModal && <Modal title={holidayForm.name ? 'Sửa ngày nghỉ lễ' : 'Thêm ngày nghỉ lễ'} description="Ngày lễ được loại trừ khi tính số ngày làm việc." onClose={() => setHolidayModal(null)}><form className="modal-form" onSubmit={saveHoliday}><div className="field-group"><label htmlFor="holiday-name">Tên ngày lễ *</label><input id="holiday-name" required maxLength={120} value={holidayForm.name} onChange={(event) => setHolidayForm({ ...holidayForm, name: event.target.value })} placeholder="Ví dụ: Ngày Giải phóng miền Nam" /></div><div className="field-group"><label htmlFor="holiday-date">Ngày *</label><input id="holiday-date" type="date" required value={holidayForm.date} onChange={(event) => setHolidayForm({ ...holidayForm, date: event.target.value })} /></div><div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setHolidayModal(null)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : 'Lưu ngày lễ'}</button></div></form></Modal>}
  </div>;
}
