import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { ArrowLeft, ArrowRight, CalendarDays, Clock3, Info, ShieldCheck } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { PageHeader, formatDate } from '../components/ui';
import { useAuth } from '../contexts';
import { useToast } from '../components/ToastProvider';
import { createLeaveRequest, getBalances, getHolidays, getRequestLeaveTypes, type CreateLeaveInput, type RequestLeaveType } from '../services/leaveService';
import type { Holiday, LeaveBalance } from '../types';

const defaultTypes: Array<RequestLeaveType & { color: string; icon: typeof CalendarDays }> = [
  { code: 'ANNUAL', name: 'Nghỉ phép năm', description: 'Nghỉ hưởng lương để nghỉ ngơi, du lịch hoặc giải quyết việc cá nhân.', deductsBalance: true, color: 'blue', icon: CalendarDays },
  { code: 'SICK', name: 'Nghỉ ốm', description: 'Nghỉ để điều trị bệnh hoặc khám sức khỏe.', deductsBalance: true, color: 'violet', icon: ShieldCheck },
  { code: 'UNPAID', name: 'Nghỉ không lương', description: 'Nghỉ không hưởng lương và không trừ vào số dư phép có lương.', deductsBalance: false, color: 'amber', icon: Clock3 },
];

function estimateWorkdays(startDate: string, endDate: string, holidays: Holiday[]): number {
  if (!startDate || !endDate || endDate < startDate) return 0;
  const holidaySet = new Set(holidays.map((holiday) => holiday.date));
  let count = 0;
  for (let date = new Date(`${startDate}T12:00:00`); date <= new Date(`${endDate}T12:00:00`); date.setDate(date.getDate() + 1)) {
    const day = date.getDay();
    const iso = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
    if (day !== 0 && day !== 6 && !holidaySet.has(iso)) count++;
  }
  return count;
}

