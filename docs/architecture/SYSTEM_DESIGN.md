# System design — trạng thái triển khai hiện tại

## 1. Phạm vi và runtime

Hệ thống gồm React + TypeScript + Nginx, hai ASP.NET Core/.NET 10 service, SQL Server với hai database tách quyền sở hữu và Redis làm cache best-effort cho HR catalog.

Không có API Gateway riêng. Nginx trong Web container proxy /hr-api/* tới HR Service và /leave-api/* tới Leave Service. Hai backend cũng được expose qua host port để local development/test.

Sơ đồ source:
- API architecture: diagrams/mermaid/api-architecture.mmd
- HR ERD: diagrams/mermaid/erd-hr-db.mmd
- Leave ERD: diagrams/mermaid/erd-leave-db.mmd
- Login sequence: diagrams/mermaid/seq-login.mmd
- Create leave sequence: diagrams/mermaid/seq-create-leave.mmd
- Approve/reject sequence: diagrams/mermaid/seq-approve-leave.mmd

Mermaid là source chỉnh sửa. PNG/SVG chỉ được coi là bản xuất đồng bộ khi đã render lại từ source mới nhất.

## 2. Trách nhiệm và quyền sở hữu dữ liệu

| Thành phần | Nguồn dữ liệu sở hữu | Trách nhiệm |
|---|---|---|
| React Web + Nginx | UI state/session | Màn hình, điều hướng, gọi API; proxy route tới hai service |
| HR Service | HR_DB | Authentication, refresh token, employee, department, reporting line, Roles/Permissions/UserRoles, leave types/policies/holidays |
| Leave Service | Leave_DB | Leave requests, balances, yearly allocations, request history, calendar và approval inbox |
| SQL Server | HR_DB, Leave_DB | Durable source of truth; mỗi service chỉ migrate/query database của mình |
| Redis | Cache HR catalog | Cache-aside cho leave types, policies, holidays; không phải nguồn quyết định nghiệp vụ |

Leave_DB lưu HR IDs như scalar references; không có cross-database foreign keys. Leave Service không dùng EF context hoặc connection tới HR_DB. Employee, Manager, leave type, policy và holiday data được lấy qua HR REST API.

## 3. Dynamic Authorization V1

HR Service sở hữu các bảng Roles, Permissions, UserRoles, RolePermissions, PermissionScopeRules, RolePermissionScopes và AuthzAuditLog.

AuthorizationService tổng hợp permission/scope từ các Role assignment còn hiệu lực. Employee, Role và Permission phải active; UserRole không bị revoke và chưa hết hạn. Các quyền được hợp nhất từ nhiều role; thiếu permission/scope hoặc claim không hợp lệ thì mặc định deny.

JWT chứa authz_v=1 và perm dạng JSON, ví dụ:

    [
      { "p": "leaves.read", "s": ["Self", "Team"] },
      { "p": "leaves.decide", "s": ["Team"] }
    ]

Login và refresh đều tính lại quyền từ HR_DB trước khi phát token. HR và Leave xác thực JWT cục bộ bằng cùng issuer/audience/signing key; request thông thường không introspect JWT qua HR. Role claim và Employee.Role vẫn tồn tại cho tương thích frontend/backfill, nhưng backend authorization của các controller đã chuyển đổi dựa trên Permission + Scope.

Các scope đang được thực thi trong API là Self, Team và All. Department tồn tại trong schema/rule constraint nhưng chưa được dùng làm bộ lọc dữ liệu. Role/Permission baseline được seed (Employee, Manager, HR, Auditor); API hiện tại quản lý UserRole assignment, chưa có CRUD tạo Role/Permission catalog hoặc chỉnh Role grants.

**Frontend chưa dynamic hoàn toàn:** App.tsx và AppShell vẫn dùng Role legacy (HR/Manager/Employee) để ẩn menu và guard route. Backend mới là nguồn quyết định bảo mật cuối cùng; ẩn route ở UI không thay thế API authorization. Chưa có UI quản trị Role assignments.

## 4. Luồng đăng nhập và refresh

1. Browser gửi credentials tới POST /api/v1/auth/login của HR.
2. HR kiểm tra Employee active và mật khẩu.
3. HR AuthorizationService lấy effective grants từ HR_DB.
4. JWT phát sub, identity claims, authz_v=1, perm và Role claim legacy cho UI.
5. Refresh token được đặt trong HttpOnly cookie; token hash được lưu trong HR_DB.
6. Khi refresh, HR xoay vòng refresh token và tính lại authorization; không sao chép perm từ JWT cũ.
7. Khi Role assignment thay đổi, JWT đã phát không bị cập nhật tại chỗ. Quyền mới có hiệu lực khi người dùng login/refresh nhận token mới. Access token cũ còn hiệu lực tới expiry; default cấu hình là 1 phút.

Deactivation thu hồi refresh token nhưng không thu hồi tức thời access JWT đã phát. Đây là giới hạn của xác thực access token stateless hiện tại.

## 5. HR/Leave authorization và Team scope

HR Employees endpoints kiểm tra permission + scope theo từng thao tác. HR write endpoints kiểm tra permission tương ứng với scope All. UserRole management yêu cầu roles.manage:All, đọc lại quyền người thực hiện từ HR_DB trước khi thao tác; chặn tự cấp quyền, privilege escalation và thu hồi quản trị viên cuối cùng; ghi audit cùng transaction.

Leave Service xây dựng LeaveAccess từ claim perm:
- leaves.read: Self/Team/All cho list/calendar/detail.
- leaves.create:Self cho tạo đơn.
- leaves.decide:Team/All cho inbox và approve/reject.
- leaves.cancel:Self cho hủy request của mình.

Team được xác định theo quan hệ quản lý hiện tại, không dựa vào ApproverId snapshot trên đơn:
- HR lookup cho employee/team cung cấp direct report IDs hiện tại.
- Leave Team IDs được lấy qua GET /api/v1/leave-scope/managers/{managerId}/employee-ids; endpoint chỉ cho chính manager đó gọi và yêu cầu leaves.read:Team hoặc leaves.decide:Team.
- Khi Manager quyết định một đơn Team, Leave Service gọi HR lấy employee snapshot để xác minh employee active và ManagerId vẫn là actor hiện tại.
- Khi scope All, không cần Team lookup.
- HR không khả dụng hoặc trả dependency error thì Leave fail closed; không thay đổi trạng thái/balance/history.

HR lookup hồ sơ nhân viên theo employees.read và lookup IDs để xác định Leave Team scope theo leaves.read/decide:Team là hai capability khác nhau.

## 6. Luồng tạo và xử lý Leave

### Tạo đơn

1. Leave Service xác minh JWT/permission.
2. Gọi HR authoritative lookup để kiểm tra Employee active và LeaveType active.
3. Lấy holiday list, tính ngày làm việc (weekday trừ ngày lễ).
4. Với loại deduct balance, lấy policy authoritative cho từng năm liên quan.
5. Chia request qua năm thành yearly allocations; ghi snapshot ApproverId/DepartmentId từ HR.
6. Trong SQL transaction, kiểm tra overlap, kiểm tra quota, reserve Pending, lưu request + allocations + history.
7. Trả response cùng balance rows liên quan.

Request có range tối đa 366 ngày; reason bắt buộc, tối đa 1.000 ký tự. Request không có working day bị từ chối.

### Approve / reject / cancel

- Chỉ request Pending được chuyển trạng thái.
- Approve: Pending giảm, Used tăng.
- Reject/Cancel: Pending giảm.
- History được append trong cùng luồng transaction.
- RowVersion, unique balance key, Serializable transaction và retry cho deadlock/concurrency bảo vệ balance/status.
- ApproverId chỉ là snapshot lúc tạo; Team authority được xác minh lại từ HR.

### Balance hiện tại

Balance key duy nhất là EmployeeId + LeaveTypeId + Year; entitlement/used/pending dùng decimal(6,1), có RowVersion. Balance row có thể được tạo lazy trong flow tạo request cho leave type trừ quota.

GET /api/v1/leave-balances/mine hiện chỉ đọc các rows tồn tại; không khởi tạo balance cho tất cả loại phép. Không có worker/event/outbox để đồng bộ entitlement sau khi Leave Policy thay đổi. Đây là gap P1 trong planning/BACKLOG.md.

### History hiện tại

Tạo, approve, reject, cancel ghi LeaveRequestHistory dạng append. Hiện chưa có GET history endpoint; UI chưa hiển thị timeline đầy đủ.

## 7. Redis và tính nhất quán

HR Service cache ba nhóm catalog:
- hr:catalog:leave-types
- hr:catalog:leave-policies
- hr:catalog:holidays

TTL chung mặc định 1.800 giây (30 phút), cấu hình Cache:ExpirationSeconds trong khoảng 1–3.600 giây. Khi cache miss, HR đọc SQL rồi thử ghi Redis. Ghi/xóa cache failure được log và không chặn luồng SQL. Nếu invalidation thất bại khi Redis outage, catalog thông thường có thể stale đến khi TTL hết.

Leave Service gọi HR với authoritative=true khi lấy leave types/policies/holidays cho nghiệp vụ, do đó bypass Redis. Balance, state và history không cache trong Redis.

## 8. Failure handling và vận hành

- HR Service không khả dụng khi Leave cần lookup: dependency failure được map thành HTTP 503 Problem Details. Không dùng ApproverId snapshot làm fallback cho Manager decision.
- HR HttpClient có timeout 5 giây; hiện chưa có circuit breaker hoặc application-level retry policy.
- Redis lỗi: HR catalog đọc fallback về SQL; Redis chỉ best-effort.
- SQL startup: HR áp dụng migrations, seed Initial HR/Demo nếu bật và Authorization baseline; có retry transient SQL khi khởi tạo. Leave áp dụng migrations lúc startup.
- Exception middleware trả Problem Details đã sanitize.
- /health hiện map generic ASP.NET health checks; chưa tách liveness/readiness hoặc health check cụ thể cho database/Redis.
- OpenAPI/Swagger chỉ bật ở Development.

## 9. Business rules về năm và timezone

HR Leave Policy dùng business calendar UTC+07:00; chỉ tạo/sửa policy của năm lớn hơn năm hiện tại theo UTC+07. Policy năm hiện tại/quá khứ read-only.

Trong khi đó, Leave Request validation hiện xác định "today" và year mặc định của Leave Balance bằng UTC. Ở thời điểm ranh giới ngày/năm tại Việt Nam có thể lệch một ngày/năm so với nghiệp vụ UTC+07. Đây là gap cần thống nhất timezone trong planning/BACKLOG.md.

## 10. Giới hạn và non-goals V1

Chưa có API Gateway riêng, message broker, outbox, asynchronous authorization read model, balance reconciliation worker, report/export backend, notification delivery, Role/Permission catalog CRUD, permission-driven UI, History read endpoint, hoặc readiness/liveness endpoints riêng.

Root Compose hiện phục vụ local/demo/benchmark; không phải production hardening. Trước production cần secret management, TLS, network restrictions, backups/restore, observability và quy trình migration/rollback.

## 11. Tài liệu liên quan

- Dynamic Authorization detail: AUTHORIZATION_DESIGN.md
- Database design: DATABASE_DESIGN.md
- API reference: ../API_REFERENCE.md
- Future backlog: ../planning/BACKLOG.md
- Validation/benchmark: ../testing/dynamic-authorization-validation.md
