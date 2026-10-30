import { Navigate, Outlet, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import { AppShell } from './components/AppShell';
import { useAuth } from './contexts';
import { ToastProvider } from './components/ToastProvider';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { LeaveRequestsPage } from './pages/LeaveRequestsPage';
import { RequestLeavePage } from './pages/RequestLeavePage';
import { ApprovalsPage } from './pages/ApprovalsPage';
import { CalendarPage } from './pages/CalendarPage';
import { EmployeesPage } from './pages/EmployeesPage';
import { DepartmentsPage } from './pages/DepartmentsPage';
import { PoliciesPage } from './pages/PoliciesPage';
import { ProfilePage } from './pages/ProfilePage';
import type { UserRole } from './types';

function RequireAuth() {
  const { user } = useAuth();
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
        <Route path="request" element={<RequestLeavePage />} />
        <Route path="approvals" element={<RequireRole roles={['Manager', 'HR']}><ApprovalsPage /></RequireRole>} />
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
