import { useEffect, useState, type FormEvent } from 'react';
import { Building2, Edit3, Plus, Trash2, Users } from 'lucide-react';
import { PageHeader, Avatar, EmptyState } from '../components/ui';
import { Modal } from '../components/Modal';
import { useToast } from '../components/ToastProvider';
import { deleteDepartment, getDepartments, getEmployeePage, saveDepartment } from '../services/hrService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Department, Employee } from '../types';

export function DepartmentsPage() {
  const notify = useToast();
  const [departments, setDepartments] = useState<Department[]>([]);
  const [departmentMembers, setDepartmentMembers] = useState<Record<number, Employee[]>>({});
  const [departmentCounts, setDepartmentCounts] = useState<Record<number, number>>({});
  const [employeeSummary, setEmployeeSummary] = useState({ total: 0, active: 0 });
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Department | null>(null);
  const [name, setName] = useState('');
  const [saving, setSaving] = useState(false);

  async function reload() {
    setLoading(true);
    setError('');
    const departmentResult = await Promise.allSettled([getDepartments()]);
    if (departmentResult[0].status !== 'fulfilled') {
      setError(getApiErrorMessage(getRejectedReason(departmentResult[0])));
      setLoading(false);
      return;
    }

    const nextDepartments = departmentResult[0].value;
    setDepartments(nextDepartments);
    const requests = await Promise.allSettled([
      getEmployeePage({ page: 1, pageSize: 1 }),
      getEmployeePage({ page: 1, pageSize: 1, isActive: true }),
      ...nextDepartments.map((department) =>
        getEmployeePage({ page: 1, pageSize: 4, departmentId: department.id }),
      ),
    ]);

    if (requests[0].status === 'fulfilled') {
      setEmployeeSummary((current) => ({ ...current, total: requests[0].value.totalCount }));
    } else {
      setError(getApiErrorMessage(getRejectedReason(requests[0])));
    }
    if (requests[1].status === 'fulfilled') {
      setEmployeeSummary((current) => ({ ...current, active: requests[1].value.totalCount }));
    } else {
      setError((current) => current || getApiErrorMessage(getRejectedReason(requests[1])));
    }

    const members: Record<number, Employee[]> = {};
    const counts: Record<number, number> = {};
    nextDepartments.forEach((department, index) => {
      const result = requests[index + 2];
      if (result.status === 'fulfilled') {
        members[department.id] = result.value.items;
        counts[department.id] = result.value.totalCount;
      } else {
        members[department.id] = [];
        counts[department.id] = 0;
        setError((current) => current || getApiErrorMessage(getRejectedReason(result)));
      }
    });
    setDepartmentMembers(members);
    setDepartmentCounts(counts);
    setLoading(false);
  }

  useEffect(() => { void reload(); }, []);

  function openCreate() { setEditing(null); setName(''); setModalOpen(true); }
  function openEdit(department: Department) { setEditing(department); setName(department.name); setModalOpen(true); }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaving(true);
    try {
      await saveDepartment(name, editing?.id);
      notify(editing ? 'Đã cập nhật phòng ban.' : 'Đã tạo phòng ban.');
      setModalOpen(false);
      await reload();
    } catch (reason) {
      notify(getApiErrorMessage(reason), 'error');
    } finally {
      setSaving(false);
    }
  }

  async function onDelete(department: Department) {
    if (!window.confirm(`Bạn có chắc muốn xóa phòng ban “${department.name}” không?`)) return;
    try {
      await deleteDepartment(department.id);
      notify('Đã xóa phòng ban.');
      await reload();
    } catch (reason) {
      notify(getApiErrorMessage(reason), 'error');
    }
  }

  return <div className="page-stack">
    <PageHeader
      eyebrow="QUẢN TRỊ"
      title="Phòng ban"
      description="Sắp xếp nhân sự theo đội nhóm và quản lý cơ cấu báo cáo."
      actions={<button className="button button-primary" onClick={openCreate}><Plus size={17} /> Thêm phòng ban</button>}
    />
    <div className="page-summary-row">
      <div className="summary-metric"><span>Tổng số phòng ban</span><strong>{departments.length}</strong></div>
      <div className="summary-metric"><span>Tổng số nhân viên</span><strong>{employeeSummary.total}</strong></div>
      <div className="summary-metric"><span>Nhân viên đang hoạt động</span><strong>{employeeSummary.active}</strong></div>
    </div>
    {error && <div className="inline-alert alert-warning">{error}</div>}
    {loading ? (
      <div className="table-loading"><span /> Đang tải phòng ban…</div>
    ) : departments.length === 0 ? (
      <EmptyState title="Chưa có phòng ban" description="Tạo phòng ban đầu tiên để tổ chức nhân sự." action={<button className="button button-primary" onClick={openCreate}><Plus size={16} /> Thêm phòng ban</button>} />
    ) : (
      <div className="department-grid">
        {departments.map((department, index) => {
          const people = departmentMembers[department.id] ?? [];
          const count = departmentCounts[department.id] ?? 0;
          const colors = ['blue', 'violet', 'green', 'amber', 'rose'];
          const tone = colors[index % colors.length];
          return (
            <article className="panel department-card" key={department.id}>
              <div className="department-card-top">
                <span className={`department-icon department-${tone}`}><Building2 size={21} /></span>
                <button className="icon-button" aria-label={`Sửa ${department.name}`} onClick={() => openEdit(department)}><Edit3 size={16} /></button>
              </div>
              <h2>{department.name}</h2>
              <p>Mã phòng ban · {department.id.toString().padStart(3, '0')}</p>
              <div className="department-card-count"><span className="department-count-icon"><Users size={15} /></span><strong>{count}</strong><span>nhân viên</span></div>
              <div className="department-card-bottom">
                <div className="avatar-stack-small">
                  {people.map((person) => <Avatar key={person.id} name={person.fullName} size="sm" />)}
                  {count > people.length && <span className="avatar-overflow">+{count - people.length}</span>}
                </div>
                <button className="text-button danger-text" onClick={() => void onDelete(department)}><Trash2 size={15} /> Xóa</button>
              </div>
            </article>
          );
        })}
      </div>
    )}
    {modalOpen && (
      <Modal title={editing ? 'Sửa phòng ban' : 'Tạo phòng ban'} description="Tên phòng ban không được trùng." onClose={() => setModalOpen(false)}>
        <form className="modal-form" onSubmit={onSubmit}>
          <div className="field-group">
            <label htmlFor="department-name">Tên phòng ban *</label>
            <input id="department-name" required minLength={2} maxLength={100} value={name} onChange={(event) => setName(event.target.value)} placeholder="Ví dụ: Chăm sóc khách hàng" autoFocus />
          </div>
          <div className="modal-actions">
            <button type="button" className="button button-secondary" onClick={() => setModalOpen(false)}>Hủy</button>
            <button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Đang lưu…' : editing ? 'Lưu thay đổi' : 'Tạo phòng ban'}</button>
          </div>
        </form>
      </Modal>
    )}
  </div>;
}
