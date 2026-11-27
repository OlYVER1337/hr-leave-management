import { lazy } from 'react';
import { Navigate, Outlet, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AppShell } from './components/AppShell';
import { useAuth } from './contexts';
import { ToastProvider } from './components/ToastProvider';
import { LoginPage } from './pages/LoginPage';
import { hasAnyPermission, hasApprovalAccess, hasPermission, type PermissionRequirement } from './utils/authorization';

// Load authenticated pages only when their route is visited.
// Named page exports are adapted to React.lazy's required default export.
const DashboardPage = lazy(() =>
  import('./pages/DashboardPage').then(({ DashboardPage }) => ({ default: DashboardPage })),
);
const LeaveRequestsPage = lazy(() =>
  import('./pages/LeaveRequestsPage').then(({ LeaveRequestsPage }) => ({ default: LeaveRequestsPage })),
);
const RequestLeavePage = lazy(() =>
  import('./pages/RequestLeavePage').then(({ RequestLeavePage }) => ({ default: RequestLeavePage })),
);
const ApprovalsPage = lazy(() =>
  import('./pages/ApprovalsPage').then(({ ApprovalsPage }) => ({ default: ApprovalsPage })),
);
const CalendarPage = lazy(() =>
  import('./pages/CalendarPage').then(({ CalendarPage }) => ({ default: CalendarPage })),
);
const EmployeesPage = lazy(() =>
  import('./pages/EmployeesPage').then(({ EmployeesPage }) => ({ default: EmployeesPage })),
);
const ManagerTeamPage = lazy(() =>
  import('./pages/ManagerTeamPage').then(({ ManagerTeamPage }) => ({ default: ManagerTeamPage })),
);
const DepartmentsPage = lazy(() =>
  import('./pages/DepartmentsPage').then(({ DepartmentsPage }) => ({ default: DepartmentsPage })),
);
const PoliciesPage = lazy(() =>
  import('./pages/PoliciesPage').then(({ PoliciesPage }) => ({ default: PoliciesPage })),
);
const ProfilePage = lazy(() =>
  import('./pages/ProfilePage').then(({ ProfilePage }) => ({ default: ProfilePage })),
);

function RequireAuth() {
  const { user, busy } = useAuth();
  if (busy) return null;
  return user ? <Outlet /> : <Navigate to="/login" replace />;
}

function RequirePermission({
  permission,
  scopes,
  children,
}: {
  permission: string;
  scopes?: readonly string[];
  children: ReactNode;
}) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  return hasPermission(user.permissions, permission, scopes)
    ? <>{children}</>
    : <Navigate to="/" replace />;
}

function RequireAnyPermission({
  requirements,
  children,
}: {
  requirements: PermissionRequirement[];
  children: ReactNode;
}) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  return hasAnyPermission(user.permissions, requirements)
    ? <>{children}</>
    : <Navigate to="/" replace />;
}

function RequireApprovalAccess({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  return hasApprovalAccess(user.permissions) ? <>{children}</> : <Navigate to="/" replace />;
}

export default function App() {
  return <ToastProvider><Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<RequireAuth />}>
      <Route element={<AppShell />}>
        <Route index element={<DashboardPage />} />
        <Route path="leaves" element={<RequirePermission permission="leaves.read" scopes={['Self', 'Team', 'All']}><LeaveRequestsPage /></RequirePermission>} />
        <Route path="all-leaves" element={<RequirePermission permission="leaves.read" scopes={['All']}><LeaveRequestsPage all /></RequirePermission>} />
        <Route path="request" element={<RequirePermission permission="leaves.create" scopes={['Self']}><RequestLeavePage /></RequirePermission>} />
        <Route path="approvals" element={<RequireApprovalAccess><ApprovalsPage /></RequireApprovalAccess>} />
        <Route path="my-team" element={<RequirePermission permission="employees.read" scopes={['Team']}><ManagerTeamPage /></RequirePermission>} />
        <Route path="calendar" element={<RequirePermission permission="leaves.read" scopes={['Self', 'Team', 'All']}><CalendarPage /></RequirePermission>} />
        <Route path="employees" element={<RequirePermission permission="employees.read" scopes={['All']}><EmployeesPage /></RequirePermission>} />
        <Route path="departments" element={<RequireAnyPermission requirements={[{ permission: 'departments.create', scopes: ['All'] }, { permission: 'departments.update', scopes: ['All'] }, { permission: 'departments.delete', scopes: ['All'] }]}><DepartmentsPage /></RequireAnyPermission>} />
        <Route path="policies" element={<RequireAnyPermission requirements={[{ permission: 'leave-policies.create', scopes: ['All'] }, { permission: 'leave-policies.update', scopes: ['All'] }, { permission: 'holidays.create', scopes: ['All'] }, { permission: 'holidays.update', scopes: ['All'] }, { permission: 'holidays.delete', scopes: ['All'] }, { permission: 'leave-types.update', scopes: ['All'] }]}><PoliciesPage /></RequireAnyPermission>} />
        <Route path="profile" element={<ProfilePage />} />
      </Route>
    </Route>
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></ToastProvider>;
}
