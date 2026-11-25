import { useEffect, useMemo, useState } from 'react';
import { CalendarDays, ChevronLeft, ChevronRight, Plus, Users } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Avatar, EmptyState, PageHeader, StatusBadge, formatDate } from '../components/ui';
import { useAuth } from '../contexts';
import { getCalendarLeaves, getHolidays } from '../services/leaveService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Holiday, LeaveRequest } from '../types';
import { BUSINESS_TIME_ZONE, formatDateOnlyUtc, getBusinessDate, parseDateOnlyUtc } from '../utils/businessTime';

function isoDate(year: number, month: number, day: number): string {
  return formatDateOnlyUtc(new Date(Date.UTC(year, month, day)));
}

export function CalendarPage() {
  const { user } = useAuth();
  const [monthDate, setMonthDate] = useState(() => parseDateOnlyUtc(`${getBusinessDate().slice(0, 7)}-01`));
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [holidays, setHolidays] = useState<Holiday[]>([]);
  const [error, setError] = useState('');
  const [selectedDate, setSelectedDate] = useState(getBusinessDate);
  const year = monthDate.getUTCFullYear(); const month = monthDate.getUTCMonth();

  useEffect(() => {
    let active = true;
    const from = isoDate(year, month, 1);
    const to = isoDate(year, month + 1, 0);
    setError('');
    if (!user) return () => { active = false; };
    Promise.allSettled([getCalendarLeaves(user, from, to), getHolidays(from, to)]).then((results) => {
      if (!active) return;
      if (results[0].status === 'fulfilled') setRequests(results[0].value); else setError(getApiErrorMessage(getRejectedReason(results[0])));
      if (results[1].status === 'fulfilled') setHolidays(results[1].value); else setError((current) => current || getApiErrorMessage(getRejectedReason(results[1])));
    });
    return () => { active = false; };
  }, [user, year, month]);

  const monthLabel = new Intl.DateTimeFormat('vi-VN', { timeZone: BUSINESS_TIME_ZONE, month: 'long', year: 'numeric' }).format(monthDate);
  const weeks = useMemo(() => {
    const first = new Date(Date.UTC(year, month, 1));
    const offset = (first.getUTCDay() + 6) % 7;
    const total = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
    const count = Math.ceil((offset + total) / 7) * 7;
    return Array.from({ length: count }, (_, index) => {
      const day = index - offset + 1;
      return day < 1 || day > total ? null : isoDate(year, month, day);
    });
  }, [year, month]);
  const visibleRequests = requests.filter((item) => item.status === 'Approved' || item.status === 'Pending');
  const selectedRequests = visibleRequests.filter((item) => selectedDate >= item.startDate && selectedDate <= item.endDate);
  const selectedHolidays = holidays.filter((item) => item.date === selectedDate);
  const today = getBusinessDate();
  const upcoming = [...visibleRequests].filter((item) => item.endDate >= today).sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, 5);

  return <div className="page-stack">
    <PageHeader eyebrow="LẬP KẾ HOẠCH NHÓM" title="Lịch nghỉ của nhóm" description="Theo dõi lịch nghỉ dự kiến và ngày lễ của tổ chức." actions={user?.role !== 'HR' ? <Link to="/request" className="button button-primary"><Plus size={16} /> Tạo đơn nghỉ</Link> : undefined} />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <div className="calendar-layout">
      <section className="panel calendar-panel">
        <div className="calendar-toolbar"><div><h2>{monthLabel}</h2><p>Lịch nghỉ trong tổ chức</p></div><div className="calendar-nav"><button className="icon-button" aria-label="Tháng trước" onClick={() => { setMonthDate(new Date(Date.UTC(year, month - 1, 1))); setSelectedDate(isoDate(year, month - 1, 1)); }}><ChevronLeft size={18} /></button><button className="button button-secondary button-today" onClick={() => { const todayDate = getBusinessDate(); const [todayYear, todayMonth] = todayDate.split('-').map(Number); setMonthDate(new Date(Date.UTC(todayYear, todayMonth - 1, 1))); setSelectedDate(todayDate); }}>Hôm nay</button><button className="icon-button" aria-label="Tháng sau" onClick={() => { setMonthDate(new Date(Date.UTC(year, month + 1, 1))); setSelectedDate(isoDate(year, month + 1, 1)); }}><ChevronRight size={18} /></button></div></div>
        <div className="calendar-legend"><span><i className="legend-dot approved-dot" /> Nghỉ đã duyệt</span><span><i className="legend-dot pending-dot" /> Chờ duyệt</span><span><i className="legend-dot holiday-dot" /> Ngày lễ</span></div>
        <div className="calendar-grid"><div className="calendar-weekday">T2</div><div className="calendar-weekday">T3</div><div className="calendar-weekday">T4</div><div className="calendar-weekday">T5</div><div className="calendar-weekday">T6</div><div className="calendar-weekday weekend">T7</div><div className="calendar-weekday weekend">CN</div>
          {weeks.map((day, index) => {
            const dayRequests = day ? visibleRequests.filter((item) => day >= item.startDate && day <= item.endDate) : [];
            const dayHoliday = day ? holidays.some((item) => item.date === day) : false;
            const isToday = day === today;
            return <button type="button" key={`${day ?? 'empty'}-${index}`} disabled={!day} className={`calendar-day${!day ? ' calendar-day-outside' : ''}${day === selectedDate ? ' calendar-day-selected' : ''}${isToday ? ' calendar-day-today' : ''}`} onClick={() => day && setSelectedDate(day)}><span className="calendar-day-number">{day ? Number(day.slice(-2)) : ''}</span>{dayHoliday && <span className="calendar-event holiday-event">Ngày lễ</span>}{dayRequests.slice(0, 2).map((item) => <span className={`calendar-event ${item.status === 'Pending' ? 'pending-event' : 'approved-event'}`} key={item.id}>{item.employeeName.split(' ').slice(-1)[0]}</span>)}{dayRequests.length > 2 && <span className="calendar-extra">+{dayRequests.length - 2} nữa</span>}</button>;
          })}
        </div>
      </section>
      <aside className="calendar-aside">
        <article className="panel selected-day-panel"><div className="panel-header"><div><h2>{formatDate(selectedDate, { weekday: 'long', month: 'short', day: 'numeric' })}</h2><p>Ngày đã chọn</p></div><span className="selected-day-icon"><CalendarDays size={18} /></span></div>
          {selectedHolidays.map((item) => <div className="selected-event selected-holiday" key={item.id}><span className="event-mark holiday-mark" /><div><strong>{item.name}</strong><small>Ngày nghỉ lễ</small></div></div>)}
          {selectedRequests.map((item) => <div className="selected-event" key={item.id}><Avatar name={item.employeeName} size="sm" /><div className="selected-event-copy"><strong>{item.employeeName}</strong><small>{item.typeName} · {item.days} ngày</small></div><span className={`event-status-dot ${item.status.toLowerCase()}`} /> </div>)}
          {selectedRequests.length === 0 && selectedHolidays.length === 0 && <EmptyState title="Không có lịch nghỉ" description="Không có đơn nghỉ hoặc ngày lễ vào ngày này." />}
        </article>
        <article className="panel upcoming-panel"><div className="panel-header"><div><h2>Sắp tới</h2><p>Các ngày nghỉ đã duyệt và đang chờ</p></div><Users size={18} className="upcoming-icon" /></div>{upcoming.length === 0 ? <EmptyState title="Chưa có lịch nghỉ sắp tới" description="Các ngày nghỉ trong tương lai sẽ hiển thị tại đây." /> : <div className="upcoming-list">{upcoming.map((item) => <button key={item.id} className="upcoming-item" onClick={() => setSelectedDate(item.startDate)}><span className="upcoming-date"><strong>{parseDateOnlyUtc(item.startDate).getUTCDate()}</strong><small>{new Intl.DateTimeFormat('vi-VN', { timeZone: BUSINESS_TIME_ZONE, month: 'short' }).format(parseDateOnlyUtc(item.startDate))}</small></span><span className="upcoming-copy"><strong>{item.employeeName}</strong><small>{item.typeName}</small></span><StatusBadge status={item.status} /></button>)}</div>}</article>
      </aside>
    </div>
  </div>;
}
