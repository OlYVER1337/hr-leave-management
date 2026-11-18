# Tài liệu tham chiếu API

## 1. Base URL, xác thực và quy ước chung

Khi chạy Docker Compose local:

| Service | Base URL | Qua Web/Nginx |
|---|---|---|
| HR Service | http://localhost:8081 | /hr-api tại http://localhost:3000 |
| Leave Service | http://localhost:8082 | /leave-api tại http://localhost:3000 |
| Web | http://localhost:3000 | — |

OpenAPI JSON và Swagger UI chỉ được map khi service chạy ở Development: HR và Leave đều dùng /openapi/v1.json và /swagger.

Các route bảo vệ nhận:

    Authorization: Bearer <access-token>

HR Service phát access JWT và đặt refresh token trong HttpOnly cookie. Refresh token không được trả về JSON. JWT hiện có:
- sub: EmployeeId
- authz_v=1: phiên bản cấu trúc authorization claim
- perm: mảng JSON permission và scope hiệu lực, ví dụ [{"p":"leaves.read","s":["Self","Team"]}]
- Role claim legacy vẫn tồn tại để tương thích giao diện; các endpoint đã chuyển đổi authorize bằng permission/scope, không dựa vào Role claim.

Scope đang thực thi: Self, Team, All. Department có trong schema/rule catalog nhưng chưa được triển khai thành bộ lọc nghiệp vụ. Quyền hiệu lực là hợp quyền từ các Role assignment còn hiệu lực; thiếu permission/scope hoặc claim không hợp lệ thì mặc định deny.

Danh sách phân trang:
- Phải truyền cả page và pageSize.
- page >= 1; pageSize từ 1 đến 100.
- Response trả item ở body và tổng số trong X-Total-Count.
- List endpoint không có page/pageSize trả HTTP 400 theo contract hiện tại.

## 2. HR Service

### 2.1. Authentication và hồ sơ hiện tại

| Method / route | Authorization | Mô tả |
|---|---|---|
| POST /api/v1/auth/login | Public | Xác thực email/password; phát access token và refresh HttpOnly cookie |
| POST /api/v1/auth/refresh | Refresh cookie | Xoay vòng refresh token và phát access JWT mới với quyền tính lại |
| POST /api/v1/auth/logout | Refresh cookie | Thu hồi refresh token và xóa cookie |
| GET /api/v1/me | Authenticated | Đọc hồ sơ của caller |
| GET /api/v1/me/team?page=1&pageSize=20 | employees.read:Team | Direct reports đang hoạt động của caller; pagination + X-Total-Count |
| PUT /api/v1/me/password | Authenticated | Đổi mật khẩu của caller |

### 2.2. Employee và lookup

| Method / route | Permission/scope | Mô tả |
|---|---|---|
| GET /api/v1/employees?page=1&pageSize=20 | employees.read:Self/Team/All | Danh sách trong phạm vi quyền; hỗ trợ filter/search/sort |
| GET /api/v1/employees/overview?page=1&pageSize=20 | employees.read:Self/Team/All | Trang dữ liệu, totalCount và summary |
| GET /api/v1/employees/{id} | employees.read:Self/Team/All | Hồ sơ trong phạm vi được phép |
| POST /api/v1/employees | employees.create:All | Tạo nhân viên; mã được backend sinh |
| PUT /api/v1/employees/{id} | employees.update:All | Cập nhật hồ sơ |
| POST /api/v1/employees/{id}/deactivate | employees.deactivate:All | Vô hiệu hóa nhân viên và thu hồi refresh token |
| PUT /api/v1/employees/{id}/password | employees.password.reset:All | Đặt lại mật khẩu nhân viên |
| POST /api/v1/lookup/employees/batch | employees.read:Self/Team/All | Lookup tối đa 100 ID; service kiểm tra scope từng record |
| GET /api/v1/lookup/employees/{id} | employees.read:Self/Team/All | Snapshot nhân viên đơn lẻ cho HR/Leave |

Danh sách direct reports của Manager chỉ truy cập qua `GET /api/v1/me/team`; endpoint tự lấy manager ID từ caller, yêu cầu `employees.read:Team` và hỗ trợ pagination.

### 2.3. Quản lý Role assignment

Các route sau yêu cầu roles.manage:All:

| Method / route | Mô tả |
|---|---|
| GET /api/v1/roles | Danh sách Role active |
| GET /api/v1/employees/{employeeId}/roles | Role assignments còn hiệu lực của nhân viên |
| POST /api/v1/employees/{employeeId}/roles | Gán Role; body có roleCode và expiresAtUtc tùy chọn |
| DELETE /api/v1/employees/{employeeId}/roles/{roleCode} | Thu hồi Role assignment |

Ví dụ body:

    {
      "roleCode": "Manager",
      "expiresAtUtc": null
    }

