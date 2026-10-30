import { useEffect, useState, type FormEvent } from 'react';
import { CalendarDays, KeyRound, Mail, ShieldCheck, UserRound } from 'lucide-react';
import { Avatar, PageHeader, formatDate } from '../components/ui';
import { useAuth } from '../contexts';
import { useToast } from '../components/ToastProvider';
import { changePassword } from '../services/authService';
import { getCurrentEmployee } from '../services/hrService';
import { getApiErrorMessage } from '../services/apiClient';
import type { Employee } from '../types';

export function ProfilePage() {
  const { user } = useAuth();
  const notify = useToast();
  const [employee, setEmployee] = useState<Employee | null>(null);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  useEffect(() => { if (user) getCurrentEmployee(user.id).then(setEmployee).catch((reason) => setError(getApiErrorMessage(reason))); }, [user]);

  async function submitPassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError('');
    if (newPassword !== confirmPassword) { setError('Mật khẩu mới và mật khẩu xác nhận không khớp.'); return; }
    if (newPassword.length < 12 || newPassword.length > 30) { setError('Mật khẩu phải có từ 12 đến 30 ký tự.'); return; }
    setSaving(true);
    try { await changePassword(currentPassword, newPassword); notify('Đã đổi mật khẩu.'); setCurrentPassword(''); setNewPassword(''); setConfirmPassword(''); }
    catch (reason) { setError(getApiErrorMessage(reason)); }
    finally { setSaving(false); }
  }

  return <div className="page-stack"><PageHeader eyebrow="TÀI KHOẢN CỦA BẠN" title="Hồ sơ của tôi" description="Xem thông tin tài khoản và cập nhật mật khẩu đăng nhập." />
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <div className="profile-layout"><section className="panel profile-card"><div className="profile-cover"><div className="profile-cover-orb" /></div><div className="profile-main"><Avatar name={user?.fullName ?? 'Người dùng'} size="lg" /><div className="profile-name-row"><div><h2>{user?.fullName}</h2><p>{user?.email}</p></div><span className={`role-pill role-${user?.role.toLowerCase()}`}>{user?.role === 'HR' ? 'Nhân sự' : user?.role === 'Manager' ? 'Quản lý' : 'Nhân viên'}</span></div><div className="profile-details"><div><span><Mail size={16} /> Địa chỉ email</span><strong>{employee?.email ?? user?.email}</strong></div><div><span><UserRound size={16} /> Mã nhân viên</span><strong>{employee?.code ?? '—'}</strong></div><div><span><ShieldCheck size={16} /> Phòng ban</span><strong>{employee?.departmentName ?? '—'}</strong></div><div><span><CalendarDays size={16} /> Ngày vào làm</span><strong>{employee?.hireDate ? formatDate(employee.hireDate) : '—'}</strong></div><div><span><UserRound size={16} /> Quản lý trực tiếp</span><strong>{employee?.managerName ?? 'Chưa phân công'}</strong></div><div><span><ShieldCheck size={16} /> Trạng thái tài khoản</span><strong><span className={`employee-status ${employee?.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{employee ? employee.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa' : '—'}</span></strong></div></div></div></section>
      <section className="panel password-panel"><div className="panel-header"><div><h2>Đổi mật khẩu</h2><p>Sử dụng mật khẩu riêng và an toàn cho tài khoản công việc.</p></div><span className="password-panel-icon"><KeyRound size={19} /></span></div><form className="modal-form password-form" onSubmit={submitPassword}><div className="field-group"><label htmlFor="current-password">Mật khẩu hiện tại *</label><input id="current-password" type="password" required autoComplete="current-password" value={currentPassword} onChange={(event) => setCurrentPassword(event.target.value)} /></div><div className="field-group"><label htmlFor="new-password">Mật khẩu mới *</label><input id="new-password" type="password" required minLength={12} maxLength={30} autoComplete="new-password" value={newPassword} onChange={(event) => setNewPassword(event.target.value)} /><small className="field-help">Từ 12 đến 30 ký tự theo quy định của HR Service.</small></div><div className="field-group"><label htmlFor="confirm-password">Xác nhận mật khẩu mới *</label><input id="confirm-password" type="password" required minLength={12} maxLength={30} autoComplete="new-password" value={confirmPassword} onChange={(event) => setConfirmPassword(event.target.value)} /></div><button className="button button-primary button-full" disabled={saving} type="submit">{saving ? 'Đang cập nhật…' : 'Cập nhật mật khẩu'}</button></form><div className="security-footnote"><ShieldCheck size={16} /><span>Mật khẩu được backend xác thực và mã hóa. Không chia sẻ thông tin đăng nhập của bạn.</span></div></section></div>
  </div>;
}
