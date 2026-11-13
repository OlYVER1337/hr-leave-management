import { useCallback, useEffect, useState, type FormEvent } from 'react';
import { ArrowDown, ArrowDownUp, ArrowUp, Building2, ChevronDown, Download, Edit3, Plus, Search, UserCheck, UserMinus, Users } from 'lucide-react';
import { Avatar, EmptyState, formatDate, PageHeader } from '../components/ui';
import { Modal } from '../components/Modal';
import { useToast } from '../components/ToastProvider';
import { deactivateEmployee, getDepartments, getEmployeePage, saveEmployee, type EmployeeInput, type EmployeePageQuery } from '../services/hrService';
import { getApiErrorMessage, getRejectedReason } from '../services/apiClient';
import type { Department, Employee, UserRole } from '../types';
import { Pagination } from '../components/Pagination';
import { usePagedBlock } from '../hooks/usePagedBlock';

const today = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
};
const blankForm = (): EmployeeInput => ({ fullName: '', email: '', password: '', role: 'Employee', hireDate: today(), departmentId: 0, managerId: null });
const pageSizeOptions = [10, 25, 50];
type EmployeeSortField = 'fullName' | 'code' | 'departmentName' | 'role' | 'managerName' | 'hireDate' | 'isActive';
type SortDirection = 'asc' | 'desc';