Backend đọc lại quyền người cấp từ HR_DB trong transaction. Không được tự gán cho chính mình; nhân viên đích phải active khi gán; người cấp không thể cấp permission/scope vượt quyền của mình; Role có roles.manage:All không được đặt thời hạn; hệ thống bảo vệ quản trị viên cuối cùng. Thay đổi được ghi AuthzAuditLog cùng transaction.

Giới hạn: API hiện chỉ quản lý assignment cho danh mục Role được seed. Chưa có endpoint tạo Role mới hoặc sửa RolePermissions/RolePermissionScopes; chưa có React UI quản trị Role assignment.

### 2.4. Department và leave catalog

| Method / route | Permission/scope | Mô tả |
|---|---|---|
| GET /api/v1/departments[/{id}] | Authenticated | Đọc phòng ban |
| POST /api/v1/departments | departments.create:All | Tạo phòng ban |
| PUT /api/v1/departments/{id} | departments.update:All | Cập nhật phòng ban |
| DELETE /api/v1/departments/{id} | departments.delete:All | Xóa theo điều kiện nghiệp vụ |
| GET /api/v1/leave-catalog?year=2027[&authoritative=true] | Authenticated | Trả leaveTypes và leavePolicies; authoritative=true bypass Redis và đọc SQL trực tiếp |
| GET /api/v1/leave-catalog?includeInactive=true | leave-types.update:All bổ sung | Bao gồm leave type inactive |
| GET /api/v1/leave-types[/{id}] | Authenticated | Đọc loại nghỉ; includeInactive=true cần leave-types.update:All |
| POST /api/v1/leave-types | leave-types.create:All | Tạo/khôi phục loại nghỉ chuẩn |
| PUT /api/v1/leave-types/{id} | leave-types.update:All | Cập nhật trạng thái active của loại nghỉ chuẩn |
| GET /api/v1/leave-policies[/{id}] | Authenticated | Đọc các phiên bản; GET list hỗ trợ year, asOfDate và authoritative=true |
| POST /api/v1/leave-policies | leave-policies.create:All | Tạo phiên bản policy đầu tiên cho loại nghỉ chuẩn trừ balance |
| PUT /api/v1/leave-policies/{id} | leave-policies.update:All | Thêm phiên bản policy mới; không sửa phiên bản cũ |
| GET /api/v1/holidays[/{id}] | Authenticated | Đọc ngày lễ |
| POST /api/v1/holidays | holidays.create:All | Thêm ngày lễ |
| PUT /api/v1/holidays/{id} | holidays.update:All | Cập nhật ngày lễ |
| DELETE /api/v1/holidays/{id} | holidays.delete:All | Xóa ngày lễ |

Ba loại nghỉ chuẩn là ANNUAL, SICK, UNPAID. Không cho phép loại nghỉ tùy ý; UNPAID không có quota policy. authoritative=true bypass Redis và đọc SQL trực tiếp. Leave Service dùng authoritative read khi tính nghiệp vụ.

Leave Policy được lưu theo phiên bản bất biến, mỗi bản ghi có EffectiveFrom, CreatedAtUtc và Reason. POST tạo phiên bản đầu tiên; PUT thêm một phiên bản mới cho cùng loại nghỉ/năm và không sửa bản ghi cũ. Chỉ năm tương lai mới được tạo/thêm phiên bản; năm hiện tại và quá khứ bị khóa. EffectiveFrom là ngày nghiệp vụ UTC+07:00 không được nằm trong quá khứ; phiên bản policy năm tương lai có thể có hiệu lực từ một ngày trong năm hiện tại. GET hỗ trợ asOfDate để lấy trực tiếp từ HR phiên bản mới nhất có EffectiveFrom không sau ngày chỉ định; có asOfDate thì endpoint tự dùng đường đọc authoritative kể cả khi không truyền authoritative=true.

## 3. Leave Service

| Method / route | Permission/scope | Mô tả |
|---|---|---|
| GET /api/v1/leave-requests?page=1&pageSize=20 | leaves.read:Self/Team/All | List phân trang, search, status, leaveTypeId |
| GET /api/v1/leave-requests/calendar?from=YYYY-MM-DD&to=YYYY-MM-DD | leaves.read:Self/Team/All | Calendar; cần đủ from/to, tối đa 62 ngày |
| GET /api/v1/leave-requests/{id} | leaves.read:Self/Team/All | Request trong scope; ngoài scope trả 404 |
| GET /api/v1/leave-requests/mine?page=1&pageSize=20 | leaves.read:Self/All | Chỉ request của caller |
| POST /api/v1/leave-requests | leaves.create:Self | Tạo request cho caller |
| GET /api/v1/leave-requests/pending-approval?page=1&pageSize=20 | leaves.decide:Team/All | Inbox; Team lấy từ reporting line hiện tại ở HR |
| GET /api/v1/leave-requests/summary | leaves.decide:Team/All | Pending theo decide scope; Approved/Total theo read scope, null nếu thiếu leaves.read |
| PATCH /api/v1/leave-requests/{id}/decision | leaves.decide:Team/All | decision=approve/reject và note tùy chọn |
| POST /api/v1/leave-requests/{id}/cancel | leaves.cancel:Self | Chủ đơn hủy khi Pending |
| GET /api/v1/leave-balances/mine?year=2026 | leaves.read:Self/All | Đồng bộ PolicyDays hiện hành từ HR rồi trả balance của caller |
| POST /api/v1/leave-balances/mine/synchronize?year=2026 | leaves.read:Self/All | Khởi tạo/đồng bộ balance của caller; vẫn chỉ tác động dữ liệu của chính caller |