function getTodayDate(): string {
  const today = new Date();
  return `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
}

export function RequestLeavePage() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const notify = useToast();
  const [types, setTypes] = useState(defaultTypes);
  const [typeCode, setTypeCode] = useState<CreateLeaveInput['typeCode']>('ANNUAL');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [reason, setReason] = useState('');
  const [balances, setBalances] = useState<LeaveBalance[]>([]);
  const [holidays, setHolidays] = useState<Holiday[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!user) return;
    Promise.allSettled([getRequestLeaveTypes(), getBalances(user), getHolidays()]).then((results) => {
      const failures: string[] = [];
      if (results[0].status === 'fulfilled') {
        const availableTypes = results[0].value.map((type) => {
          const fallback = defaultTypes.find((item) => item.code === type.code);
          const color = fallback?.color ?? 'blue';
          const icon = fallback?.icon ?? CalendarDays;
          return { ...type, color, icon };
        });
        setTypes(availableTypes);
        setTypeCode((current) => availableTypes.some((type) => type.code === current) ? current : availableTypes[0]?.code ?? '');
      } else failures.push(results[0].reason instanceof Error ? results[0].reason.message : 'Không tải được loại nghỉ phép.');
      if (results[1].status === 'fulfilled') setBalances(results[1].value);
      else failures.push(results[1].reason instanceof Error ? results[1].reason.message : 'Không tải được số dư phép.');
      if (results[2].status === 'fulfilled') setHolidays(results[2].value);
      else failures.push(results[2].reason instanceof Error ? results[2].reason.message : 'Không tải được ngày lễ.');
      if (failures.length) setError(failures.join(' '));
    });
  }, [user]);

  const days = useMemo(() => estimateWorkdays(startDate, endDate, holidays), [startDate, endDate, holidays]);
  const selectedType = types.find((item) => item.code === typeCode) ?? types[0] ?? defaultTypes[0];
  const SelectedTypeIcon = selectedType.icon;
  const selectedBalance = balances.find((item) => item.code === typeCode);
  const today = getTodayDate();
  const dateInPast = Boolean((startDate && startDate < today) || (endDate && endDate < today));
  const dateInvalid = Boolean(startDate && endDate && endDate < startDate);
  const balanceInsufficient = selectedBalance?.available != null && days > selectedBalance.available;

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError('');
    if (!user) { setError('Bạn cần đăng nhập lại.'); return; }
    if (dateInPast) { setError('Ngày bắt đầu và ngày kết thúc phải từ hôm nay trở đi.'); return; }
    if (!startDate || !endDate || dateInvalid || days < 1) { setError('Vui lòng chọn khoảng thời gian hợp lệ có ít nhất một ngày làm việc.'); return; }
    if (balanceInsufficient) { setError('Số ngày nghỉ vượt quá số dư hiện có.'); return; }
    setSubmitting(true);
    try {
      await createLeaveRequest(user, { typeCode, startDate, endDate, reason });
      notify('Đã gửi đơn nghỉ phép để chờ phê duyệt.');
      navigate('/leaves');
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Không thể gửi yêu cầu.'); }
    finally { setSubmitting(false); }
  }

  return <div className="page-stack">
    <PageHeader eyebrow="NGHỈ PHÉP" title="Tạo đơn nghỉ phép" description="Gửi yêu cầu nghỉ để quản lý xem xét và phê duyệt." actions={<Link to="/leaves" className="button button-secondary"><ArrowLeft size={16} /> Đơn nghỉ của tôi</Link>} />
    <div className="request-layout">
      <form className="panel request-form-panel" onSubmit={onSubmit}>
        <div className="form-section-heading"><span className="form-section-number">01</span><div><h2>Thông tin nghỉ phép</h2><p>Chọn loại nghỉ và khoảng thời gian bạn cần nghỉ.</p></div></div>
        <div className="field-group"><label>Loại nghỉ <span className="required-mark">*</span></label><div className="leave-type-options">{types.map((type) => { const Icon = type.icon; return <button type="button" key={type.code} className={`leave-type-option ${type.color}${typeCode === type.code ? ' selected' : ''}`} onClick={() => setTypeCode(type.code)}><span className="type-option-icon"><Icon size={18} /></span><span className="type-option-copy"><strong>{type.name}</strong><small>{type.description}</small></span><span className={`custom-radio${typeCode === type.code ? ' checked' : ''}`} /></button>; })}</div>{types.length === 0 && <p className="field-error">Không có loại nghỉ nào đang hoạt động.</p>}</div>
        <div className="form-divider" />
        <div className="form-section-heading"><span className="form-section-number">02</span><div><h2>Thời gian nghỉ</h2><p>Số ngày nghỉ tính theo ngày làm việc, không bao gồm cuối tuần và ngày lễ.</p></div></div>
        <div className="date-fields"><div className="field-group"><label htmlFor="startDate">Ngày bắt đầu <span className="required-mark">*</span></label><div className="field-icon-wrap"><CalendarDays size={17} /><input id="startDate" type="date" min={today} required value={startDate} onChange={(event) => { setStartDate(event.target.value); if (endDate && event.target.value > endDate) setEndDate(event.target.value); }} /></div></div><div className="field-group"><label htmlFor="endDate">Ngày kết thúc <span className="required-mark">*</span></label><div className="field-icon-wrap"><CalendarDays size={17} /><input id="endDate" type="date" min={startDate && startDate > today ? startDate : today} required value={endDate} onChange={(event) => setEndDate(event.target.value)} /></div></div></div>
        {dateInPast && <p className="field-error">Ngày nghỉ không thể nằm trong quá khứ.</p>}
        {dateInvalid && <p className="field-error">Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.</p>}
        <div className="duration-preview"><span className="duration-icon"><Clock3 size={19} /></span><div><strong>{days} ngày làm việc</strong><small>{startDate && endDate && !dateInvalid ? `${formatDate(startDate)} – ${formatDate(endDate)}` : 'Chọn ngày để xem trước thời lượng nghỉ'}</small></div><span className="duration-label">ƯỚC TÍNH</span></div>
        <div className="form-divider" />
        <div className="form-section-heading"><span className="form-section-number">03</span><div><h2>Bổ sung thông tin</h2><p>Ghi chú ngắn giúp quản lý sắp xếp công việc trong nhóm.</p></div></div>
        <div className="field-group"><label htmlFor="reason">Lý do nghỉ <span className="required-mark">*</span></label><textarea id="reason" required minLength={5} maxLength={600} rows={4} placeholder="Nhập lý do nghỉ phép…" value={reason} onChange={(event) => setReason(event.target.value)} /><div className="field-helper-row"><span>Hạn chế chia sẻ thông tin cá nhân nhạy cảm.</span><span>{reason.length}/600</span></div></div>
        {error && <div className="form-error form-error-block">{error}</div>}
        <div className="form-actions"><Link to="/leaves" className="button button-secondary">Hủy</Link><button type="submit" disabled={submitting || types.length === 0 || dateInPast || dateInvalid || !startDate || !endDate || days < 1} className="button button-primary">{submitting ? 'Đang gửi…' : 'Gửi yêu cầu'}<ArrowRight size={17} /></button></div>
      </form>
      <aside className="request-aside">
        <article className="panel request-summary-panel"><div className="aside-card-heading"><span className={`aside-type-icon ${selectedType.color}`}><SelectedTypeIcon size={19} /></span><div><h3>Tóm tắt yêu cầu</h3><p>Kiểm tra trước khi gửi</p></div></div><div className="summary-line"><span>Loại nghỉ</span><strong>{selectedType.name}</strong></div><div className="summary-line"><span>Ngày bắt đầu</span><strong>{startDate ? formatDate(startDate) : 'Chưa chọn'}</strong></div><div className="summary-line"><span>Ngày kết thúc</span><strong>{endDate ? formatDate(endDate) : 'Chưa chọn'}</strong></div><div className="summary-line"><span>Thời lượng</span><strong>{days} ngày</strong></div><div className="summary-line"><span>Người duyệt</span><strong>{user?.role === 'HR' ? 'Bộ phận nhân sự' : user?.role === 'Manager' ? 'Bộ phận nhân sự' : 'Quản lý trực tiếp'}</strong></div><div className="summary-total"><span>Tổng số ngày nghỉ ước tính</span><strong>{days}</strong></div></article>
        <article className="panel balance-mini-panel"><div className="aside-card-heading"><span className="aside-type-icon blue"><ShieldCheck size={19} /></span><div><h3>Số dư phép hiện có</h3><p>Sau khi trừ các đơn đang chờ</p></div></div>{selectedBalance ? <><div className="balance-mini-numbers"><strong>{selectedBalance.available === null ? '∞' : selectedBalance.available}</strong><span>{selectedBalance.available === null ? 'Không giới hạn cố định' : `/ ${selectedBalance.entitled} ngày`}</span></div><div className="progress-track"><span className={`progress-${selectedBalance.color}`} style={{ width: selectedBalance.entitled ? `${Math.max(0, Math.min(100, ((selectedBalance.available ?? 0) / selectedBalance.entitled) * 100))}%` : '100%' }} /></div><p className={`balance-check-note${balanceInsufficient ? ' balance-check-error' : ''}`}>{balanceInsufficient ? 'Số ngày yêu cầu vượt quá số dư hiện có.' : `Đã dùng ${selectedBalance.used} ngày · Đang chờ ${selectedBalance.pending} ngày.`}</p></> : <p className="muted-copy">Chưa có số dư cho loại nghỉ và năm này.</p>}</article>
        <article className="policy-note"><span><Info size={17} /></span><div><strong>Lưu ý</strong><p>Số ngày hiển thị chỉ là ước tính. Backend sẽ tính lại ngày làm việc, ngày lễ, đơn trùng và số dư trước khi tiếp nhận yêu cầu.</p></div></article>
      </aside>
    </div>
  </div>;
}
