import { lazy, Suspense, useEffect, useMemo, useRef, useState } from 'react';
import { ArrowRight, CalendarDays, CheckCircle2, Clock3, Coffee, Download, FileCheck2, Plus, ShieldCheck, Sparkles, Users } from 'lucide-react';
import { Link } from 'react-router-dom';
import { Avatar, EmptyState, formatDate, PageHeader, StatCard, StatusBadge } from '../components/ui';
import { useAuth } from '../contexts';
import { getApprovalsInbox, getBalances, getLeaveRequests } from '../services/leaveService';
import { getDepartments, getEmployeePage } from '../services/hrService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Department, LeaveBalance, LeaveRequest } from '../types';
import { BUSINESS_TIME_ZONE, getBusinessDate } from '../utils/businessTime';

const DashboardCharts = lazy(() =>
  import('../components/DashboardCharts').then(({ DashboardCharts }) => ({
    default: DashboardCharts,
  })),
);


export function DashboardPage() {
  const { user } = useAuth();
  const [requests, setRequests] = useState<LeaveRequest[]>([]);
  const [approvalInbox, setApprovalInbox] = useState<LeaveRequest[]>([]);
  const [balances, setBalances] = useState<LeaveBalance[]>([]);
  // Số liệu nhân viên lấy từ totalCount của API phân trang, không tải danh sách đầy đủ.
  const [employeeStats, setEmployeeStats] = useState({ total: 0, active: 0 });
  const [departments, setDepartments] = useState<Department[]>([]);
  const [loading, setLoading] = useState(true);
  const [approvalInboxLoaded, setApprovalInboxLoaded] = useState(false);
  const [loadError, setLoadError] = useState('');

  useEffect(() => {
    let active = true;
    async function load() {
      setLoading(true); setApprovalInboxLoaded(false); setLoadError('');
      const results = await Promise.allSettled([
        user ? getLeaveRequests(user) : Promise.resolve([]),
        user ? getBalances(user) : Promise.resolve([]),
        user?.role === 'HR'
          ? Promise.all([
              getEmployeePage({ page: 1, pageSize: 1 }),
              getEmployeePage({ page: 1, pageSize: 1, isActive: true }),
            ])
          : Promise.resolve(null),
        user?.role === 'HR' ? getDepartments() : Promise.resolve([]),
        user?.role === 'Manager' ? getApprovalsInbox(user) : Promise.resolve([]),
      ]);
      if (!active) return;
      if (results[0].status === 'fulfilled') setRequests(results[0].value); else setLoadError(getApiErrorMessage(getRejectedReason(results[0])));
      if (results[1].status === 'fulfilled') setBalances(results[1].value); else setLoadError((current) => current || getApiErrorMessage(getRejectedReason(results[1])));
      if (results[2].status === 'fulfilled') {
        const [all, active] = results[2].value ?? [null, null];
        setEmployeeStats({ total: all?.totalCount ?? 0, active: active?.totalCount ?? 0 });
      } else {
        setLoadError((current) => current || getApiErrorMessage(getRejectedReason(results[2])));
      }
      if (results[3].status === 'fulfilled') setDepartments(results[3].value); else setLoadError((current) => current || getApiErrorMessage(getRejectedReason(results[3])));
      if (results[4].status === 'fulfilled') {
        setApprovalInbox(results[4].value);
        setApprovalInboxLoaded(true);
      } else {
        setLoadError((current) => current || getApiErrorMessage(getRejectedReason(results[4])));
      }
      setLoading(false);
    }
    void load(); return () => { active = false; };
  }, [user]);

  const chartData = useMemo(() => {
    const [currentYear, currentMonth] = getBusinessDate().slice(0, 7).split('-').map(Number);
    return Array.from({ length: 6 }, (_, index) => {
      const date = new Date(Date.UTC(currentYear, currentMonth - 1 - 5 + index, 1));
      const key = `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`;
      const monthRequests = requests.filter((request) => request.startDate.slice(0, 7) === key && request.status !== 'Cancelled');
      return {
        month: new Intl.DateTimeFormat('vi-VN', { timeZone: BUSINESS_TIME_ZONE, month: 'short' }).format(date),
        annual: monthRequests.filter((request) => request.typeCode === 'ANNUAL').length,
        sick: monthRequests.filter((request) => request.typeCode === 'SICK').length,
      };
    });
  }, [requests]);

  const myRequests = useMemo(() => requests.filter((item) => item.employeeId === user?.id), [requests, user?.id]);
  const scopedRequests = user?.role === 'Employee' ? myRequests : requests;
  const pendingCount = requests.filter((item) => item.status === 'Pending').length;
  const managerPendingCount = approvalInbox.filter((item) => item.status === 'Pending').length;
  const managerPendingValue = approvalInboxLoaded ? managerPendingCount : '—';
  const managerPendingDescription = approvalInboxLoaded
    ? `Có ${managerPendingCount} yêu cầu đang chờ bạn phê duyệt.`
    : 'Chưa thể tải số đơn đang chờ bạn phê duyệt.';
  const usedDays = balances.filter((item) => item.code !== 'UNPAID').reduce((sum, item) => sum + item.used, 0);
  const availableDays = balances.filter((item) => item.available !== null).reduce((sum, item) => sum + (item.available ?? 0), 0);
  const firstName = user?.fullName.split(' ').slice(-1)[0] ?? 'bạn';
  const roleSubtitle = user?.role === 'HR' ? 'Tổng quan hoạt động và tình hình nhân sự trong tổ chức.' : user?.role === 'Manager' ? 'Tổng quan về đội nhóm và các yêu cầu đang chờ duyệt.' : 'Theo dõi ngày nghỉ và các yêu cầu của bạn.';
  const statusData = [
    { name: 'Đã duyệt', value: requests.filter((item) => item.status === 'Approved').length, color: '#2fb78a' },
    { name: 'Chờ duyệt', value: pendingCount, color: '#f5b544' },
    { name: 'Bị từ chối', value: requests.filter((item) => item.status === 'Rejected').length, color: '#e56d78' },
    { name: 'Đã hủy', value: requests.filter((item) => item.status === 'Cancelled').length, color: '#9aa5b5' },
  ];
  const displayRequests = [...scopedRequests].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).slice(0, 5);

  return (
    <div className="page-stack">
      <PageHeader eyebrow="TỔNG QUAN" title={`Xin chào, ${firstName}`} description={roleSubtitle} actions={<><button className="button button-secondary" disabled title="Chưa khả dụng: API xuất báo cáo chưa được triển khai"><Download size={16} /> Xuất báo cáo (chưa khả dụng)</button>{user?.role !== 'HR' && <Link to="/request" className="button button-primary"><Plus size={17} /> Tạo đơn nghỉ</Link>}</>} />
      {loadError && <div className="inline-alert alert-warning"><span>{loadError}</span></div>}
      {loading && <div className="loading-line"><span /> Đang tải dữ liệu…</div>}
      <div className="welcome-strip"><div className="welcome-spark"><Sparkles size={20} /></div><div><strong>{user?.role === 'HR' ? 'Nắm bắt tình hình nhân sự.' : user?.role === 'Manager' ? 'Đội nhóm luôn trong tầm kiểm soát.' : 'Dành thời gian cho điều quan trọng.'}</strong><p>{user?.role === 'HR' ? 'Theo dõi hoạt động nghỉ phép và chính sách nhân sự.' : user?.role === 'Manager' ? managerPendingDescription : 'Chủ động lên kế hoạch và cập nhật ngày nghỉ của bạn.'}</p></div><div className="welcome-art"><span /><span /><span /><span /><span /></div></div>
      <section className="stat-grid">
        {user?.role === 'HR' ? <>
          <StatCard label="Nhân viên đang hoạt động" value={employeeStats.active || '—'} hint={`${employeeStats.total} hồ sơ nhân viên`} icon={<Users size={19} />} tone="blue" />
          <StatCard label="Đơn chờ phê duyệt" value={pendingCount} hint="Trong toàn tổ chức" icon={<Clock3 size={19} />} tone="amber" />
          <StatCard label="Đơn đã phê duyệt" value={requests.filter((item) => item.status === 'Approved').length} hint="Trong hệ thống" icon={<FileCheck2 size={19} />} tone="green" />
          <StatCard label="Phòng ban" value={departments.length || '—'} hint="Các đội nhóm hiện có" icon={<Coffee size={19} />} tone="violet" />
        </> : <>
          <StatCard label="Số ngày phép còn lại" value={availableDays} hint="Phép năm và phép ốm" icon={<CalendarDays size={19} />} tone="blue" />
          <StatCard label="Số ngày đã nghỉ" value={usedDays} hint="Ngày nghỉ hưởng lương đã duyệt" icon={<CheckCircle2 size={19} />} tone="green" />
          <StatCard label={user?.role === 'Manager' ? 'Đơn chờ tôi phê duyệt' : 'Đang chờ xử lý'} value={user?.role === 'Manager' ? managerPendingValue : myRequests.filter((item) => item.status === 'Pending').length} hint={user?.role === 'Manager' ? 'Trong hộp thư phê duyệt' : 'Yêu cầu đang chờ quyết định'} icon={<Clock3 size={19} />} tone="amber" />
          <StatCard label="Đơn nghỉ của tôi" value={myRequests.length} hint="Tất cả yêu cầu" icon={<FileCheck2 size={19} />} tone="violet" />
        </>}
      </section>

      <DeferredDashboardCharts chartData={chartData} statusData={statusData} />

      <section className="dashboard-grid lower-grid">
        <article className="panel table-panel">
          <div className="panel-header"><div><h2>{user?.role === 'Employee' ? 'Đơn nghỉ gần đây của tôi' : 'Đơn nghỉ gần đây'}</h2><p>Cập nhật hoạt động mới nhất</p></div><Link to={user?.role === 'HR' ? '/all-leaves' : '/leaves'} className="text-link">Xem tất cả <ArrowRight size={15} /></Link></div>
          {displayRequests.length === 0 ? <EmptyState title="Chưa có đơn nghỉ phép" description="Các yêu cầu nghỉ sẽ hiển thị tại đây sau khi được gửi." /> : <div className="table-scroll"><table className="data-table"><thead><tr>{user?.role !== 'Employee' && <th>Nhân viên</th>}<th>Loại nghỉ</th><th>Thời gian</th><th>Số ngày</th><th>Trạng thái</th></tr></thead><tbody>{displayRequests.map((item) => <tr key={item.id}>{user?.role !== 'Employee' && <td><div className="person-cell"><Avatar name={item.employeeName} size="sm" /><div><strong>{item.employeeName}</strong><small>{item.departmentName}</small></div></div></td>}<td><span className="leave-type-cell"><span className={`leave-type-dot ${item.typeCode.toLowerCase()}`} />{item.typeName}</span></td><td><div className="date-range-cell">{formatDate(item.startDate, { month: 'short', day: 'numeric' })}<span>—</span>{formatDate(item.endDate, { month: 'short', day: 'numeric' })}</div></td><td>{item.days} ngày</td><td><StatusBadge status={item.status} /></td></tr>)}</tbody></table></div>}
        </article>
        <article className="panel balance-panel">
          <div className="panel-header"><div><h2>Số dư phép của tôi</h2><p>Số ngày còn lại trong năm nay</p></div><Link to="/leaves" className="panel-more-link">Chi tiết <ArrowRight size={14} /></Link></div>
          <div className="balance-list">{balances.filter((item) => item.code !== 'UNPAID').map((balance) => {
            const percent = balance.entitled ? Math.min(100, Math.round(((balance.available ?? 0) / balance.entitled) * 100)) : 0;
            return <div className="balance-item" key={balance.code}><div className="balance-item-top"><span className={`balance-icon balance-${balance.color}`}>{balance.code === 'ANNUAL' ? <CalendarDays size={17} /> : <ShieldCheck size={17} />}</span><div className="balance-label"><strong>{balance.name}</strong><small>Đã dùng {balance.used} · Chờ duyệt {balance.pending}</small></div><div className="balance-numbers"><strong>{balance.available}</strong><small>/ {balance.entitled} ngày</small>{balance.policyDays !== undefined && balance.policyDays !== balance.entitled && <small>Quota HR: {balance.policyDays} ngày</small>}</div></div><div className="progress-track"><span className={`progress-${balance.color}`} style={{ width: `${percent}%` }} /></div></div>;
          })}</div>
          <div className="balance-note"><span className="balance-note-icon"><Sparkles size={16} /></span><p>{user?.role === 'HR' ? 'Số dư phép cá nhân được tính theo chính sách hiện hành.' : 'Dự định nghỉ phép? Hãy gửi yêu cầu sớm để đội nhóm chủ động sắp xếp.'}</p>{user?.role !== 'HR' && <Link to="/request" aria-label="Tạo đơn nghỉ phép"><ArrowRight size={16} /></Link>}</div>
        </article>
      </section>
    </div>
  );
}

