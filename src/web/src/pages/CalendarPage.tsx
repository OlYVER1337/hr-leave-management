import { useEffect, useMemo, useState } from 'react';
import { CalendarDays, ChevronLeft, ChevronRight, Plus, Users } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Avatar, EmptyState, PageHeader, StatusBadge, formatDate } from '../components/ui';
import { useAuth } from '../contexts';
import { getCalendarLeaves, getHolidays } from '../services/leaveService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Holiday, LeaveRequest } from '../types';

function isoDate(year: number, month: number, day: number) {
  const date = new Date(year, month, day);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

export function CalendarPage() {
  const { user } = useAuth();
  const [monthDate, setMonthDate] = useState(() => new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [holidays, setHolidays] = useState<Holiday[]>([]);
  const [error, setError] = useState('');
  const [selectedDate, setSelectedDate] = useState(() => isoDate(new Date().getFullYear(), new Date().getMonth(), new Date().getDate()));
  const year = monthDate.getFullYear(); const month = monthDate.getMonth();

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

  const monthLabel = new Intl.DateTimeFormat('vi-VN', { month: 'long', year: 'numeric' }).format(monthDate);
  const weeks = useMemo(() => {
    const first = new Date(year, month, 1);
    const offset = (first.getDay() + 6) % 7;
    const total = new Date(year, month + 1, 0).getDate();
    const count = Math.ceil((offset + total) / 7) * 7;
    return Array.from({ length: count }, (_, index) => {
      const day = index - offset + 1;
      return day < 1 || day > total ? null : isoDate(year, month, day);
    });
  }, [year, month]);
  const visibleRequests = requests.filter((item) => item.status === 'Approved' || item.status === 'Pending');
  const selectedRequests = visibleRequests.filter((item) => selectedDate >= item.startDate && selectedDate <= item.endDate);
  const selectedHolidays = holidays.filter((item) => item.date === selectedDate);
  const upcoming = [...visibleRequests].filter((item) => item.endDate >= isoDate(new Date().getFullYear(), new Date().getMonth(), new Date().getDate())).sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, 5);

  return <div className="page-stack">
    <PageHeader eyebrow="LẬP KẾ HOẠCH NHÓM" title="Lịch nghỉ của nhóm" description="Theo dõi lịch nghỉ dự kiến và ngày lễ của tổ chức." actions={<Link to="/request" className="button button-primary"><Plus size={16} /> Tạo đơn nghỉ</Link>} />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <div className="calendar-layout">
      <section className="panel calendar-panel">
        <div className="calendar-toolbar"><div><h2>{monthLabel}</h2><p>Lịch nghỉ trong tổ chức</p></div><div className="calendar-nav"><button className="icon-button" aria-label="Tháng trước" onClick={() => { setMonthDate(new Date(year, month - 1, 1)); setSelectedDate(isoDate(year, month - 1, 1)); }}><ChevronLeft size={18} /></button><button className="button button-secondary button-today" onClick={() => { const now = new Date(); setMonthDate(new Date(now.getFullYear(), now.getMonth(), 1)); setSelectedDate(isoDate(now.getFullYear(), now.getMonth(), now.getDate())); }}>Hôm nay</button><button className="icon-button" aria-label="Tháng sau" onClick={() => { setMonthDate(new Date(year, month + 1, 1)); setSelectedDate(isoDate(year, month + 1, 1)); }}><ChevronRight size={18} /></button></div></div>
        <div className="calendar-legend"><span><i className="legend-dot approved-dot" /> Nghỉ đã duyệt</span><span><i className="legend-dot pending-dot" /> Chờ duyệt</span><span><i className="legend-dot holiday-dot" /> Ngày lễ</span></div>
        <div className="calendar-grid"><div className="calendar-weekday">T2</div><div className="calendar-weekday">T3</div><div className="calendar-weekday">T4</div><div className="calendar-weekday">T5</div><div className="calendar-weekday">T6</div><div className="calendar-weekday weekend">T7</div><div className="calendar-weekday weekend">CN</div>
          {weeks.map((day, index) => {
            const dayRequests = day ? visibleRequests.filter((item) => day >= item.startDate && day <= item.endDate) : [];
            const dayHoliday = day ? holidays.some((item) => item.date === day) : false;
            const isToday = day === isoDate(new Date().getFullYear(), new Date().getMonth(), new Date().getDate());
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
        <article className="panel upcoming-panel"><div className="panel-header"><div><h2>Sắp tới</h2><p>Các ngày nghỉ đã duyệt và đang chờ</p></div><Users size={18} className="upcoming-icon" /></div>{upcoming.length === 0 ? <EmptyState title="Chưa có lịch nghỉ sắp tới" description="Các ngày nghỉ trong tương lai sẽ hiển thị tại đây." /> : <div className="upcoming-list">{upcoming.map((item) => <button key={item.id} className="upcoming-item" onClick={() => setSelectedDate(item.startDate)}><span className="upcoming-date"><strong>{new Date(`${item.startDate}T12:00:00`).getDate()}</strong><small>{new Intl.DateTimeFormat('vi-VN', { month: 'short' }).format(new Date(`${item.startDate}T12:00:00`))}</small></span><span className="upcoming-copy"><strong>{item.employeeName}</strong><small>{item.typeName}</small></span><StatusBadge status={item.status} /></button>)}</div>}</article>
      </aside>
    </div>
  </div>;
}
