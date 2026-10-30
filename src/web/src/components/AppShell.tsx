import { useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import {
  Bell, CalendarDays, ChevronDown, ChevronLeft, ChevronRight, CircleHelp, ClipboardCheck,
  Clock3, FileClock, LayoutDashboard, LogOut, Menu, Settings2, Users, UserRound,
  Building2, CalendarRange, BriefcaseBusiness, Sparkles,
} from 'lucide-react';
import { useAuth } from '../contexts';
import { DEMO_MODE } from '../services/config';
import { Avatar } from './ui';

type NavItem = { label: string; to: string; icon: typeof LayoutDashboard; roles?: Array<'HR' | 'Manager' | 'Employee'>; badge?: string };
const primaryItems: NavItem[] = [
  { label: 'Tổng quan', to: '/', icon: LayoutDashboard },
  { label: 'Đơn nghỉ của tôi', to: '/leaves', icon: FileClock, roles: ['Employee', 'Manager'] },
  { label: 'Tạo đơn nghỉ', to: '/request', icon: CalendarDays, roles: ['Employee', 'Manager'] },
  { label: 'Hộp thư phê duyệt', to: '/approvals', icon: ClipboardCheck, roles: ['Manager', 'HR'] },
  { label: 'Lịch nghỉ của nhóm', to: '/calendar', icon: CalendarRange, roles: ['Employee', 'Manager', 'HR'] },
];
const adminItems: NavItem[] = [
  { label: 'Nhân viên', to: '/employees', icon: Users, roles: ['HR'] },
  { label: 'Phòng ban', to: '/departments', icon: Building2, roles: ['HR'] },
  { label: 'Chính sách nghỉ phép', to: '/policies', icon: BriefcaseBusiness, roles: ['HR'] },
  { label: 'Tất cả đơn nghỉ', to: '/all-leaves', icon: Clock3, roles: ['HR'] },
];

export function AppShell() {
  const { user, signOut } = useAuth();
  const navigate = useNavigate();
  const [collapsed, setCollapsed] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [profileOpen, setProfileOpen] = useState(false);
  const role = user?.role ?? 'Employee';
  const visiblePrimary = primaryItems.filter((item) => !item.roles || item.roles.includes(role));
  const visibleAdmin = adminItems.filter((item) => item.roles?.includes(role));

  function handleSignOut() { signOut(); navigate('/login'); }
  function renderNav(items: NavItem[]) {
    return items.map((item) => {
      const Icon = item.icon;
      return <NavLink end={item.to === '/'} to={item.to} className={({ isActive }) => `nav-link${isActive ? ' nav-link-active' : ''}`} key={item.to} onClick={() => setMobileOpen(false)} title={collapsed ? item.label : undefined}>
        <Icon size={19} strokeWidth={1.8} /><span className="nav-label">{item.label}</span>{item.badge && <span className="nav-badge">{item.badge}</span>}
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
            <span className="topbar-date">{new Intl.DateTimeFormat('vi-VN', { weekday: 'short', month: 'short', day: 'numeric' }).format(new Date())}</span>
            <button className="icon-button notification-button" aria-label="Thông báo"><Bell size={19} /><i /></button>
            <div className="topbar-profile-wrap">
              <button className="topbar-profile" onClick={() => setProfileOpen((value) => !value)}><Avatar name={user?.fullName ?? 'Người dùng'} size="sm" /><span>{user?.fullName.split(' ').slice(-1)[0]}</span><ChevronDown size={14} /></button>
              {profileOpen && <div className="profile-menu"><button onClick={() => { setProfileOpen(false); navigate('/profile'); }}><UserRound size={16} /> Hồ sơ của tôi</button><button onClick={() => { setProfileOpen(false); navigate('/policies'); }}><Settings2 size={16} /> Cài đặt</button><button onClick={handleSignOut}><LogOut size={16} /> Đăng xuất</button></div>}
            </div>
          </div>
        </header>
        <main className="main-content"><Outlet /></main>
        <footer className="app-footer"><span>Bộ công cụ nhân sự Peopleflow</span><span>An toàn · Lấy con người làm trung tâm · Dành cho đội nhóm</span></footer>
      </div>
    </div>
  );
}
