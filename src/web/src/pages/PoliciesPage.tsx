import { useEffect, useState, type FormEvent } from 'react';
import { CalendarDays, CheckCircle2, Edit3, Plus, ShieldCheck, Trash2 } from 'lucide-react';
import { PageHeader, EmptyState, formatDate } from '../components/ui';
import { Modal } from '../components/Modal';
import { useToast } from '../components/ToastProvider';
import { getHolidays, getPolicies, getPolicyVersions, restoreBuiltInLeaveType, saveHolidays, savePolicies } from '../services/leaveService';
import { DEMO_MODE } from '../services/config';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Holiday, LeavePolicy } from '../types';

const newHoliday = () => ({ id: '', name: '', date: '' });

function getNextPolicyEffectiveDate(
  policy: LeavePolicy,
  currentBusinessDate: string,
  policyYear: number,
): string | null {
  const firstDayOfPolicyYear = `${policyYear}-01-01`;
  const earliestAllowedDate = currentBusinessDate > firstDayOfPolicyYear
    ? currentBusinessDate
    : firstDayOfPolicyYear;
  let candidate = earliestAllowedDate;

  if (policy.effectiveFrom && policy.effectiveFrom >= earliestAllowedDate) {
    const nextDate = new Date(`${policy.effectiveFrom}T00:00:00Z`);
    nextDate.setUTCDate(nextDate.getUTCDate() + 1);
    candidate = nextDate.toISOString().slice(0, 10);
  }

  return candidate <= `${policyYear}-12-31` ? candidate : null;
}

