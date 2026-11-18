import { useEffect, useRef, useState, type FormEvent } from 'react';
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

function getMinimumPolicyEffectiveDate(
  previousVersion: LeavePolicy | null,
  currentBusinessDate: string,
  policyYear: number,
): string {
  const firstDayOfPolicyYear = `${policyYear}-01-01`;
  const earliestAllowedDate = currentBusinessDate > firstDayOfPolicyYear
    ? currentBusinessDate
    : firstDayOfPolicyYear;
  if (!previousVersion?.effectiveFrom || previousVersion.effectiveFrom < earliestAllowedDate) {
    return earliestAllowedDate;
  }

  const nextDate = new Date(`${previousVersion.effectiveFrom}T00:00:00Z`);
  nextDate.setUTCDate(nextDate.getUTCDate() + 1);
  return nextDate.toISOString().slice(0, 10);
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
  const [viewMode, setViewMode] = useState<'CURRENT' | 'HISTORY'>('CURRENT');
  const [historyTypeCode, setHistoryTypeCode] = useState<string | null>(null);
  const [policyBaseline, setPolicyBaseline] = useState<LeavePolicy | null>(null);
  const savePolicyInFlight = useRef(false);
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
    Promise.allSettled([getPolicies(currentYear), getPolicyVersions(), getHolidays()]).then((results) => {
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
      const latestPolicies = await getPolicies(currentYear, `${currentYear}-12-31`, true);
      let targetYear = currentYear;
      let latestPolicy = latestPolicies.find((item) => item.typeCode === policy.typeCode) ?? policy;
      let effectiveFrom = getNextPolicyEffectiveDate(
        latestPolicy,
        currentBusinessDate,
        targetYear,
      );

      if (!effectiveFrom) {
        targetYear = currentYear + 1;
        const nextYearPolicies = await getPolicies(targetYear, `${targetYear}-12-31`, true);
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
      setPolicyBaseline(latestPolicy.policyId && latestPolicy.effectiveFrom ? latestPolicy : null);
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
      const yearPolicies = await getPolicies(year, `${year}-12-31`, true);
      const targetPolicy = yearPolicies.find((item) => item.typeCode === currentPolicy.typeCode);
      if (!targetPolicy) {
        throw new Error('Không tìm thấy chính sách cho năm được chọn.');
      }
      const effectiveFrom = getNextPolicyEffectiveDate(
        targetPolicy,
        currentBusinessDate,
        year,
      );
      setPolicyBaseline(targetPolicy.policyId && targetPolicy.effectiveFrom ? targetPolicy : null);
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
    event.preventDefault();
    if (!policyModal || savePolicyInFlight.current) return;
    savePolicyInFlight.current = true;
    setSaving(true);
    try {
      await savePolicies([policyModal]);
      setPolicyModal(null);
      setPolicyBaseline(null);
      notify('Đã lưu chính sách nghỉ phép.');

      try {
        const [currentPolicies, versions] = await Promise.all([
          getPolicies(currentYear),
          getPolicyVersions(),
        ]);
        setPoliciesState(currentPolicies);
        setPolicyVersions(versions);
      } catch (refreshError) {
        notify(
          `Đã lưu chính sách nhưng chưa thể tải lại danh sách. ${getApiErrorMessage(refreshError)}`,
          'error',
        );
      }
    } catch (reason) {
      notify(getApiErrorMessage(reason), 'error');
    } finally {
      savePolicyInFlight.current = false;
      setSaving(false);
    }
  }
  async function restorePolicy(policy: LeavePolicy) {
    setSaving(true);
    try {
      await restoreBuiltInLeaveType({ ...policy, year: currentYear });
      const [currentPolicies, versions] = await Promise.all([
        getPolicies(currentYear),
        getPolicyVersions(),
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

  const historyPolicy = historyTypeCode
    ? policies.find((policy) => policy.typeCode === historyTypeCode) ?? null
    : null;
  const historyVersions = historyTypeCode
    ? policyVersions.filter((version) => version.typeCode === historyTypeCode)
    : [];
  const sortVersionsByYearAndDate = (left: LeavePolicy, right: LeavePolicy) =>
    (right.year ?? 0) - (left.year ?? 0) ||
    (right.effectiveFrom ?? '').localeCompare(left.effectiveFrom ?? '');

  return <div className="page-stack">
    <PageHeader eyebrow="QUẢN TRỊ" title="Chính sách nghỉ phép" description="Quản lý ba loại nghỉ cố định, hạn mức phép và ngày nghỉ lễ của tổ chức." actions={tab === 'holidays' ? <button className="button button-primary" onClick={openCreateHoliday}><Plus size={17} /> Thêm ngày lễ</button> : undefined} />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    {tab === 'policies' && <div className="policy-year-toolbar" style={{ display: 'flex', alignItems: 'end', gap: 14, flexWrap: 'wrap' }}>
      <div className="field-group" style={{ width: 240 }}>
        <label htmlFor="policy-view-filter">Hiển thị chính sách</label>
        <select id="policy-view-filter" value={viewMode} onChange={(event) => setViewMode(event.target.value as 'CURRENT' | 'HISTORY')}>
          <option value="CURRENT">Đang có hiệu lực</option>
          <option value="HISTORY">Tất cả lịch sử</option>
        </select>
      </div>
      <span style={{ color: '#64748b', fontSize: 11, paddingBottom: 10 }}>
        {viewMode === 'CURRENT'
          ? `Chính sách đang áp dụng trong năm ${currentYear}.`
          : `Lịch sử phiên bản của từng loại nghỉ trong năm ${currentYear}; các phiên bản chỉ đọc.`}
      </span>
    </div>}
    <div className="policy-tabs"><button className={tab === 'policies' ? 'active' : ''} onClick={() => setTab('policies')}><ShieldCheck size={17} /> Loại nghỉ & hạn mức <span>{policies.length}</span></button><button className={tab === 'holidays' ? 'active' : ''} onClick={() => setTab('holidays')}><CalendarDays size={17} /> Ngày nghỉ lễ <span>{holidays.length}</span></button></div>
    {loading ? <div className="table-loading"><span /> Đang tải chính sách…</div> : tab === 'policies' ? viewMode === 'CURRENT' ? <section className="policy-card-grid">{policies.map((policy) => <article className="panel policy-card" key={policy.id}><div className="policy-card-top"><span className={`policy-icon policy-${policy.typeCode.toLowerCase()}`}><ShieldCheck size={20} /></span><span className={`employee-status ${policy.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{!policy.isActive ? 'Loại nghỉ tạm khóa' : policy.deductsBalance && !policy.policyId ? 'Chưa cấu hình hạn mức năm' : 'Đang áp dụng'}</span></div><h2>{policy.typeName}</h2><p className="policy-description">{policy.description}</p><div className="policy-entitlement"><strong>{policy.deductsBalance && policy.daysPerYear === null ? '—' : policy.daysPerYear === null ? 'Không giới hạn' : policy.daysPerYear}</strong><span>{policy.deductsBalance && policy.daysPerYear === null ? 'Chưa cấu hình hạn mức năm' : policy.daysPerYear === null ? 'Không có hạn mức năm cố định' : 'ngày mỗi năm'}</span></div>{policy.effectiveFrom && <p className="policy-description">Hiệu lực từ {formatDate(policy.effectiveFrom)}</p>}{policy.reason && <p className="policy-description">Lý do: {policy.reason}</p>}<div className="policy-properties"><span><i className={policy.paid ? 'property-yes' : 'property-no'} />{policy.paid ? 'Nghỉ hưởng lương' : 'Nghỉ không lương'}</span><span><i className={policy.deductsBalance ? 'property-yes' : 'property-no'} />{policy.deductsBalance ? 'Trừ vào số dư phép' : 'Không trừ số dư phép'}</span></div>{policy.deductsBalance && <div className="policy-actions">{policy.isActive && <button className="button button-secondary button-full" onClick={() => void openCreatePolicy(policy)} disabled={saving || loadingPolicyYear || !policy.leaveTypeId}><Edit3 size={15} /> {policy.policyId ? 'Tạo phiên bản mới' : 'Thiết lập hạn mức'}</button>}<button className="button button-ghost button-full" onClick={() => setHistoryTypeCode(policy.typeCode)}>{policyVersions.filter((version) => version.typeCode === policy.typeCode).length} phiên bản · Xem lịch sử</button></div>}{!policy.isActive && <button className="button button-primary button-full" aria-label={`Thêm lại chính sách ${policy.typeName}`} onClick={() => void restorePolicy(policy)} disabled={saving}><Plus size={15} /> Thêm lại chính sách</button>}{!policy.deductsBalance && <p className="policy-description">Loại nghỉ này không quản lý version quota.</p>}</article>)}</section> : <section className="policy-history-groups">{policies.map((policy) => {
        const versions = policyVersions.filter((version) => version.typeCode === policy.typeCode);
        return <section className="panel policy-history-group" key={policy.typeCode}>
          <div className="panel-header">
            <div><h2>{policy.typeName}</h2><p>{policy.description}</p></div>
            <button className="button button-secondary" onClick={() => setHistoryTypeCode(policy.typeCode)}>Xem lịch sử ({versions.length})</button>
          </div>
          {policy.deductsBalance
            ? versions.length
              ? <div className="policy-card-grid">{[...versions].sort(sortVersionsByYearAndDate).map((version) => <article className="policy-version-card" key={version.policyId}>
                  <div className="policy-card-top"><strong>Năm {version.year} · Version {version.versionNumber}</strong><span className="employee-status">{version.year === currentYear && version.policyId === policy.policyId ? 'Đang áp dụng' : version.effectiveFrom && version.effectiveFrom > currentBusinessDate ? 'Đã lên lịch' : 'Phiên bản trước'}</span></div>
                  <div className="policy-entitlement"><strong>{version.daysPerYear}</strong><span>ngày mỗi năm</span></div>
                  <p className="policy-description">Hiệu lực từ {version.effectiveFrom ? formatDate(version.effectiveFrom) : 'Chưa xác định'}</p>
                  {version.reason && <p className="policy-description">Lý do: {version.reason}</p>}
                </article>)}</div>
              : <EmptyState title="Chưa có phiên bản" description="Loại nghỉ này chưa có lịch sử chính sách." />
            : <p className="policy-description">UNPAID không áp dụng quota và không có version.</p>}
        </section>;
      })}</section> : <section className="panel holiday-panel"><div className="panel-header"><div><h2>Lịch nghỉ lễ</h2><p>Ngày lễ không được tính vào số ngày làm việc khi tạo đơn nghỉ.</p></div><span className="holiday-count"><CalendarDays size={15} /> {holidays.length} ngày</span></div>{holidays.length === 0 ? <EmptyState title="Chưa cấu hình ngày lễ" description="Thêm ngày nghỉ lễ cho năm sắp tới." /> : <div className="table-scroll"><table className="data-table"><thead><tr><th>Ngày lễ</th><th>Ngày</th><th>Thứ</th><th /></tr></thead><tbody>{[...holidays].sort((a, b) => a.date.localeCompare(b.date)).map((holiday) => <tr key={holiday.id}><td><div className="holiday-name-cell"><span><CalendarDays size={17} /></span><strong>{holiday.name}</strong></div></td><td>{formatDate(holiday.date)}</td><td>{new Intl.DateTimeFormat('vi-VN', { weekday: 'long' }).format(new Date(`${holiday.date}T12:00:00`))}</td><td><div className="row-actions"><button className="icon-button" aria-label={`Sửa ${holiday.name}`} onClick={() => openEditHoliday(holiday)}><Edit3 size={16} /></button><button className="icon-button danger-icon" aria-label={`Xóa ${holiday.name}`} onClick={() => void deleteHoliday(holiday)}><Trash2 size={16} /></button></div></td></tr>)}</tbody></table></div>}</section>}
    <div className="policy-reminder"><CheckCircle2 size={18} /><p><strong>Chính sách cần được theo dõi.</strong> {DEMO_MODE ? 'Ứng dụng đang ở chế độ demo; thay đổi được lưu cục bộ trong trình duyệt.' : 'HR Service là nguồn dữ liệu chính sách; mỗi lần lưu tạo một phiên bản mới, giữ nguyên phiên bản cũ và làm mới bộ nhớ đệm liên quan.'}</p></div>

    {historyPolicy && <Modal title={`Lịch sử chính sách · ${historyPolicy.typeName}`} description="Các phiên bản được sắp xếp theo năm và ngày hiệu lực mới nhất trước." onClose={() => setHistoryTypeCode(null)}><div className="policy-version-list">{historyVersions.length ? [...historyVersions].sort(sortVersionsByYearAndDate).map((version) => <article className="policy-version-row" key={version.policyId}><div><div className="policy-card-top"><strong>Năm {version.year} · Version {version.versionNumber}</strong><span className="employee-status">{version.year === currentYear && version.policyId === historyPolicy.policyId ? 'Đang áp dụng' : version.effectiveFrom && version.effectiveFrom > currentBusinessDate ? 'Đã lên lịch' : 'Phiên bản trước'}</span></div><div className="policy-entitlement"><strong>{version.daysPerYear}</strong><span>ngày mỗi năm</span></div><p className="policy-description">Hiệu lực từ {version.effectiveFrom ? formatDate(version.effectiveFrom) : 'Chưa xác định'}</p>{version.reason && <p className="policy-description">Lý do: {version.reason}</p>}</div></article>) : <EmptyState title="Chưa có lịch sử phiên bản" description="Chính sách này chưa có version nào." />}</div></Modal>}

    {policyModal && <Modal title={`Tạo phiên bản ${policyModal.typeName}`} description={`Phiên bản mới cho năm ${policyModal.year}; phiên bản trước vẫn được lưu trong lịch sử.`} onClose={() => { if (!saving && !loadingPolicyYear) { setPolicyModal(null); setPolicyBaseline(null); } }}><form className="modal-form" onSubmit={updatePolicy}>{policyModal.deductsBalance ? <>{policyBaseline && <div className="policy-baseline"><strong>Phiên bản gần nhất trong năm {policyModal.year}</strong><div>{policyBaseline.daysPerYear} ngày/năm</div><p className="policy-description">Hiệu lực từ {policyBaseline.effectiveFrom ? formatDate(policyBaseline.effectiveFrom) : 'Chưa xác định'}</p></div>}<div className="field-group"><label htmlFor="policy-year">Năm áp dụng</label><select id="policy-year" value={policyModal.year ?? currentYear} onChange={(event) => void changePolicyYear(Number(event.target.value))} disabled={saving || loadingPolicyYear}>{policyYearOptions.map((year) => <option key={year} value={year}>{year}</option>)}</select></div><div className="field-group"><label htmlFor="policy-days">Số ngày mỗi năm</label><input id="policy-days" type="number" min={0} max={366} step="0.1" required value={policyModal.daysPerYear ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, daysPerYear: event.target.value === '' ? null : Number(event.target.value) })} /></div><div className="field-group"><label htmlFor="policy-effective-from">Ngày hiệu lực *</label><input id="policy-effective-from" type="date" lang="vi-VN" min={getMinimumPolicyEffectiveDate(policyBaseline, currentBusinessDate, policyModal.year ?? currentYear)} max={`${policyModal.year}-12-31`} required value={policyModal.effectiveFrom ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, effectiveFrom: event.target.value })} disabled={saving || loadingPolicyYear || !policyModal.effectiveFrom} /><p className="field-hint">Ngày đã chọn: {policyModal.effectiveFrom ? formatDate(policyModal.effectiveFrom) : 'Chưa chọn'} · Ngày mới phải sau phiên bản trước và không hồi tố.</p></div><div className="field-group"><label htmlFor="policy-reason">Lý do thay đổi</label><input id="policy-reason" type="text" maxLength={500} value={policyModal.reason ?? ''} onChange={(event) => setPolicyModal({ ...policyModal, reason: event.target.value })} placeholder="Ví dụ: Điều chỉnh chính sách nghỉ phép" /></div></> : <p>Loại nghỉ này không áp dụng hạn mức ngày phép năm.</p>}<div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => { setPolicyModal(null); setPolicyBaseline(null); }} disabled={saving || loadingPolicyYear}>Hủy</button><button className="button button-primary" type="submit" disabled={saving || loadingPolicyYear || !policyModal.effectiveFrom}>{saving || loadingPolicyYear ? 'Đang lưu…' : 'Lưu chính sách'}</button></div></form></Modal>}
    {holidayModal && <Modal title={holidayForm.name ? 'Sửa ngày nghỉ lễ' : 'Thêm ngày nghỉ lễ'} description="Ngày lễ được loại trừ khi tính số ngày làm việc." onClose={() => setHolidayModal(null)}><form className="modal-form" onSubmit={saveHoliday}><div className="field-group"><label htmlFor="holiday-name">Tên ngày lễ *</label><input id="holiday-name" required maxLength={120} value={holidayForm.name} onChange={(event) => setHolidayForm({ ...holidayForm, name: event.target.value })} placeholder="Ví dụ: Ngày Giải phóng miền Nam" /></div><div className="field-group"><label htmlFor="holiday-date">Ngày *</label><input id="holiday-date" type="date" required value={holidayForm.date} onChange={(event) => setHolidayForm({ ...holidayForm, date: event.target.value })} /></div><div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setHolidayModal(null)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : 'Lưu ngày lễ'}</button></div></form></Modal>}
  </div>;
}