type ActivityPoint = { month: string; annual: number; sick: number };
type StatusPoint = { name: string; value: number; color: string };

function DeferredDashboardCharts({
  chartData,
  statusData,
}: {
  chartData: ActivityPoint[];
  statusData: StatusPoint[];
}) {
  const sectionRef = useRef<HTMLElement | null>(null);
  const [shouldLoad, setShouldLoad] = useState(false);

  useEffect(() => {
    const section = sectionRef.current;
    if (!section || typeof IntersectionObserver === 'undefined') {
      // Render immediately in browsers without IntersectionObserver (and in jsdom tests).
      setShouldLoad(true);
      return;
    }

    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((entry) => entry.isIntersecting)) {
          setShouldLoad(true);
          observer.disconnect();
        }
      },
      { rootMargin: '200px 0px' },
    );

    observer.observe(section);
    return () => observer.disconnect();
  }, []);

  return (
    <section
      ref={sectionRef}
      className="dashboard-grid"
      aria-label="Biểu đồ tổng quan"
    >
      {shouldLoad ? (
        <Suspense fallback={
          <div className="panel chart-panel" role="status" aria-live="polite">
            Đang tải biểu đồ...
          </div>
        }>
          <DashboardCharts chartData={chartData} statusData={statusData} />
        </Suspense>
      ) : (
        <>
          <div className="panel chart-panel" aria-label="Biểu đồ hoạt động nghỉ phép đang chờ tải">
            <div className="loading-line"><span /> Biểu đồ hoạt động nghỉ phép sẽ tải khi cần...</div>
          </div>
          <div className="panel breakdown-panel" aria-label="Biểu đồ trạng thái đơn nghỉ đang chờ tải">
            <div className="loading-line"><span /> Biểu đồ trạng thái đơn nghỉ sẽ tải khi cần...</div>
          </div>
        </>
      )}
    </section>
  );
}