export function EmployeesPage() {
  const notify = useToast();
  const [departments, setDepartments] = useState<Department[]>([]);
  const [managerOptions, setManagerOptions] = useState<Employee[]>([]);
  const [summary, setSummary] = useState({ total: 0, active: 0, managers: 0 });
  const [refreshToken, setRefreshToken] = useState(0);
  const [auxiliaryLoading, setAuxiliaryLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [role, setRole] = useState<'All' | UserRole>('All');
  const [activeFilter, setActiveFilter] = useState<'All' | 'Active' | 'Inactive'>('All');
  const [departmentFilter, setDepartmentFilter] = useState('All');
  const [sortField, setSortField] = useState<EmployeeSortField>('fullName');
  const [sortDirection, setSortDirection] = useState<SortDirection>('asc');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(pageSizeOptions[0]);
  const [debouncedSearch, setDebouncedSearch] = useState('');
  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedSearch(search), 300);
    return () => window.clearTimeout(handle);
  }, [search]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Employee | null>(null);
  const [form, setForm] = useState<EmployeeInput>(blankForm());
  const [saving, setSaving] = useState(false);

  async function reloadAuxiliary() {
    setAuxiliaryLoading(true);
    setError('');
    const requests = await Promise.allSettled([
      getDepartments(),
      getEmployeePage({ page: 1, pageSize: 1 }),
      getEmployeePage({ page: 1, pageSize: 1, isActive: true }),
      getEmployeePage({ page: 1, pageSize: 100, role: 'Manager', isActive: true }),
    ]);
    if (requests[0].status === 'fulfilled') setDepartments(requests[0].value);
    else setError(getApiErrorMessage(getRejectedReason(requests[0])));
    if (requests[1].status === 'fulfilled') setSummary((value) => ({ ...value, total: requests[1].value.totalCount }));
    else setError((value) => value || getApiErrorMessage(getRejectedReason(requests[1])));
    if (requests[2].status === 'fulfilled') setSummary((value) => ({ ...value, active: requests[2].value.totalCount }));
    else setError((value) => value || getApiErrorMessage(getRejectedReason(requests[2])));
    if (requests[3].status === 'fulfilled') {
      setSummary((value) => ({ ...value, managers: requests[3].value.totalCount }));
      setManagerOptions(requests[3].value.items);
    } else {
      setError((value) => value || getApiErrorMessage(getRejectedReason(requests[3])));
    }
    setAuxiliaryLoading(false);
  }
  useEffect(() => { void reloadAuxiliary(); }, []);

  // Page/size are owned by usePagedBlock; this object only identifies the
  // filters and sort order that define a distinct result set.
  const query: Omit<EmployeePageQuery, 'page' | 'pageSize'> = {
    search: debouncedSearch || undefined,
    role: role === 'All' ? undefined : role,
    isActive: activeFilter === 'All' ? undefined : activeFilter === 'Active',
    departmentId: departmentFilter === 'All' ? undefined : Number(departmentFilter),
    sortBy: sortField,
    sortDirection,
  };
  const queryKey = JSON.stringify(query);
  const loadBlock = useCallback(
    (requestPage: number, requestPageSize: number) => getEmployeePage({
      ...query,
      page: requestPage,
      pageSize: requestPageSize,
    }),
    [queryKey],
  );
  const {
    items: blockEmployees,
    totalCount,
    hasTotalCount,
    loading: pageLoading,
    error: pageError,
  } = usePagedBlock<Employee>({
    page,
    pageSize,
    queryKey,
    refreshToken,
    load: loadBlock,
  });
  const pageCount = Math.max(1, Math.ceil(totalCount / pageSize));
  const currentPage = hasTotalCount ? Math.min(page, pageCount) : page;
  const pageEmployees = blockEmployees;
  const firstVisibleEmployee = totalCount === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const lastVisibleEmployee = Math.min(currentPage * pageSize, totalCount);
  useEffect(() => {
    if (hasTotalCount && page !== currentPage) setPage(currentPage);
  }, [hasTotalCount, page, currentPage]);

  function handleSort(field: EmployeeSortField) {
    setPage(1);
    if (sortField === field) {
      setSortDirection((current) => current === 'asc' ? 'desc' : 'asc');
      return;
    }
    setSortField(field);
    setSortDirection('asc');
  }

  function renderSortIcon(field: EmployeeSortField) {
    if (sortField !== field) return <ArrowDownUp size={13} aria-hidden="true" />;
    return sortDirection === 'asc'
      ? <ArrowUp size={13} aria-hidden="true" />
      : <ArrowDown size={13} aria-hidden="true" />;
  }

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
      notify(editing ? 'Đã cập nhật nhân viên.' : 'Đã tạo nhân viên.');
      setModalOpen(false);
      setPage(1);
      setRefreshToken((value) => value + 1);
      await reloadAuxiliary();
    } catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
    finally { setSaving(false); }
  }

  async function onDeactivate(employee: Employee) {
    if (!window.confirm(`Vô hiệu hóa tài khoản ${employee.fullName}? Nhân viên sẽ không thể đăng nhập nữa.`)) return;
    try {
      await deactivateEmployee(employee.id);
      notify('Đã vô hiệu hóa nhân viên.');
      setRefreshToken((value) => value + 1);
      await reloadAuxiliary();
    } catch (reason) { notify(getApiErrorMessage(reason), 'error'); }
  }

  const managers = managerOptions.filter((employee) => employee.id !== editing?.id);
  return <div className="page-stack">
    <PageHeader eyebrow="QUẢN TRỊ" title="Nhân viên" description="Quản lý hồ sơ nhân viên, phân công đội nhóm và quyền truy cập." actions={<><button className="button button-secondary" disabled title="Chưa khả dụng: API xuất danh sách chưa được triển khai"><Download size={16} /> Xuất danh sách (chưa khả dụng)</button><button className="button button-primary" onClick={openCreate}><Plus size={17} /> Thêm nhân viên</button></>} />
    <div className="page-summary-row"><div className="summary-metric"><span>Tổng số nhân viên</span><strong>{summary.total}</strong></div><div className="summary-metric"><span>Đang hoạt động</span><strong>{summary.active}</strong></div><div className="summary-metric"><span>Quản lý</span><strong>{summary.managers}</strong></div><div className="summary-metric"><span>Phòng ban</span><strong>{departments.length}</strong></div></div>
    {error && <div className="inline-alert alert-warning">{error}</div>}
    <section className="panel page-table-panel">
      <div className="table-toolbar">
        <div className="search-field">
          <Search size={17} />
          <input
            placeholder="Tìm tên, email, mã nhân viên…"
            aria-label="Tìm nhân viên"
            value={search}
            onChange={(event) => { setSearch(event.target.value); setPage(1); }}
          />
        </div>
        <div className="toolbar-filters">
          <label className="select-wrap">
            <Users size={15} />
            <select aria-label="Lọc theo vai trò" value={role} onChange={(event) => { setRole(event.target.value as 'All' | UserRole); setPage(1); }}>
              <option value="All">Tất cả vai trò</option>
              <option value="HR">Nhân sự</option>
              <option value="Manager">Quản lý</option>
              <option value="Employee">Nhân viên</option>
            </select>
            <ChevronDown size={14} />
          </label>
          <label className="select-wrap">
            <Building2 size={15} />
            <select aria-label="Lọc theo phòng ban" value={departmentFilter} onChange={(event) => { setDepartmentFilter(event.target.value); setPage(1); }}>
              <option value="All">Tất cả phòng ban</option>
              {departments.map((department) => (
                <option value={department.id} key={department.id}>{department.name}</option>
              ))}
            </select>
            <ChevronDown size={14} />
          </label>
          <label className="select-wrap">
            <select aria-label="Lọc theo trạng thái" value={activeFilter} onChange={(event) => { setActiveFilter(event.target.value as 'All' | 'Active' | 'Inactive'); setPage(1); }}>
              <option value="All">Tất cả trạng thái</option>
              <option value="Active">Đang hoạt động</option>
              <option value="Inactive">Đã vô hiệu hóa</option>
            </select>
            <ChevronDown size={14} />
          </label>
        </div>
      </div>
      {(pageLoading || auxiliaryLoading) ? <div className="table-loading"><span /> Đang tải nhân viên…</div> : pageError || error ? <div className="inline-alert alert-warning">{pageError || error}</div> : totalCount === 0 ? <EmptyState title="Không tìm thấy nhân viên" description="Thử từ khóa khác hoặc thêm nhân viên mới." /> : <div className="table-scroll"><table className="data-table"><thead>
            <tr>
              <th aria-sort={sortField === 'fullName' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" className="table-sort-button" aria-label="Sắp xếp theo họ tên" onClick={() => handleSort('fullName')}>Nhân viên {renderSortIcon('fullName')}</button>
              </th>
              <th aria-sort={sortField === 'code' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" className="table-sort-button" aria-label="Sắp xếp theo mã nhân viên" onClick={() => handleSort('code')}>Mã nhân viên {renderSortIcon('code')}</button>
              </th>
              <th aria-sort={sortField === 'departmentName' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" className="table-sort-button" aria-label="Sắp xếp theo phòng ban" onClick={() => handleSort('departmentName')}>Phòng ban {renderSortIcon('departmentName')}</button>
              </th>
              <th aria-sort={sortField === 'role' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" className="table-sort-button" aria-label="Sắp xếp theo vai trò" onClick={() => handleSort('role')}>Vai trò {renderSortIcon('role')}</button>
              </th>
              <th aria-sort={sortField === 'managerName' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" className="table-sort-button" aria-label="Sắp xếp theo quản lý" onClick={() => handleSort('managerName')}>Quản lý {renderSortIcon('managerName')}</button>
              </th>
              <th aria-sort={sortField === 'hireDate' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" className="table-sort-button" aria-label="Sắp xếp theo ngày vào làm" onClick={() => handleSort('hireDate')}>Ngày vào làm {renderSortIcon('hireDate')}</button>
              </th>
              <th aria-sort={sortField === 'isActive' ? (sortDirection === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" className="table-sort-button" aria-label="Sắp xếp theo trạng thái" onClick={() => handleSort('isActive')}>Trạng thái {renderSortIcon('isActive')}</button>
              </th>
              <th />
            </tr>
          </thead><tbody>{pageEmployees.map((employee) => <tr key={employee.id}><td><div className="person-cell"><Avatar name={employee.fullName} size="sm" /><div><strong>{employee.fullName}</strong><small>{employee.email}</small></div></div></td><td><span className="code-pill">{employee.code}</span></td><td>{employee.departmentName || '—'}</td><td><span className={`role-pill role-${employee.role.toLowerCase()}`}>{employee.role === 'HR' ? 'Nhân sự' : employee.role === 'Manager' ? 'Quản lý' : 'Nhân viên'}</span></td><td>{employee.managerName || <span className="muted-copy">Chưa phân công</span>}</td><td>{formatDate(employee.hireDate)}</td><td><span className={`employee-status ${employee.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{employee.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</span></td><td><div className="row-actions"><button className="icon-button" title="Sửa nhân viên" aria-label={`Sửa ${employee.fullName}`} onClick={() => openEdit(employee)}><Edit3 size={16} /></button>{employee.isActive && <button className="icon-button danger-icon" title="Vô hiệu hóa nhân viên" aria-label={`Vô hiệu hóa ${employee.fullName}`} onClick={() => void onDeactivate(employee)}><UserMinus size={16} /></button>}</div></td></tr>)}</tbody></table></div>}
      <div className="table-footer">
        <span aria-live="polite">Đang hiển thị <strong>{firstVisibleEmployee}–{lastVisibleEmployee}</strong> / <strong>{totalCount}</strong> nhân viên</span>
        <div className="employee-pagination">
          <label htmlFor="employee-page-size">Số dòng</label>
          <select id="employee-page-size" aria-label="Số nhân viên mỗi trang" value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1); }}>
            {pageSizeOptions.map((size) => <option value={size} key={size}>{size}</option>)}
          </select>
          <Pagination page={currentPage} pageCount={pageCount} onPageChange={setPage} />
        </div>
        <span className="table-footer-note"><UserCheck size={14} /> Hồ sơ nhân viên</span>
      </div>
    </section>

    {modalOpen && <Modal title={editing ? 'Sửa thông tin nhân viên' : 'Thêm nhân viên'} description={editing ? 'Cập nhật hồ sơ và tuyến báo cáo của nhân viên.' : 'Tạo tài khoản nhân viên mới trong tổ chức.'} onClose={() => setModalOpen(false)} size="lg"><form onSubmit={onSubmit} className="modal-form">
      <div className="form-grid-two"><div className="field-group form-grid-full"><label htmlFor="emp-code">Mã nhân viên</label><input id="emp-code" readOnly value={editing?.code ?? 'Tự động sinh sau khi tạo'} /><small className="field-help">Mã được hệ thống tạo tự động và không thể thay đổi.</small></div><div className="field-group"><label htmlFor="emp-name">Họ và tên *</label><input id="emp-name" required maxLength={150} value={form.fullName} onChange={(event) => update('fullName', event.target.value)} placeholder="Họ và tên" /></div><div className="field-group"><label htmlFor="emp-email">Email công việc *</label><input id="emp-email" type="email" required maxLength={200} value={form.email} onChange={(event) => update('email', event.target.value)} placeholder="ten@congty.com" /></div><div className="field-group"><label htmlFor="emp-role">Vai trò *</label><label className="select-wrap select-wrap-full"><select id="emp-role" required value={form.role} onChange={(event) => update('role', event.target.value as UserRole)}><option value="Employee">Nhân viên</option><option value="Manager">Quản lý</option><option value="HR">Nhân sự</option></select><ChevronDown size={14} /></label></div><div className="field-group"><label htmlFor="emp-department">Phòng ban *</label><label className="select-wrap select-wrap-full"><select id="emp-department" required value={form.departmentId || ''} onChange={(event) => update('departmentId', Number(event.target.value))}><option value="" disabled>Chọn phòng ban</option>{departments.map((department) => <option value={department.id} key={department.id}>{department.name}</option>)}</select><ChevronDown size={14} /></label></div><div className="field-group"><label htmlFor="emp-hire-date">Ngày vào làm *</label><input id="emp-hire-date" type="date" required value={form.hireDate} onChange={(event) => update('hireDate', event.target.value)} /></div><div className="field-group form-grid-full"><label htmlFor="emp-manager">Quản lý trực tiếp</label><label className="select-wrap select-wrap-full"><select id="emp-manager" value={form.managerId ?? ''} onChange={(event) => update('managerId', event.target.value || null)}><option value="">Không có quản lý trực tiếp</option>{managers.map((manager) => <option key={manager.id} value={manager.id}>{manager.fullName} · {manager.departmentName}</option>)}</select><ChevronDown size={14} /></label><small className="field-help">Chỉ hiển thị quản lý đang hoạt động.</small></div>{!editing && <div className="field-group form-grid-full"><label htmlFor="emp-password">Mật khẩu tạm thời *</label><input id="emp-password" type="password" minLength={12} maxLength={30} required value={form.password ?? ''} onChange={(event) => update('password', event.target.value)} placeholder="Tối thiểu 12 ký tự" /><small className="field-help">Mật khẩu phải có từ 12 đến 30 ký tự.</small></div>}</div>
      <div className="modal-actions"><button type="button" className="button button-secondary" onClick={() => setModalOpen(false)}>Hủy</button><button type="submit" disabled={saving} className="button button-primary">{saving ? 'Đang lưu…' : editing ? 'Lưu thay đổi' : 'Tạo nhân viên'}</button></div>
    </form></Modal>}
  </div>;
}
