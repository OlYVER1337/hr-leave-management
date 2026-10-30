import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { ChevronDown, Download, Edit3, Plus, Search, UserCheck, UserMinus, Users } from 'lucide-react';
import { Avatar, EmptyState, formatDate, PageHeader } from '../components/ui';
import { Modal } from '../components/Modal';
import { useToast } from '../components/ToastProvider';
import { deactivateEmployee, getDepartments, getEmployees, saveEmployee, type EmployeeInput } from '../services/hrService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Department, Employee, UserRole } from '../types';

const today = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
};
const blankForm = (): EmployeeInput => ({ code: '', fullName: '', email: '', password: '', role: 'Employee', hireDate: today(), departmentId: 0, managerId: null });

export function EmployeesPage() {
  const notify = useToast();
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [role, setRole] = useState<'All' | UserRole>('All');
  const [activeFilter, setActiveFilter] = useState<'All' | 'Active' | 'Inactive'>('All');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Employee | null>(null);
  const [form, setForm] = useState<EmployeeInput>(blankForm());
  const [saving, setSaving] = useState(false);

  async function reload() {
    setLoading(true); setError('');
    const results = await Promise.allSettled([getEmployees(), getDepartments()]);
    if (results[0].status === 'fulfilled') setEmployees(results[0].value); else setError(getApiErrorMessage(getRejectedReason(results[0])));
    if (results[1].status === 'fulfilled') setDepartments(results[1].value); else setError((value) => value || getApiErrorMessage(getRejectedReason(results[1])));
    setLoading(false);
  }
  useEffect(() => { void reload(); }, []);

  const filtered = useMemo(() => employees.filter((item) => {
    const query = search.toLowerCase();
    const textMatch = [item.fullName, item.code, item.email, item.departmentName].some((value) => value.toLowerCase().includes(query));
    return textMatch && (role === 'All' || item.role === role) && (activeFilter === 'All' || (activeFilter === 'Active' ? item.isActive : !item.isActive));
  }), [employees, search, role, activeFilter]);

  function openCreate() {
  setEditing(null);
  setForm(blankForm());
  setModalOpen(true);
}  function openEdit(employee: Employee) { setEditing(employee); setForm({ code: employee.code, fullName: employee.fullName, email: employee.email, role: employee.role, hireDate: employee.hireDate.slice(0, 10), departmentId: employee.departmentId, managerId: employee.managerId }); setModalOpen(true); }
  function update<K extends keyof EmployeeInput>(key: K, value: EmployeeInput[K]) { setForm((current) => ({ ...current, [key]: value })); }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setSaving(true);
    try {
      await saveEmployee({ ...form, departmentId: Number(form.departmentId), managerId: form.managerId || null }, editing?.id);
      notify(editing ? 'Đã cập nhật nhân viên.' : 'Đã tạo nhân viên.'); setModalOpen(false); await reload();
    } catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
    finally { setSaving(false); }
  }

  async function onDeactivate(employee: Employee) {
    if (!window.confirm(`Vô hiệu hóa tài khoản ${employee.fullName}? Nhân viên sẽ không thể đăng nhập nữa.`)) return;
    try { await deactivateEmployee(employee.id); notify('Đã vô hiệu hóa nhân viên.'); await reload(); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
  }

  const managers = employees.filter((employee) => employee.role === 'Manager' && employee.isActive && employee.id !== editing?.id);
  return <div className="page-stack">
    <PageHeader eyebrow="QUẢN TRỊ" title="Nhân viên" description="Quản lý hồ sơ nhân viên, phân công đội nhóm và quyền truy cập." actions={<><button className="button button-secondary" onClick={() => notify('Chức năng xuất danh sách sẽ khả dụng khi có API báo cáo.', 'info')}><Download size={16} /> Xuất danh sách</button><button className="button button-primary" onClick={openCreate}><Plus size={17} /> Thêm nhân viên</button></>} />
    <div className="page-summary-row"><div className="summary-metric"><span>Tổng số nhân viên</span><strong>{employees.length}</strong></div><div className="summary-metric"><span>Đang hoạt động</span><strong>{employees.filter((item) => item.isActive).length}</strong></div><div className="summary-metric"><span>Quản lý</span><strong>{employees.filter((item) => item.role === 'Manager' && item.isActive).length}</strong></div><div className="summary-metric"><span>Phòng ban</span><strong>{departments.length}</strong></div></div>
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <section className="panel page-table-panel">
      <div className="table-toolbar"><div className="search-field"><Search size={17} /><input placeholder="Tìm tên, email, mã nhân viên…" aria-label="Tìm nhân viên" value={search} onChange={(event) => setSearch(event.target.value)} /></div><div className="toolbar-filters"><label className="select-wrap"><Users size={15} /><select aria-label="Lọc theo vai trò" value={role} onChange={(event) => setRole(event.target.value as 'All' | UserRole)}><option value="All">Tất cả vai trò</option><option value="HR">Nhân sự</option><option value="Manager">Quản lý</option><option value="Employee">Nhân viên</option></select><ChevronDown size={14} /></label><label className="select-wrap"><select aria-label="Lọc theo trạng thái" value={activeFilter} onChange={(event) => setActiveFilter(event.target.value as 'All' | 'Active' | 'Inactive')}><option value="All">Tất cả trạng thái</option><option value="Active">Đang hoạt động</option><option value="Inactive">Đã vô hiệu hóa</option></select><ChevronDown size={14} /></label></div></div>
      {loading ? <div className="table-loading"><span /> Đang tải nhân viên…</div> : filtered.length === 0 ? <EmptyState title="Không tìm thấy nhân viên" description="Thử từ khóa khác hoặc thêm nhân viên mới." /> : <div className="table-scroll"><table className="data-table"><thead><tr><th>Nhân viên</th><th>Mã nhân viên</th><th>Phòng ban</th><th>Vai trò</th><th>Quản lý</th><th>Ngày vào làm</th><th>Trạng thái</th><th /></tr></thead><tbody>{filtered.map((employee) => <tr key={employee.id}><td><div className="person-cell"><Avatar name={employee.fullName} size="sm" /><div><strong>{employee.fullName}</strong><small>{employee.email}</small></div></div></td><td><span className="code-pill">{employee.code}</span></td><td>{employee.departmentName || '—'}</td><td><span className={`role-pill role-${employee.role.toLowerCase()}`}>{employee.role === 'HR' ? 'Nhân sự' : employee.role === 'Manager' ? 'Quản lý' : 'Nhân viên'}</span></td><td>{employee.managerName || <span className="muted-copy">Chưa phân công</span>}</td><td>{formatDate(employee.hireDate)}</td><td><span className={`employee-status ${employee.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{employee.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</span></td><td><div className="row-actions"><button className="icon-button" title="Sửa nhân viên" aria-label={`Sửa ${employee.fullName}`} onClick={() => openEdit(employee)}><Edit3 size={16} /></button>{employee.isActive && <button className="icon-button danger-icon" title="Vô hiệu hóa nhân viên" aria-label={`Vô hiệu hóa ${employee.fullName}`} onClick={() => void onDeactivate(employee)}><UserMinus size={16} /></button>}</div></td></tr>)}</tbody></table></div>}
      <div className="table-footer"><span>Đang hiển thị <strong>{filtered.length}</strong> / <strong>{employees.length}</strong> nhân viên</span><span className="table-footer-note"><UserCheck size={14} /> Hồ sơ nhân viên</span></div>
    </section>

    {modalOpen && <Modal title={editing ? 'Sửa thông tin nhân viên' : 'Thêm nhân viên'} description={editing ? 'Cập nhật hồ sơ và tuyến báo cáo của nhân viên.' : 'Tạo tài khoản nhân viên mới trong tổ chức.'} onClose={() => setModalOpen(false)} size="lg"><form onSubmit={onSubmit} className="modal-form">
      <div className="form-grid-two"><div className="field-group"><label htmlFor="emp-code">Mã nhân viên *</label><input id="emp-code" required maxLength={20} value={form.code} onChange={(event) => update('code', event.target.value)} placeholder="Ví dụ: NV024" /></div><div className="field-group"><label htmlFor="emp-name">Họ và tên *</label><input id="emp-name" required maxLength={150} value={form.fullName} onChange={(event) => update('fullName', event.target.value)} placeholder="Họ và tên" /></div><div className="field-group"><label htmlFor="emp-email">Email công việc *</label><input id="emp-email" type="email" required maxLength={200} value={form.email} onChange={(event) => update('email', event.target.value)} placeholder="ten@congty.com" /></div><div className="field-group"><label htmlFor="emp-role">Vai trò *</label><label className="select-wrap select-wrap-full"><select id="emp-role" required value={form.role} onChange={(event) => update('role', event.target.value as UserRole)}><option value="Employee">Nhân viên</option><option value="Manager">Quản lý</option><option value="HR">Nhân sự</option></select><ChevronDown size={14} /></label></div><div className="field-group"><label htmlFor="emp-department">Phòng ban *</label><label className="select-wrap select-wrap-full"><select id="emp-department" required value={form.departmentId || ''} onChange={(event) => update('departmentId', Number(event.target.value))}><option value="" disabled>Chọn phòng ban</option>{departments.map((department) => <option value={department.id} key={department.id}>{department.name}</option>)}</select><ChevronDown size={14} /></label></div><div className="field-group"><label htmlFor="emp-hire-date">Ngày vào làm *</label><input id="emp-hire-date" type="date" required value={form.hireDate} onChange={(event) => update('hireDate', event.target.value)} /></div><div className="field-group form-grid-full"><label htmlFor="emp-manager">Quản lý trực tiếp</label><label className="select-wrap select-wrap-full"><select id="emp-manager" value={form.managerId ?? ''} onChange={(event) => update('managerId', event.target.value || null)}><option value="">Không có quản lý trực tiếp</option>{managers.map((manager) => <option key={manager.id} value={manager.id}>{manager.fullName} · {manager.departmentName}</option>)}</select><ChevronDown size={14} /></label><small className="field-help">Chỉ hiển thị quản lý đang hoạt động.</small></div>{!editing && <div className="field-group form-grid-full"><label htmlFor="emp-password">Mật khẩu tạm thời *</label><input id="emp-password" type="password" minLength={12} maxLength={30} required value={form.password ?? ''} onChange={(event) => update('password', event.target.value)} placeholder="Tối thiểu 12 ký tự" /><small className="field-help">Mật khẩu phải có từ 12 đến 30 ký tự.</small></div>}</div>
      <div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setModalOpen(false)}>Hủy</button><button type="submit" disabled={saving} className="button button-primary">{saving ? 'Đang lưu…' : editing ? 'Lưu thay đổi' : 'Tạo nhân viên'}</button></div>
    </form></Modal>}
  </div>;
}