export function PoliciesPage() {
  const currentBusinessDate = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).format(new Date());
  const currentYear = Number(currentBusinessDate.slice(0, 4));
  const policyYearOptions = Array.from({ length: 8 }, (_, index) => currentYear + index);
  const notify = useToast();
  const [tab, setTab] = useState<'policies' | 'holidays'>('policies');
  const [selectedVersion, setSelectedVersion] = useState('CURRENT');
  const [policies, setPoliciesState] = useState<LeavePolicy[]>([]);
  const [policyVersions, setPolicyVersions] = useState<LeavePolicy[]>([]);
  const [holidays, setHolidaysState] = useState<Holiday[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [policyModal, setPolicyModal] = useState<LeavePolicy | null>(null);
  const [holidayModal, setHolidayModal] = useState<Holiday | null>(null);
  const [holidayForm, setHolidayForm] = useState(newHoliday());
  const [saving, setSaving] = useState(false);
  const [loadingPolicyYear, setLoadingPolicyYear] = useState(false);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError('');
    Promise.allSettled([getPolicies(currentYear), getPolicyVersions(currentYear), getHolidays()]).then((results) => {
      if (!active) return;
      if (results[0].status === 'fulfilled') setPoliciesState(results[0].value); else setError(getApiErrorMessage(getRejectedReason(results[0])));
      if (results[1].status === 'fulfilled') setPolicyVersions(results[1].value); else setError((value) => value || getApiErrorMessage(getRejectedReason(results[1])));
      if (results[2].status === 'fulfilled') setHolidaysState(results[2].value); else setError((value) => value || getApiErrorMessage(getRejectedReason(results[2])));
      setLoading(false);
    });
    return () => { active = false; };
  }, [currentYear]);

  async function openCreatePolicy(policy: LeavePolicy) {
    setLoadingPolicyYear(true);
    try {
      const latestPolicies = await getPolicies(currentYear, `${currentYear}-12-31`);
      let targetYear = currentYear;
      let latestPolicy = latestPolicies.find((item) => item.typeCode === policy.typeCode) ?? policy;
      let effectiveFrom = getNextPolicyEffectiveDate(
        latestPolicy,
        currentBusinessDate,
        targetYear,
      );

      if (!effectiveFrom) {
        targetYear = currentYear + 1;
        const nextYearPolicies = await getPolicies(targetYear, `${targetYear}-12-31`);
        latestPolicy = nextYearPolicies.find((item) => item.typeCode === policy.typeCode) ?? policy;
        effectiveFrom = getNextPolicyEffectiveDate(
          latestPolicy,
          currentBusinessDate,
          targetYear,
        );
      }

      if (!effectiveFrom) {
        notify('Không còn ngày hiệu lực hợp lệ trong năm được chọn.', 'error');
        return;
      }
      setPolicyModal({
        ...latestPolicy,
        year: targetYear,
        effectiveFrom,
        reason: '',
      });
    } catch (reason) {
      notify(getApiErrorMessage(reason), 'error');
    } finally {
      setLoadingPolicyYear(false);
    }
  }

  async function changePolicyYear(year: number) {
    if (!policyModal) return;
    const currentPolicy = policyModal;
    setPolicyModal({ ...currentPolicy, year, effectiveFrom: '' });
    setLoadingPolicyYear(true);
    try {
      const yearPolicies = await getPolicies(year, `${year}-12-31`);
      const targetPolicy = yearPolicies.find((item) => item.typeCode === currentPolicy.typeCode);
      if (!targetPolicy) {
        throw new Error('Không tìm thấy chính sách cho năm được chọn.');
      }
      const effectiveFrom = getNextPolicyEffectiveDate(
        targetPolicy,
        currentBusinessDate,
        year,
      );
      setPolicyModal({
        ...targetPolicy,
        year,
        effectiveFrom: effectiveFrom ?? '',
        reason: '',
      });
      if (!effectiveFrom) {
        notify('Không còn ngày hiệu lực hợp lệ trong năm được chọn.', 'error');
      }
    } catch (reason) {
      notify(getApiErrorMessage(reason), 'error');
    } finally {
      setLoadingPolicyYear(false);
    }
  }

  async function updatePolicy(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!policyModal) return; setSaving(true);
    try {
      await savePolicies([policyModal]);
      const [currentPolicies, versions] = await Promise.all([
        getPolicies(currentYear),
        getPolicyVersions(currentYear),
      ]);
      setPoliciesState(currentPolicies);
      setPolicyVersions(versions);
      setPolicyModal(null);
      notify('Đã lưu chính sách nghỉ phép.');
    }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
    finally { setSaving(false); }
  }
  async function restorePolicy(policy: LeavePolicy) {
    setSaving(true);
    try {
      await restoreBuiltInLeaveType({ ...policy, year: currentYear });
      const [currentPolicies, versions] = await Promise.all([
        getPolicies(currentYear),
        getPolicyVersions(currentYear),
      ]);
      setPoliciesState(currentPolicies);
      setPolicyVersions(versions);
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

  const versionNumbers = [...new Set(policyVersions
    .map((policy) => policy.versionNumber)
    .filter((value): value is number => value !== undefined))]
    .sort((left, right) => left - right);

  const displayedPolicies = selectedVersion === 'CURRENT'
    ? policies
    : selectedVersion === 'ALL'
      ? [
        ...policyVersions,
        ...policies
          .filter((policy) =>
            !policy.deductsBalance ||
            !policyVersions.some((version) => version.typeCode === policy.typeCode))
          .map((policy) => policy.deductsBalance
            ? { ...policy, policyId: undefined, effectiveFrom: undefined, reason: undefined, versionMissing: true }
            : policy),
      ]
      : policies.map((currentPolicy) => {
        if (!currentPolicy.deductsBalance) return currentPolicy;
        const requestedVersion = Number(selectedVersion);
        return policyVersions.find((version) =>
          version.typeCode === currentPolicy.typeCode &&
          version.versionNumber === requestedVersion)
          ?? {
            ...currentPolicy,
            id: `${currentPolicy.id}-missing-version-${requestedVersion}`,
            policyId: undefined,
            effectiveFrom: undefined,
            reason: undefined,
            versionNumber: requestedVersion,
            versionMissing: true,
          };
      });

  return <div className="page-stack">
    <PageHeader eyebrow="QUẢN TRỊ" title="Chính sách nghỉ phép" description="Quản lý ba loại nghỉ cố định, hạn mức phép và ngày nghỉ lễ của tổ chức." actions={tab === 'holidays' ? <button className="button button-primary" onClick={openCreateHoliday}><Plus size={17} /> Thêm ngày lễ</button> : undefined} />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    {tab === 'policies' && <div className="policy-year-toolbar" style={{ display: 'flex', alignItems: 'end', gap: 14, flexWrap: 'wrap' }}>
      <div className="field-group" style={{ width: 240 }}>
        <label htmlFor="policy-version-filter">Lọc theo phiên bản</label>
        <select id="policy-version-filter" value={selectedVersion} onChange={(event) => setSelectedVersion(event.target.value)}>
          <option value="CURRENT">Đang có hiệu lực</option>
          <option value="ALL">Tất cả phiên bản</option>
          {versionNumbers.map((version) => <option key={version} value={String(version)}>Version {version}</option>)}
        </select>
      </div>
      <span style={{ color: '#23845b', fontSize: 11, paddingBottom: 10 }}>
        Đang hiển thị phiên bản có hiệu lực trong năm {currentYear}. Năm áp dụng của phiên bản mới được chọn trong biểu mẫu.
      </span>
    </div>}
    <div className="policy-tabs"><button className={tab === 'policies' ? 'active' : ''} onClick={() => setTab('policies')}><ShieldCheck size={17} /> Loại nghỉ & hạn mức <span>{policies.length}</span></button><button className={tab === 'holidays' ? 'active' : ''} onClick={() => setTab('holidays')}><CalendarDays size={17} /> Ngày nghỉ lễ <span>{holidays.length}</span></button></div>
    {loading ? <div className="table-loading"><span /> Đang tải chính sách…</div> : tab === 'policies' ? <section className="policy-card-grid">{displayedPolicies.map((policy) => <article className="panel policy-card" key={policy.id}><div className="policy-card-top"><span className={`policy-icon policy-${policy.typeCode.toLowerCase()}`}><ShieldCheck size={20} /></span><span className={`employee-status ${policy.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{!policy.isActive ? 'Loại nghỉ tạm khóa' : policy.versionMissing ? (policy.versionNumber ? `Không có Version ${policy.versionNumber}` : 'Chưa có phiên bản') : !policy.deductsBalance ? 'Không áp dụng version' : policy.policyId ? 'Đang cấu hình' : 'Chưa cấu hình hạn mức năm'}</span></div><h2>{policy.typeName}{policy.versionNumber ? ` · Version ${policy.versionNumber}` : ''}</h2><p className="policy-description">{policy.description}</p><div className="policy-entitlement"><strong>{policy.versionMissing ? '—' : policy.daysPerYear === null ? 'Không giới hạn' : policy.daysPerYear}</strong><span>{policy.versionMissing ? 'Không có hạn mức ở phiên bản này' : policy.daysPerYear === null ? 'Không có hạn mức năm cố định' : 'ngày mỗi năm'}</span></div>{policy.effectiveFrom && <p className="policy-description">Hiệu lực từ {formatDate(policy.effectiveFrom)}</p>}{policy.reason && <p className="policy-description">Lý do: {policy.reason}</p>}<div className="policy-properties"><span><i className={policy.paid ? 'property-yes' : 'property-no'} />{policy.paid ? 'Nghỉ hưởng lương' : 'Nghỉ không lương'}</span><span><i className={policy.deductsBalance ? 'property-yes' : 'property-no'} />{policy.deductsBalance ? 'Trừ vào số dư phép' : 'Không trừ số dư phép'}</span></div>{policy.isActive ? (policy.deductsBalance ? <button className="button button-secondary button-full" onClick={() => void openCreatePolicy(policy)} disabled={saving || loadingPolicyYear || !policy.leaveTypeId} title="Tạo phiên bản cho năm hiện tại hoặc tương lai.">{policy.policyId ? <Edit3 size={15} /> : <Plus size={15} />}{policy.policyId ? ' Tạo phiên bản mới' : ' Thiết lập hạn mức'}</button> : <p className="policy-description">Loại nghỉ này không áp dụng quota.</p>) : <button className="button button-primary button-full" aria-label={`Thêm lại chính sách ${policy.typeName}`} onClick={() => void restorePolicy(policy)} disabled={saving}><Plus size={15} /> Thêm lại chính sách</button>}</article>)}</section> : <section className="panel holiday-panel"><div className="panel-header"><div><h2>Lịch nghỉ lễ</h2><p>Ngày lễ không được tính vào số ngày làm việc khi tạo đơn nghỉ.</p></div><span className="holiday-count"><CalendarDays size={15} /> {holidays.length} ngày</span></div>{holidays.length === 0 ? <EmptyState title="Chưa cấu hình ngày lễ" description="Thêm ngày nghỉ lễ cho năm sắp tới." /> : <div className="table-scroll"><table className="data-table"><thead><tr><th>Ngày lễ</th><th>Ngày</th><th>Thứ</th><th /></tr></thead><tbody>{[...holidays].sort((a, b) => a.date.localeCompare(b.date)).map((holiday) => <tr key={holiday.id}><td><div className="holiday-name-cell"><span><CalendarDays size={17} /></span><strong>{holiday.name}</strong></div></td><td>{formatDate(holiday.date)}</td><td>{new Intl.DateTimeFormat('vi-VN', { weekday: 'long' }).format(new Date(`${holiday.date}T12:00:00`))}</td><td><div className="row-actions"><button className="icon-button" aria-label={`Sửa ${holiday.name}`} onClick={() => openEditHoliday(holiday)}><Edit3 size={16} /></button><button className="icon-button danger-icon" aria-label={`Xóa ${holiday.name}`} onClick={() => void deleteHoliday(holiday)}><Trash2 size={16} /></button></div></td></tr>)}</tbody></table></div>}</section>}
    <div className="policy-reminder"><CheckCircle2 size={18} /><p><strong>Chính sách cần được theo dõi.</strong> {DEMO_MODE ? 'Ứng dụng đang ở chế độ demo; thay đổi được lưu cục bộ trong trình duyệt.' : 'HR Service là nguồn dữ liệu chính sách; mỗi lần lưu tạo một phiên bản mới, giữ nguyên phiên bản cũ và làm mới bộ nhớ đệm liên quan.'}</p></div>

    {policyModal && <Modal title={`Tạo phiên bản ${policyModal.typeName}`} description={`Phiên bản mới cho năm ${policyModal.year}; phiên bản trước vẫn được lưu trong lịch sử.`} onClose={() => setPolicyModal(null)}><form className="modal-form" onSubmit={updatePolicy}>{policyModal.deductsBalance ? <><div className="field-group"><label htmlFor="policy-year">Năm áp dụng</label><select id="policy-year" value={policyModal.year ?? currentYear} onChange={(event) => void changePolicyYear(Number(event.target.value))} disabled={saving || loadingPolicyYear}>{policyYearOptions.map((year) => <option key={year} value={year}>{year}</option>)}</select></div><div className="field-group"><label htmlFor="policy-days">Số ngày mỗi năm</label><input id="policy-days" type="number" min={0} max={366} step="0.1" required value={policyModal.daysPerYear ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, daysPerYear: event.target.value === '' ? null : Number(event.target.value) })} /></div><div className="field-group"><label htmlFor="policy-effective-from">Ngày hiệu lực *</label><input id="policy-effective-from" type="date" min={policyModal.year === currentYear ? currentBusinessDate : `${policyModal.year}-01-01`} max={`${policyModal.year}-12-31`} required value={policyModal.effectiveFrom ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, effectiveFrom: event.target.value })} disabled={saving || loadingPolicyYear} /></div><div className="field-group"><label htmlFor="policy-reason">Lý do thay đổi</label><input id="policy-reason" type="text" maxLength={500} value={policyModal.reason ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, reason: event.target.value })} placeholder="Ví dụ: Điều chỉnh chính sách nghỉ phép" /></div></> : <p>Loại nghỉ này không áp dụng hạn mức ngày phép năm.</p>}<div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setPolicyModal(null)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving || loadingPolicyYear || !policyModal.effectiveFrom}>{saving || loadingPolicyYear ? 'Đang xử lý…' : 'Lưu chính sách'}</button></div></form></Modal>}
    {holidayModal && <Modal title={holidayForm.name ? 'Sửa ngày nghỉ lễ' : 'Thêm ngày nghỉ lễ'} description="Ngày lễ được loại trừ khi tính số ngày làm việc." onClose={() => setHolidayModal(null)}><form className="modal-form" onSubmit={saveHoliday}><div className="field-group"><label htmlFor="holiday-name">Tên ngày lễ *</label><input id="holiday-name" required maxLength={120} value={holidayForm.name} onChange={(event) => setHolidayForm({ ...holidayForm, name: event.target.value })} placeholder="Ví dụ: Ngày Giải phóng miền Nam" /></div><div className="field-group"><label htmlFor="holiday-date">Ngày *</label><input id="holiday-date" type="date" required value={holidayForm.date} onChange={(event) => setHolidayForm({ ...holidayForm, date: event.target.value })} /></div><div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setHolidayModal(null)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : 'Lưu ngày lễ'}</button></div></form></Modal>}
  </div>;
}