Ví dụ quyết định:

    PATCH /api/v1/leave-requests/{id}/decision
    Authorization: Bearer <access-token>
    Content-Type: application/json

    {
      "decision": "approve",
      "note": "Đã sắp xếp nhân sự thay ca."
    }

Trạng thái LeaveRequestStatus: 0 Pending, 1 Approved, 2 Rejected, 3 Cancelled.

### 3.1. Quy tắc nghiệp vụ

- Employee phải active; leave type phải active.
- GET balance và POST synchronize đều lấy catalog authoritative từ HR qua HTTP trước khi đọc/cập nhật số dư; Leave Service không truy cập HR_DB trực tiếp.
- Balance lưu `PolicyDays`, `Used`, `Pending`; `Entitled = max(PolicyDays, Used + Pending)`, `Available = Entitled - Used - Pending`. Khi HR hạ quota xuống thấp hơn tổng Used + Pending, PolicyDays vẫn được cập nhật; Entitled tạm thời bằng Used + Pending để không tạo số dư âm.
- Luồng tạo đơn chọn policy version có EffectiveFrom mới nhất nhưng không sau ngày tạo đơn UTC+07:00. Mỗi yearly allocation lưu PolicyVersionId và PolicyDaysAtCreation để truy vết; request cũ không bị đổi policy đã áp dụng.
- Khi duyệt, từ chối hoặc hủy đơn, Leave lấy policy hiện hành từ HR và cập nhật PolicyDays trong cùng transaction với Used/Pending. Nếu HR unavailable, thao tác cần refresh fail closed.
- EndDate không trước StartDate; range tối đa 366 ngày; reason bắt buộc, tối đa 1.000 ký tự.
- Backend tính ngày làm việc: thứ Hai–thứ Sáu, bỏ holidays từ HR; request không có ngày làm việc bị từ chối.
- Request trừ balance được chia allocation theo từng năm; mỗi allocation lưu ID phiên bản policy được áp dụng lúc tạo đơn.
- Không cho request Pending/Approved chồng ngày của cùng nhân viên.
- Create reserve Pending; Approve chuyển Pending sang Used; Reject/Cancel giải phóng Pending.
- Create/decision/cancel và balance/history cùng transaction; RowVersion, unique index, Serializable transaction và retry bảo vệ cạnh tranh.
- ApproverId trên request chỉ là snapshot lúc tạo, không phải nguồn authorization hiện tại.
- Với Team scope, Leave gọi HR để lấy membership hiện tại; khi quyết định đơn, xác minh Employee còn active và ManagerId vẫn là actor. HR unavailable thì fail closed, trả 503.
- Leave Service không truy cập HR_DB trực tiếp.

Balance row được tạo khi nhân viên gọi POST `/api/v1/leave-balances/mine/synchronize`, GET `/api/v1/leave-balances/mine` hoặc tạo request thuộc leave type trừ quota. GET và POST đều đồng bộ PolicyDays từ HR authoritative trước khi trả số dư; CreateAsync, decision và cancel refresh quota trong transaction cập nhật balance.

History ghi Created/Approved/Rejected/Cancelled nhưng hiện chưa có GET API riêng để đọc history.

## 4. Lỗi HTTP

| Status | Ý nghĩa thường gặp |
|---:|---|
| 400 | Binding, pagination hoặc request format không hợp lệ |
| 401 | Thiếu/sai/hết hạn token hoặc credentials không hợp lệ |
| 403 | Thiếu permission/scope hoặc không thỏa authorization |
| 404 | Không tồn tại hoặc record ngoài data scope |
| 409 | Conflict trạng thái/unique/concurrency/nghiệp vụ |
| 422 | Validation nghiệp vụ thất bại |
| 503 | HR Service dependency không khả dụng |
| 500 | Lỗi không mong muốn; chi tiết chỉ có trong server log |

Application errors dùng Problem Details (application/problem+json). Client nên xử lý theo HTTP status và validation fields, không phụ thuộc title/detail như error code.

## 5. Chức năng chưa có

- GET Leave Request History.
- Export/report backend riêng; KPI hiện tổng hợp từ các API hiện có.
- Notification API/backend delivery.
- Role/Permission catalog CRUD và React UI quản trị grants; hiện chỉ có baseline seeds và UserRole assignment APIs.
- API/worker đồng bộ tất cả LeaveBalance khi policy thay đổi.
