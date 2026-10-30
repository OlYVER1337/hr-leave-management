import { useEffect, useState, type FormEvent } from 'react';
import { Building2, Edit3, Plus, Trash2, Users } from 'lucide-react';
import { PageHeader, Avatar, EmptyState } from '../components/ui';
import { Modal } from '../components/Modal';
import { useToast } from '../components/ToastProvider';
import { deleteDepartment, getDepartments, getEmployees, saveDepartment } from '../services/hrService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Department, Employee } from '../types';

export function DepartmentsPage() {
  const notify = useToast();
  const [departments, setDepartments] = useState<Department[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Department | null>(null);
  const [name, setName] = useState('');
  const [saving, setSaving] = useState(false);

  async function reload() {
    setLoading(true); setError('');
    const result = await Promise.allSettled([getDepartments(), getEmployees()]);
    if (result[0].status === 'fulfilled') setDepartments(result[0].value); else setError(getApiErrorMessage(getRejectedReason(result[0])));
    if (result[1].status === 'fulfilled') setEmployees(result[1].value); else setError((current) => current || getApiErrorMessage(getRejectedReason(result[1])));
    setLoading(false);
  }
  useEffect(() => { void reload(); }, []);

  function openCreate() { setEditing(null); setName(''); setModalOpen(true); }
  function openEdit(department: Department) { setEditing(department); setName(department.name); setModalOpen(true); }
  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setSaving(true);
    try { await saveDepartment(name, editing?.id); notify(editing ? 'Đã cập nhật phòng ban.' : 'Đã tạo phòng ban.'); setModalOpen(false); await reload(); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
    finally { setSaving(false); }
  }
  async function onDelete(department: Department) {
    if (!window.confirm(`Bạn có chắc muốn xóa phòng ban “${department.name}” không?`)) return;
    try { await deleteDepartment(department.id); notify('Đã xóa phòng ban.'); await reload(); }
    catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
  }

  return <div className="page-stack">
    <PageHeader eyebrow="QUẢN TRỊ" title="Phòng ban" description="Sắp xếp nhân sự theo đội nhóm và quản lý cơ cấu báo cáo." actions={<button className="button button-primary" onClick={openCreate}><Plus size={17} /> Thêm phòng ban</button>} />
    <div className="page-summary-row"><div className="summary-metric"><span>Tổng số phòng ban</span><strong>{departments.length}</strong></div><div className="summary-metric"><span>Nhân viên đang hoạt động</span><strong>{employees.filter((item) => item.isActive).length}</strong></div><div className="summary-metric"><span>Nhân viên chưa phân phòng</span><strong>{employees.filter((item) => !item.departmentId).length}</strong></div></div>
    {error && <div className="inline-alert alert-warning">{error}</div>}
    {loading ? <div className="table-loading"><span /> Đang tải phòng ban…</div> : departments.length === 0 ? <EmptyState title="Chưa có phòng ban" description="Tạo phòng ban đầu tiên để tổ chức nhân sự." action={<button className="button button-primary" onClick={openCreate}><Plus size={16} /> Thêm phòng ban</button>} /> : <div className="department-grid">{departments.map((department, index) => {
      const people = employees.filter((employee) => employee.departmentId === department.id);
      const colors = ['blue', 'violet', 'green', 'amber', 'rose']; const tone = colors[index % colors.length];
      return <article className="panel department-card" key={department.id}><div className="department-card-top"><span className={`department-icon department-${tone}`}><Building2 size={21} /></span><button className="icon-button" aria-label={`Sửa ${department.name}`} onClick={() => openEdit(department)}><Edit3 size={16} /></button></div><h2>{department.name}</h2><p>Mã phòng ban · {department.id.toString().padStart(3, '0')}</p><div className="department-card-count"><span className="department-count-icon"><Users size={15} /></span><strong>{people.length}</strong><span>nhân viên</span></div><div className="department-card-bottom"><div className="avatar-stack-small">{people.slice(0, 4).map((person) => <Avatar key={person.id} name={person.fullName} size="sm" />)}{people.length > 4 && <span className="avatar-overflow">+{people.length - 4}</span>}</div><button className="text-button danger-text" onClick={() => void onDelete(department)}><Trash2 size={15} /> Xóa</button></div></article>;
    })}</div>}
    {modalOpen && <Modal title={editing ? 'Sửa phòng ban' : 'Tạo phòng ban'} description="Tên phòng ban không được trùng." onClose={() => setModalOpen(false)}><form className="modal-form" onSubmit={onSubmit}><div className="field-group"><label htmlFor="department-name">Tên phòng ban *</label><input id="department-name" required minLength={2} maxLength={100} value={name} onChange={(event) => setName(event.target.value)} placeholder="Ví dụ: Chăm sóc khách hàng" autoFocus /></div><div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setModalOpen(false)}>Hủy</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : editing ? 'Lưu thay đổi' : 'Tạo phòng ban'}</button></div></form></Modal>}
  </div>;
}
