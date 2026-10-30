import { useState, type FormEvent } from 'react';
import { ArrowRight, CalendarCheck2, Check, Eye, EyeOff, Fingerprint, LockKeyhole, Mail, ShieldCheck, Sparkles, UsersRound } from 'lucide-react';
import { Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from '../contexts';
import { DEMO_MODE } from '../services/config';
import { useToast } from '../components/ToastProvider';

const accounts = [
  { label: 'Quản trị nhân sự', email: 'hr@company.com', role: 'Toàn quyền', color: 'blue' },
  { label: 'Quản lý nhóm', email: 'manager@company.com', role: 'Phê duyệt & nhóm', color: 'violet' },
  { label: 'Nhân viên', email: 'employee@company.com', role: 'Nghỉ phép cá nhân', color: 'green' },
];

export function LoginPage() {
  const { user, signIn, busy } = useAuth();
  const navigate = useNavigate();
  const notify = useToast();
  const [email, setEmail] = useState(DEMO_MODE ? 'hr@company.com' : '');
  const [password, setPassword] = useState(DEMO_MODE ? 'Demo123!' : '');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState('');
  if (user) return <Navigate to="/" replace />;

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError('');
    try { await signIn(email, password); notify('Đăng nhập thành công.'); navigate('/'); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Không thể đăng nhập.'); }
  }

  return (
    <div className="login-page">
      <section className="login-visual">
        <div className="login-brand"><span className="brand-mark"><Sparkles size={21} /></span><span>people<strong>flow</strong></span></div>
        <div className="login-visual-content">
          <span className="visual-kicker"><span /> NỀN TẢNG QUẢN TRỊ NHÂN SỰ</span>
          <h1>Con người là trên hết.<br /><em>Làm việc hiệu quả hơn.</em></h1>
          <p>Quản lý ngày nghỉ dễ dàng, hỗ trợ nhân viên và giúp đội nhóm luôn phối hợp nhịp nhàng.</p>
          <div className="visual-benefits">
            <div><span><CalendarCheck2 size={19} /></span><div><strong>Nghỉ phép không cần giấy tờ</strong><small>Gửi yêu cầu nhanh, phê duyệt rõ ràng.</small></div><Check size={17} /></div>
            <div><span><UsersRound size={19} /></span><div><strong>Đội nhóm luôn phối hợp</strong><small>Nắm được ai đang làm việc, ai đang nghỉ.</small></div><Check size={17} /></div>
            <div><span><ShieldCheck size={19} /></span><div><strong>An tâm cho tổ chức</strong><small>Phân quyền truy cập theo vai trò.</small></div><Check size={17} /></div>
          </div>
          <div className="login-visual-bottom"><div className="avatar-stack"><span>MA</span><span>LN</span><span>KL</span><span>+8</span></div><span>Dành cho những con người tạo nên công việc.</span></div>
        </div>
        <div className="login-orbit orbit-one" /><div className="login-orbit orbit-two" />
      </section>
      <section className="login-form-side">
        <div className="login-form-wrap">
          <div className="login-mobile-brand"><span className="brand-mark"><Sparkles size={21} /></span><span>people<strong>flow</strong></span></div>
          <div className="login-heading"><div className="login-icon"><Fingerprint size={23} /></div><div className="eyebrow">CHÀO MỪNG TRỞ LẠI</div><h2>Đăng nhập vào hệ thống</h2><p>Sử dụng tài khoản công việc để tiếp tục với Peopleflow.</p></div>
          {DEMO_MODE && <div className="demo-login-note"><Sparkles size={16} /><span>Đang bật chế độ dùng thử. Hãy chọn một tài khoản mẫu bên dưới.</span></div>}
          <form className="login-form" onSubmit={onSubmit}>
            <label htmlFor="email">Email công việc</label>
            <div className="input-with-icon"><Mail size={18} /><input id="email" type="email" autoComplete="username" required placeholder="you@company.com" value={email} onChange={(e) => setEmail(e.target.value)} /></div>
            <div className="form-label-row"><label htmlFor="password">Mật khẩu</label><span className="muted-label">Đăng nhập an toàn</span></div>
            <div className="input-with-icon"><LockKeyhole size={18} /><input id="password" type={showPassword ? 'text' : 'password'} autoComplete="current-password" required placeholder="Nhập mật khẩu" value={password} onChange={(e) => setPassword(e.target.value)} /><button type="button" className="password-toggle" aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'} onClick={() => setShowPassword((show) => !show)}>{showPassword ? <EyeOff size={18} /> : <Eye size={18} />}</button></div>
            {error && <div className="form-error">{error}</div>}
            <button className="button button-primary button-full login-submit" disabled={busy} type="submit">{busy ? 'Đang đăng nhập…' : 'Đăng nhập Peopleflow'}<ArrowRight size={17} /></button>
          </form>
          {DEMO_MODE && <div className="demo-accounts"><div className="demo-accounts-heading"><span>TRUY CẬP NHANH</span><span>Mật khẩu: <code>Demo123!</code></span></div>{accounts.map((account) => <button className="demo-account" key={account.email} onClick={() => { setEmail(account.email); setPassword('Demo123!'); setError(''); }}><span className={`demo-account-icon demo-${account.color}`}><UsersRound size={17} /></span><span className="demo-account-copy"><strong>{account.label}</strong><small>{account.email}</small></span><span className="demo-account-role">{account.role}</span></button>)}</div>}
          <div className="login-security"><ShieldCheck size={15} /><span>Phiên làm việc được bảo vệ bằng phân quyền theo vai trò.</span></div>
          <div className="login-legal">© {new Date().getFullYear()} Peopleflow HR Suite <span>·</span> Đơn giản hóa công tác quản trị nhân sự.</div>
        </div>
      </section>
    </div>
  );
}
