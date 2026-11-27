import { Suspense, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import {
  Bell, CalendarDays, ChevronDown, ChevronLeft, ChevronRight, CircleHelp, ClipboardCheck,
  Clock3, FileClock, LayoutDashboard, LogOut, Menu, Settings2, Users, UserRound,
  Building2, CalendarRange, BriefcaseBusiness, Sparkles,
} from 'lucide-react';
import { useAuth } from '../contexts';
import { DEMO_MODE } from '../services/config';
import { getApiErrorMessage } from '../services/apiClient';
import { Avatar } from './ui';
import { BUSINESS_TIME_ZONE } from '../utils/businessTime';
import { hasAnyPermission, hasApprovalAccess, hasPermission, type PermissionRequirement } from '../utils/authorization';

type NavItem = { label: string; to: string; icon: typeof LayoutDashboard; requirements?: PermissionRequirement[]; accessMode?: 'approvals'; badge?: string };
const primaryItems: NavItem[] = [
  { label: 'Tổng quan', to: '/', icon: LayoutDashboard },
  { label: 'Đơn nghỉ của tôi', to: '/leaves', icon: FileClock, requirements: [{ permission: 'leaves.read', scopes: ['Self', 'Team', 'All'] }] },
  { label: 'Tạo đơn nghỉ', to: '/request', icon: CalendarDays, requirements: [{ permission: 'leaves.create', scopes: ['Self'] }] },
  { label: 'Hộp thư phê duyệt', to: '/approvals', icon: ClipboardCheck, accessMode: 'approvals' },
  { label: 'Nhân viên phụ trách', to: '/my-team', icon: Users, requirements: [{ permission: 'employees.read', scopes: ['Team'] }] },
  { label: 'Lịch nghỉ của nhóm', to: '/calendar', icon: CalendarRange, requirements: [{ permission: 'leaves.read', scopes: ['Self', 'Team', 'All'] }] },
];
const adminItems: NavItem[] = [
  { label: 'Nhân viên', to: '/employees', icon: Users, requirements: [{ permission: 'employees.read', scopes: ['All'] }] },
  { label: 'Phòng ban', to: '/departments', icon: Building2, requirements: [
    { permission: 'departments.create', scopes: ['All'] },
    { permission: 'departments.update', scopes: ['All'] },
    { permission: 'departments.delete', scopes: ['All'] },
  ] },
  { label: 'Chính sách nghỉ phép', to: '/policies', icon: BriefcaseBusiness, requirements: [
    { permission: 'leave-policies.create', scopes: ['All'] },
    { permission: 'leave-policies.update', scopes: ['All'] },
    { permission: 'holidays.create', scopes: ['All'] },
    { permission: 'holidays.update', scopes: ['All'] },
    { permission: 'holidays.delete', scopes: ['All'] },
    { permission: 'leave-types.update', scopes: ['All'] },
  ] },
  { label: 'Tất cả đơn nghỉ', to: '/all-leaves', icon: Clock3, requirements: [{ permission: 'leaves.read', scopes: ['All'] }] },
];

export function AppShell() {
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const role = user?.role ?? 'Employee';
  const policyRequirements: PermissionRequirement[] = [
    { permission: 'leave-policies.create', scopes: ['All'] },
    { permission: 'leave-policies.update', scopes: ['All'] },
    { permission: 'holidays.create', scopes: ['All'] },
    { permission: 'holidays.update', scopes: ['All'] },
    { permission: 'holidays.delete', scopes: ['All'] },
    { permission: 'leave-types.update', scopes: ['All'] },
  ];
  const canManagePolicies = hasAnyPermission(user?.permissions, policyRequirements);
  const visiblePrimary = primaryItems.filter((item) =>
    (!item.requirements || hasAnyPermission(user?.permissions, item.requirements)) &&
    (item.accessMode !== 'approvals' || hasApprovalAccess(user?.permissions)));
  const visibleAdmin = adminItems.filter((item) =>
    !item.requirements || hasAnyPermission(user?.permissions, item.requirements));

  async function handleSignOut() {
    try { await signOut(); }
    catch (error) { window.alert(getApiErrorMessage(error)); }
    finally { navigate('/login'); }
  }
  function renderNav(items: NavItem[]) {
    return items.map((item) => {
      const Icon = item.icon;
      const canReadTeamOrAllLeaves = hasPermission(user?.permissions, 'leaves.read', ['Team', 'All']);
      const label = item.to === '/leaves' && canReadTeamOrAllLeaves
        ? 'Đơn nghỉ trong phạm vi'
        : item.to === '/calendar' && !canReadTeamOrAllLeaves
          ? 'Lịch nghỉ của tôi'
          : item.to === '/calendar'
            ? 'Lịch nghỉ trong phạm vi'
            : item.label;
      return <NavLink end={item.to === '/'} to={item.to} className={({ isActive }) => `nav-link${isActive ? ' nav-link-active' : ''}`} key={item.to} onClick={() => setMobileOpen(false)} title={collapsed ? item.label : undefined}>
        <Icon size={19} strokeWidth={1.8} /><span className="nav-label">{label}</span>{item.badge && <span className="nav-badge">{item.badge}</span>}
      </NavLink>;
    });
  }

  return (
    <div className={`app-shell${collapsed ? ' sidebar-collapsed' : ''}${mobileOpen ? ' mobile-nav-open' : ''}`}>
      <aside className="sidebar">
        <div className="brand-row">
          <div className="brand-mark"><Sparkles size={21} /></div>
          <div className="brand-copy"><strong>people<span>flow</span></strong><small>QUẢN TRỊ NHÂN SỰ</small></div>
          <button className="collapse-button" aria-label={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'} title={collapsed ? 'Mở rộng menu' : 'Thu gọn menu'} onClick={() => setCollapsed((value) => !value)}>{collapsed ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}</button>
        </div>
        <div className="workspace-switcher"><div className="workspace-icon">N</div><div className="workspace-copy"><strong>Không gian nhân sự</strong><small>Không gian làm việc</small></div><ChevronDown size={15} /></div>
        <nav className="sidebar-nav">
          <div className="nav-section-label">LÀM VIỆC</div>
          {renderNav(visiblePrimary)}
          {visibleAdmin.length > 0 && <><div className="nav-section-label nav-section-spaced">QUẢN TRỊ</div>{renderNav(visibleAdmin)}</>}
        </nav>
        <div className="sidebar-bottom">
          <NavLink to="/profile" className={({ isActive }) => `nav-link${isActive ? ' nav-link-active' : ''}`} title={collapsed ? 'Hồ sơ của tôi' : undefined}><UserRound size={19} /><span className="nav-label">Hồ sơ của tôi</span></NavLink>
          <button className="nav-link nav-help" onClick={() => window.open('https://github.com/OlYVER1337/hr-leave-management', '_blank', 'noopener,noreferrer')}><CircleHelp size={19} /><span className="nav-label">Trợ giúp & tài liệu</span></button>
          <div className="sidebar-divider" />
          <div className="sidebar-user"><Avatar name={user?.fullName ?? 'User'} /><div className="sidebar-user-copy"><strong>{user?.fullName ?? 'User'}</strong><small>{role === 'HR' ? 'HR Administrator' : role}</small></div><button className="icon-button user-logout" title="Đăng xuất" onClick={handleSignOut}><LogOut size={17} /></button></div>
        </div>
      </aside>
      {mobileOpen && <button className="mobile-scrim" aria-label="Đóng menu" onClick={() => setMobileOpen(false)} />}
      <div className="main-column">
        <header className="topbar">
          <button className="mobile-menu-button icon-button" aria-label="Mở menu" onClick={() => setMobileOpen((value) => !value)}><Menu size={21} /></button>
          <div className="breadcrumb"><span>Không gian làm việc</span><ChevronRight size={15} /><strong>Quản trị nhân sự</strong></div>
          <div className="topbar-right">
            {DEMO_MODE && <span className="demo-pill"><span /> Chế độ dùng thử</span>}
            <span className="topbar-date">{new Intl.DateTimeFormat('vi-VN', { timeZone: BUSINESS_TIME_ZONE, weekday: 'short', month: 'short', day: 'numeric' }).format(new Date())}</span>
            <button className="icon-button notification-button" aria-label="Thông báo chưa khả dụng" title="Thông báo chưa khả dụng" disabled><Bell size={19} /></button>
            <div className="topbar-profile-wrap">
              <button className="topbar-profile" onClick={() => setProfileOpen((value) => !value)}><Avatar name={user?.fullName ?? 'Người dùng'} size="sm" /><span>{user?.fullName.split(' ').slice(-1)[0]}</span><ChevronDown size={14} /></button>
              {profileOpen && <div className="profile-menu"><button onClick={() => { setProfileOpen(false); navigate('/profile'); }}><UserRound size={16} /> Hồ sơ của tôi</button>{canManagePolicies && <button onClick={() => { setProfileOpen(false); navigate('/policies'); }}><Settings2 size={16} /> Chính sách nghỉ phép</button>}<button onClick={handleSignOut}><LogOut size={16} /> Đăng xuất</button></div>}
            </div>
          </div>
        </header>
        <main className="main-content">
          <Suspense fallback={<div className="table-loading" role="status" aria-live="polite"><span aria-hidden="true" /> Đang tải trang...</div>}>
            <Outlet />
          </Suspense>
        </main>
        <footer className="app-footer"><span>Bộ công cụ nhân sự Peopleflow</span><span>An toàn · Lấy con người làm trung tâm · Dành cho đội nhóm</span></footer>
      </div>
    </div>
  );
}
