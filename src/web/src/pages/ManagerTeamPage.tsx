import { useEffect, useMemo, useState } from 'react';
import { Eye, Search, Users } from 'lucide-react';
import { Avatar, EmptyState, formatDate, PageHeader } from '../components/ui';
import { Modal } from '../components/Modal';
import { useAuth } from '../contexts';
import { getMyTeam } from '../services/hrService';
import { getApiErrorMessage } from '../services/apiClient';
import type { Employee } from '../types';

export function ManagerTeamPage() {
  const { user } = useAuth();
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [selectedEmployee, setSelectedEmployee] = useState<Employee | null>(null);

  useEffect(() => {
    let active = true;
    async function loadTeam() {
      if (!user || user.role !== 'Manager') {
        setEmployees([]);
        setError('Chỉ quản lý mới có quyền xem danh sách nhân viên phụ trách.');
        setLoading(false);
        return;
      }

      setLoading(true);
      setError('');
      try {
        const team = await getMyTeam(user.id);
        if (active) setEmployees(team);
      } catch (reason) {
        if (active) setError(getApiErrorMessage(reason));
      } finally {
        if (active) setLoading(false);
      }
    }

    void loadTeam();
    return () => { active = false; };
  }, [user]);

  const filteredEmployees = useMemo(() => {
    const query = search.trim().toLocaleLowerCase('vi-VN');
    if (!query) return employees;
    return employees.filter((employee) =>
      [employee.fullName, employee.code, employee.email, employee.departmentName]
        .some((value) => value.toLocaleLowerCase('vi-VN').includes(query)),
    );
  }, [employees, search]);

  const departmentsCount = new Set(employees.map((employee) => employee.departmentName)).size;

  return <div className="page-stack">
    <PageHeader
      eyebrow="QUẢN LÝ ĐỘI NHÓM"
      title="Nhân viên phụ trách"
      description="Tra cứu hồ sơ những nhân viên được phân công trực tiếp cho bạn. Thông tin chỉ có quyền xem."
    />

    <div className="page-summary-row manager-team-summary">
      <div className="summary-metric"><span>Nhân viên đang phụ trách</span><strong>{employees.length}</strong></div>
      <div className="summary-metric"><span>Đang hoạt động</span><strong>{employees.filter((employee) => employee.isActive).length}</strong></div>
      <div className="summary-metric"><span>Phòng ban có nhân viên</span><strong>{departmentsCount}</strong></div>
    </div>

    {error && <div className="inline-alert alert-warning" role="alert">{error}</div>}

    <section className="panel page-table-panel">
      <div className="table-toolbar">
        <div className="search-field">
          <Search size={17} />
          <input
            placeholder="Tìm tên, email, mã nhân viên, phòng ban…"
            aria-label="Tìm nhân viên phụ trách"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
        </div>
        <span className="table-footer-note"><Users size={14} /> Chỉ nhân viên báo cáo trực tiếp</span>
      </div>

      {loading ? (
        <div className="table-loading"><span /> Đang tải danh sách nhân viên…</div>
      ) : error ? (
        <EmptyState
          title="Không thể tải danh sách nhân viên"
          description="Kiểm tra kết nối HR Service hoặc thử đăng nhập lại."
        />
      ) : filteredEmployees.length === 0 ? (
        <EmptyState
          title={employees.length === 0 ? 'Bạn chưa có nhân viên được phân công' : 'Không tìm thấy nhân viên'}
          description={employees.length === 0
            ? 'Khi HR phân công nhân viên cho bạn, hồ sơ của họ sẽ xuất hiện tại đây.'
            : 'Thử từ khóa khác để tìm trong danh sách nhân viên phụ trách.'}
        />
      ) : (
        <div className="table-scroll">
          <table className="data-table">
            <thead><tr>
              <th>Nhân viên</th><th>Mã nhân viên</th><th>Phòng ban</th>
              <th>Vai trò</th><th>Ngày vào làm</th><th>Trạng thái</th><th />
            </tr></thead>
            <tbody>
              {filteredEmployees.map((employee) => (
                <tr key={employee.id}>
                  <td><div className="person-cell"><Avatar name={employee.fullName} size="sm" /><div><strong>{employee.fullName}</strong><small>{employee.email}</small></div></div></td>
                  <td><span className="code-pill">{employee.code}</span></td>
                  <td>{employee.departmentName || '—'}</td>
                  <td><span className={`role-pill role-${employee.role.toLowerCase()}`}>{employee.role === 'Manager' ? 'Quản lý' : employee.role === 'HR' ? 'Nhân sự' : 'Nhân viên'}</span></td>
                  <td>{formatDate(employee.hireDate)}</td>
                  <td><span className={`employee-status ${employee.isActive ? 'employee-active' : 'employee-inactive'}`}><i />{employee.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</span></td>
                  <td><button type="button" className="icon-button" title="Xem hồ sơ nhân viên" aria-label={`Xem hồ sơ ${employee.fullName}`} onClick={() => setSelectedEmployee(employee)}><Eye size={16} /></button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {!loading && employees.length > 0 && (
        <div className="table-footer">
          <span aria-live="polite">Đang hiển thị <strong>{filteredEmployees.length}</strong> / <strong>{employees.length}</strong> nhân viên</span>
          <span className="table-footer-note"><Users size={14} /> Danh sách chỉ đọc</span>
        </div>
      )}
    </section>

    {selectedEmployee && (
      <Modal
        title="Thông tin nhân viên"
        description="Hồ sơ nhân viên thuộc nhóm bạn đang phụ trách."
        onClose={() => setSelectedEmployee(null)}
        size="lg"
      >
        <div className="request-details">
          <div><span>Họ và tên</span><strong>{selectedEmployee.fullName}</strong></div>
          <div><span>Mã nhân viên</span><strong>{selectedEmployee.code}</strong></div>
          <div><span>Email công việc</span><strong>{selectedEmployee.email}</strong></div>
          <div><span>Vai trò</span><strong>{selectedEmployee.role === 'Manager' ? 'Quản lý' : selectedEmployee.role === 'HR' ? 'Nhân sự' : 'Nhân viên'}</strong></div>
          <div><span>Phòng ban</span><strong>{selectedEmployee.departmentName || '—'}</strong></div>
          <div><span>Ngày vào làm</span><strong>{formatDate(selectedEmployee.hireDate)}</strong></div>
          <div><span>Quản lý trực tiếp</span><strong>{selectedEmployee.managerName || user?.fullName || '—'}</strong></div>
          <div><span>Trạng thái</span><strong>{selectedEmployee.isActive ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</strong></div>
        </div>
        <div className="modal-actions" style={{ padding: '16px 22px 20px' }}>
          <button type="button" className="button button-primary" onClick={() => setSelectedEmployee(null)}>Đóng</button>
        </div>
      </Modal>
    )}
  </div>;
}
