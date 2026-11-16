import { lazy } from 'react';
import { Navigate, Outlet, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AppShell } from './components/AppShell';
import { useAuth } from './contexts';
import { ToastProvider } from './components/ToastProvider';
import { LoginPage } from './pages/LoginPage';
import type { UserRole } from './types';

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

function RequireRole({ roles, children }: { roles: UserRole[]; children: ReactNode }) {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  return roles.includes(user.role) ? <>{children}</> : <Navigate to="/" replace />;
}

export default function App() {
  return <ToastProvider><Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<RequireAuth />}>
      <Route element={<AppShell />}>
        <Route index element={<DashboardPage />} />
        <Route path="leaves" element={<LeaveRequestsPage />} />
        <Route path="all-leaves" element={<RequireRole roles={['HR']}><LeaveRequestsPage all /></RequireRole>} />
        <Route path="request" element={<RequireRole roles={['Employee', 'Manager']}><RequestLeavePage /></RequireRole>} />
        <Route path="approvals" element={<RequireRole roles={['Manager', 'HR']}><ApprovalsPage /></RequireRole>} />
        <Route path="my-team" element={<RequireRole roles={['Manager']}><ManagerTeamPage /></RequireRole>} />
        <Route path="calendar" element={<CalendarPage />} />
        <Route path="employees" element={<RequireRole roles={['HR']}><EmployeesPage /></RequireRole>} />
        <Route path="departments" element={<RequireRole roles={['HR']}><DepartmentsPage /></RequireRole>} />
        <Route path="policies" element={<RequireRole roles={['HR']}><PoliciesPage /></RequireRole>} />
        <Route path="profile" element={<ProfilePage />} />
      </Route>
    </Route>
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes></ToastProvider>;
}
