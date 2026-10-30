import { useEffect, useMemo, useState } from 'react';
import { ArrowRight, CalendarDays, CheckCircle2, Clock3, Coffee, Download, FileCheck2, Plus, ShieldCheck, Sparkles, Users } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Bar, BarChart, CartesianGrid, Cell, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Avatar, EmptyState, formatDate, PageHeader, StatCard, StatusBadge } from '../components/ui';
import { useAuth } from '../contexts';
import { getBalances, getLeaveRequests } from '../services/leaveService';
import { getDepartments, getEmployees } from '../services/hrService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import { useToast } from '../components/ToastProvider';
import type { Department, Employee, LeaveBalance, LeaveRequest } from '../types';

const tooltipStyle = { border: '1px solid #e5eaf2', borderRadius: 12, boxShadow: '0 8px 24px rgba(17, 34, 68, 0.08)', fontSize: 12 };

export function DashboardPage() {
  const { user } = useAuth();
  const notify = useToast();
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [balances, setBalances] = useState<LeaveBalance[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');

  useEffect(() => {
    let active = true;
    async function load() {
      setLoading(true); setLoadError('');
      const results = await Promise.allSettled([user ? getLeaveRequests(user) : Promise.resolve([]), user ? getBalances(user) : Promise.resolve([]), user?.role === 'HR' ? getEmployees() : Promise.resolve([]), user?.role === 'HR' ? getDepartments() : Promise.resolve([])]);
      if (!active) return;
      if (results[0].status === 'fulfilled') setRequests(results[0].value); else setLoadError(getApiErrorMessage(getRejectedReason(results[0])));
      if (results[1].status === 'fulfilled') setBalances(results[1].value); else setLoadError((current) => current || getApiErrorMessage(getRejectedReason(results[1])));
      if (results[2].status === 'fulfilled') setEmployees(results[2].value); else setLoadError((current) => current || getApiErrorMessage(getRejectedReason(results[2])));
      if (results[3].status === 'fulfilled') setDepartments(results[3].value); else setLoadError((current) => current || getApiErrorMessage(getRejectedReason(results[3])));
      setLoading(false);
    }
    void load(); return () => { active = false; };
  }, [user]);

  const chartData = useMemo(() => {
    const now = new Date();
    return Array.from({ length: 6 }, (_, index) => {
      const date = new Date(now.getFullYear(), now.getMonth() - 5 + index, 1);
      const key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
      const monthRequests = requests.filter((request) => request.startDate.slice(0, 7) === key && request.status !== 'Cancelled');
      return {
        month: new Intl.DateTimeFormat('vi-VN', { month: 'short' }).format(date),
        annual: monthRequests.filter((request) => request.typeCode === 'ANNUAL').length,
        sick: monthRequests.filter((request) => request.typeCode === 'SICK').length,
      };
    });
  }, [requests]);

  const myRequests = useMemo(() => requests.filter((item) => item.employeeId === user?.id), [requests, user?.id]);
  const scopedRequests = user?.role === 'Employee' ? myRequests : requests;
  const pendingCount = requests.filter((item) => item.status === 'Pending').length;
  const usedDays = balances.filter((item) => item.code !== 'UNPAID').reduce((sum, item) => sum + item.used, 0);
  const availableDays = balances.filter((item) => item.available !== null).reduce((sum, item) => sum + (item.available ?? 0), 0);
  const firstName = user?.fullName.split(' ').slice(-1)[0] ?? 'bạn';
  const roleSubtitle = user?.role === 'HR' ? 'Tổng quan hoạt động và tình hình nhân sự trong tổ chức.' : user?.role === 'Manager' ? 'Tổng quan về đội nhóm và các yêu cầu đang chờ duyệt.' : 'Theo dõi ngày nghỉ và các yêu cầu của bạn.';
  const statusData = [
    { name: 'Đã duyệt', value: requests.filter((item) => item.status === 'Approved').length, color: '#2fb78a' },
    { name: 'Chờ duyệt', value: pendingCount, color: '#f5b544' },
    { name: 'Bị từ chối', value: requests.filter((item) => item.status === 'Rejected').length, color: '#e56d78' },
  ];
  const displayRequests = [...scopedRequests].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).slice(0, 5);

  return (
    <div className="page-stack">
      <PageHeader eyebrow="TỔNG QUAN" title={`Xin chào, ${firstName}`} description={roleSubtitle} actions={<><button className="button button-secondary" onClick={() => notify('Chức năng xuất báo cáo sẽ khả dụng khi có API báo cáo.', 'info')}><Download size={16} /> Xuất báo cáo</button><Link to="/request" className="button button-primary"><Plus size={17} /> Tạo đơn nghỉ</Link></>} />
      {loadError && <div className="inline-alert alert-warning"><span>{loadError}</span></div>}
      {loading && <div className="loading-line"><span /> Đang tải dữ liệu…</div>}
      <div className="welcome-strip"><div className="welcome-spark"><Sparkles size={20} /></div><div><strong>{user?.role === 'HR' ? 'Nắm bắt tình hình nhân sự.' : user?.role === 'Manager' ? 'Đội nhóm luôn trong tầm kiểm soát.' : 'Dành thời gian cho điều quan trọng.'}</strong><p>{user?.role === 'HR' ? 'Theo dõi hoạt động nghỉ phép và chính sách nhân sự.' : user?.role === 'Manager' ? `Có ${pendingCount} yêu cầu đang chờ phê duyệt trong đội nhóm.` : 'Chủ động lên kế hoạch và cập nhật ngày nghỉ của bạn.'}</p></div><div className="welcome-art"><span /><span /><span /><span /><span /></div></div>
      <section className="stat-grid">
        {user?.role === 'HR' ? <>
          <StatCard label="Nhân viên đang hoạt động" value={employees.filter((employee) => employee.isActive).length || '—'} hint={`${employees.length} hồ sơ nhân viên`} icon={<Users size={19} />} tone="blue" />
          <StatCard label="Đơn chờ phê duyệt" value={pendingCount} hint="Trong toàn tổ chức" icon={<Clock3 size={19} />} tone="amber" />
          <StatCard label="Đơn đã phê duyệt" value={requests.filter((item) => item.status === 'Approved').length} hint="Trong hệ thống" icon={<FileCheck2 size={19} />} tone="green" />
          <StatCard label="Phòng ban" value={departments.length || '—'} hint="Các đội nhóm hiện có" icon={<Coffee size={19} />} tone="violet" />
        </> : <>
          <StatCard label="Số ngày phép còn lại" value={availableDays} hint="Phép năm và phép ốm" icon={<CalendarDays size={19} />} tone="blue" />
          <StatCard label="Số ngày đã nghỉ" value={usedDays} hint="Ngày nghỉ hưởng lương đã duyệt" icon={<CheckCircle2 size={19} />} tone="green" />
          <StatCard label="Đang chờ xử lý" value={user?.role === 'Manager' ? pendingCount : myRequests.filter((item) => item.status === 'Pending').length} hint="Yêu cầu đang chờ quyết định" icon={<Clock3 size={19} />} tone="amber" />
          <StatCard label="Đơn nghỉ của tôi" value={myRequests.length} hint="Tất cả yêu cầu" icon={<FileCheck2 size={19} />} tone="violet" />
        </>}
      </section>

      <section className="dashboard-grid">
        <article className="panel chart-panel">
          <div className="panel-header"><div><h2>Hoạt động nghỉ phép</h2><p>Yêu cầu trong sáu tháng gần đây</p></div><span className="panel-select">6 tháng gần đây <ChevronDownIcon /></span></div>
          <div className="chart-legend"><span><i className="legend-dot annual" /> Nghỉ phép năm</span><span><i className="legend-dot sick" /> Nghỉ ốm</span></div>
          <div className="chart-wrap"><ResponsiveContainer width="100%" height="100%"><BarChart data={chartData} barGap={5} margin={{ top: 10, right: 8, left: -24, bottom: 0 }}><CartesianGrid stroke="#edf0f6" vertical={false} /><XAxis dataKey="month" axisLine={false} tickLine={false} tick={{ fill: '#7f8ba1', fontSize: 12 }} dy={8} /><YAxis axisLine={false} tickLine={false} tick={{ fill: '#7f8ba1', fontSize: 11 }} /><Tooltip contentStyle={tooltipStyle} cursor={{ fill: '#f6f8fc' }} /><Bar dataKey="annual" name="Nghỉ phép năm" fill="#5578e8" radius={[5, 5, 0, 0]} maxBarSize={22} /><Bar dataKey="sick" name="Nghỉ ốm" fill="#b9a6f7" radius={[5, 5, 0, 0]} maxBarSize={22} /></BarChart></ResponsiveContainer></div>
        </article>
        <article className="panel breakdown-panel">
          <div className="panel-header"><div><h2>Trạng thái đơn nghỉ</h2><p>Tình hình xử lý hiện tại</p></div><span className="panel-more">•••</span></div>
          <div className="donut-wrap"><ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={statusData} dataKey="value" nameKey="name" innerRadius={64} outerRadius={83} paddingAngle={4} stroke="none"><Cell fill="#2fb78a" /><Cell fill="#f5b544" /><Cell fill="#e56d78" /></Pie><Tooltip contentStyle={tooltipStyle} /></PieChart></ResponsiveContainer><div className="donut-center"><strong>{requests.length}</strong><span>Tổng số đơn</span></div></div>
          <div className="breakdown-legend">{statusData.map((item) => <div key={item.name}><span><i style={{ background: item.color }} />{item.name}</span><strong>{item.value}</strong></div>)}</div>
        </article>
      </section>

      <section className="dashboard-grid lower-grid">
        <article className="panel table-panel">
          <div className="panel-header"><div><h2>{user?.role === 'Employee' ? 'Đơn nghỉ gần đây của tôi' : 'Đơn nghỉ gần đây'}</h2><p>Cập nhật hoạt động mới nhất</p></div><Link to={user?.role === 'HR' ? '/all-leaves' : '/leaves'} className="text-link">Xem tất cả <ArrowRight size={15} /></Link></div>
          {displayRequests.length === 0 ? <EmptyState title="Chưa có đơn nghỉ phép" description="Các yêu cầu nghỉ sẽ hiển thị tại đây sau khi được gửi." /> : <div className="table-scroll"><table className="data-table"><thead><tr>{user?.role !== 'Employee' && <th>Nhân viên</th>}<th>Loại nghỉ</th><th>Thời gian</th><th>Số ngày</th><th>Trạng thái</th></tr></thead><tbody>{displayRequests.map((item) => <tr key={item.id}>{user?.role !== 'Employee' && <td><div className="person-cell"><Avatar name={item.employeeName} size="sm" /><div><strong>{item.employeeName}</strong><small>{item.departmentName}</small></div></div></td>}<td><span className="leave-type-cell"><span className={`leave-type-dot ${item.typeCode.toLowerCase()}`} />{item.typeName}</span></td><td><div className="date-range-cell">{formatDate(item.startDate, { month: 'short', day: 'numeric' })}<span>—</span>{formatDate(item.endDate, { month: 'short', day: 'numeric' })}</div></td><td>{item.days} ngày</td><td><StatusBadge status={item.status} /></td></tr>)}</tbody></table></div>}
        </article>
        <article className="panel balance-panel">
          <div className="panel-header"><div><h2>{user?.role === 'Employee' ? 'Số dư phép của tôi' : 'Tổng quan số dư phép'}</h2><p>Số ngày còn lại trong năm nay</p></div><Link to="/leaves" className="panel-more-link">Chi tiết <ArrowRight size={14} /></Link></div>
          <div className="balance-list">{balances.filter((item) => item.code !== 'UNPAID').map((balance) => {
            const percent = balance.entitled ? Math.min(100, Math.round(((balance.available ?? 0) / balance.entitled) * 100)) : 0;
            return <div className="balance-item" key={balance.code}><div className="balance-item-top"><span className={`balance-icon balance-${balance.color}`}>{balance.code === 'ANNUAL' ? <CalendarDays size={17} /> : <ShieldCheck size={17} />}</span><div className="balance-label"><strong>{balance.name}</strong><small>Đã dùng {balance.used} · Chờ duyệt {balance.pending}</small></div><div className="balance-numbers"><strong>{balance.available}</strong><small>/ {balance.entitled} ngày</small></div></div><div className="progress-track"><span className={`progress-${balance.color}`} style={{ width: `${percent}%` }} /></div></div>;
          })}</div>
          <div className="balance-note"><span className="balance-note-icon"><Sparkles size={16} /></span><p>Dự định nghỉ phép? Hãy gửi yêu cầu sớm để đội nhóm chủ động sắp xếp.</p><Link to="/request" aria-label="Tạo đơn nghỉ phép"><ArrowRight size={16} /></Link></div>
        </article>
      </section>
    </div>
  );
}

function ChevronDownIcon() { return <span aria-hidden="true">⌄</span>; }
