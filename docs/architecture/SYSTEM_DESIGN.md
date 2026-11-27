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
- Balance self-sync sequence: diagrams/mermaid/seq-balance-sync.mmd
- Policy version lifecycle: diagrams/mermaid/seq-policy-version.mmd
- Employee cancel flow: diagrams/mermaid/seq-cancel-leave.mmd

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

Login và refresh đều tính lại quyền từ HR_DB trước khi phát token. HR và Leave xác thực JWT cục bộ bằng cùng issuer/audience/signing key; request thông thường không introspect JWT qua HR. Role claim và `Employee.Role` vẫn tồn tại cho tương thích dữ liệu, nhãn hiển thị, lọc/thống kê nhân viên và một số nghiệp vụ legacy; chúng không phải nguồn xác định effective permission. Backend authorization của các endpoint đã chuyển đổi dựa trên Permission + Scope.

Các scope đang được thực thi trong API là Self, Team và All. Department tồn tại trong schema/rule constraint nhưng chưa được dùng làm bộ lọc dữ liệu. Role/Permission baseline được seed (Employee, Manager, HR, Auditor); API hiện tại quản lý UserRole assignment, chưa có CRUD tạo Role/Permission catalog hoặc chỉnh Role grants.

`Frontend permission-driven` đã được merge vào `master` qua PR #18 (merge commit `d69156aa8990e2d92d21ad0dac1f9e0a7e1d2c5e`). `authorization.ts` đọc claim `authz_v=1` và `perm`, hợp nhất grant trùng permission, chỉ nhận scope `Self/Team/All`, và fail closed khi claim/token không hợp lệ. `App.tsx` bảo vệ route; `AppShell.tsx` lọc menu; các trang ẩn từng action theo permission/scope. Login, restore session và refresh đều dựng/cập nhật permission trong AuthContext. Route/menu phê duyệt yêu cầu đồng thời `leaves.decide` và `leaves.read` với phạm vi tương thích vì inbox và trang chi tiết dùng hai policy backend khác nhau. Legacy Role chỉ còn cho nhãn hiển thị/tương thích; không quyết định menu, route hoặc action. Browser không xác thực chữ ký JWT; API backend vẫn là enforcement cuối cùng. Chưa có UI quản trị UserRole assignment hoặc CRUD Role/Permission catalog.

Trên nhánh `feature/consolidate-employees-team-api`, trang `/my-team` vẫn được giữ làm màn hình Manager read-only nhưng dùng chung `GET /api/v1/employees?directReportsOnly=true` với trang nhân viên. Backend lấy actor ID từ JWT, bắt buộc `employees.read:Team` cho filter direct reports, chỉ trả direct report active theo `ManagerId` hiện tại và dùng chung search/filter/sort/pagination. Endpoint cũ `GET /api/v1/me/team` đã bị loại khỏi controller/runtime/OpenAPI; thay đổi hợp nhất API chưa merge vào `master`.

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
- leaves.decide:Team/All cho inbox và approve/reject. Endpoint summary cũng dùng decide scope (policy `LeavesSummary` ánh xạ tới `leaves.decide:Team/All`).
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
4. Với loại deduct balance, lấy toàn bộ policy version authoritative cho từng năm liên quan; chọn phiên bản mới nhất có `EffectiveFrom <= ngày tạo đơn` theo UTC+07.
5. Chia request qua năm thành yearly allocations; mỗi allocation lưu `PolicyVersionId` và `PolicyDaysAtCreation`, đồng thời ghi snapshot ApproverId/DepartmentId từ HR.
6. Trong transaction `SERIALIZABLE`, kiểm tra overlap Pending/Approved; tạo hoặc đọc balance, refresh `PolicyDays` từ policy version lấy được, tính `Entitled = max(PolicyDays, Used + Pending)`, kiểm tra Available và tăng Pending.
7. Cùng transaction lưu LeaveRequest, YearAllocations và LeaveRequestHistory (Created), sau đó commit và trả balance rows liên quan.

Request có range tối đa 366 ngày; reason bắt buộc, tối đa 1.000 ký tự. Request không có working day bị từ chối.

### Approve / reject

- Inbox yêu cầu `leaves.decide:Team/All` và phân trang `page/pageSize`. Team scope lấy direct report IDs hiện tại từ HR; All scope không cần Team lookup.
- Chỉ request Pending được quyết định; self-approval bị chặn.
- Với Team scope, Leave gọi HR lookup employee và xác minh employee còn active, `ManagerId` hiện tại bằng actor. Không dùng `ApproverId` snapshot làm authority.
- Trước transition, Leave lấy policy hiện hành từ HR theo từng năm trong allocation. Trong transaction `SERIALIZABLE`, refresh `PolicyDays`, giảm Pending; Approve tăng Used và set Approved, Reject chỉ set Rejected. History được append trong cùng transaction.

### Cancel

- `leaves.cancel:Self`; request phải thuộc caller và đang Pending.
- Leave lấy policy hiện hành từ HR theo từng năm allocation, sau đó trong transaction `SERIALIZABLE` refresh `PolicyDays`, giảm Pending, giữ nguyên Used, set Cancelled và append history.
- HR policy lookup lỗi thì fail closed; không đổi trạng thái/balance/history.

RowVersion, unique balance key, Serializable transaction và retry cho deadlock/concurrency bảo vệ balance/status. `ApproverId` được khởi tạo từ ManagerId khi tạo đơn; với quyết định Team, Leave ghi lại ManagerId hiện tại đã được HR xác minh trong transaction chuyển trạng thái. Trường này phục vụ lưu dấu approver, không phải nguồn xác định quyền Team.

### Balance hiện tại

Balance key duy nhất là EmployeeId + LeaveTypeId + Year; các cột lưu là `PolicyDays`, `Used`, `Pending` (decimal(6,1)), có RowVersion và CHECK constraint ngăn bộ đếm âm. `Entitled = max(PolicyDays, Used + Pending)`; `Available = Entitled - Used - Pending`. Balance row được tạo lazy khi tạo đơn trừ quota hoặc khi tự đồng bộ theo employee/năm.

GET `/api/v1/leave-balances/mine` và POST `/api/v1/leave-balances/mine/synchronize` đều gọi HR catalog với `authoritative=true`, sau đó cập nhật `PolicyDays` trong transaction `SERIALIZABLE` trước khi trả số dư. Hai endpoint chỉ đồng bộ EmployeeId lấy từ JWT, kể cả caller có scope All; nếu thiếu year thì dùng business year UTC+07. Chỉ leave type active có `DeductsBalance=true` và có policy hợp lệ mới được đồng bộ; loại thiếu policy được bỏ qua và trả tên/code qua header `X-Leave-Policy-Missing`. Luồng đọc có side effect đồng bộ idempotent; chưa có HR bulk reconciliation/worker cho toàn bộ nhân viên. Khi quota giảm dưới Used + Pending, hệ thống vẫn cập nhật PolicyDays; Entitled tạm thời bằng Used + Pending, Available bằng 0. Khi Pending được giải phóng sau Reject/Cancel, Entitled tự tính lại theo PolicyDays và lượng Used + Pending còn lại. Luồng tạo đơn snapshot policy version/quota lúc tạo; Approve/Reject/Cancel refresh PolicyDays hiện hành từ HR trong cùng transaction cập nhật Used/Pending.

### History hiện tại

Tạo, approve, reject, cancel ghi LeaveRequestHistory dạng append. Hiện chưa có GET history endpoint; UI chưa hiển thị timeline đầy đủ.

## 7. Redis và tính nhất quán

HR Service cache ba nhóm catalog:
- hr:catalog:leave-types
- hr:catalog:leave-policies
- hr:catalog:holidays

TTL chung mặc định 1.800 giây (30 phút), cấu hình `Cache:ExpirationSeconds` trong khoảng 1–3.600 giây. Khi cache miss, HR đọc SQL rồi thử ghi Redis. Khi cập nhật/xóa catalog, HR cố gắng invalidate key tương ứng. Ghi/xóa cache failure được log và không chặn luồng SQL; nếu invalidation thất bại khi Redis outage, catalog thông thường có thể stale đến khi TTL hết.

Leave Service gọi HR với `authoritative=true` khi lấy leave types/policies/holidays cho nghiệp vụ, do đó bypass Redis. Khi HR xóa policy version, HR còn gọi Leave GET `/api/v1/leave-policy-usage/{policyVersionId}` để đếm allocation tham chiếu trước khi xóa; Leave không khả dụng thì HR fail closed. Balance, state và history không cache trong Redis.

## 8. Failure handling và vận hành

- HR Service không khả dụng khi Leave cần lookup: dependency failure được map thành HTTP 503 Problem Details. Không dùng ApproverId snapshot làm fallback cho Manager decision.
- HR HttpClient có timeout 5 giây; hiện chưa có circuit breaker hoặc application-level retry policy.
- Redis lỗi: HR catalog đọc fallback về SQL; Redis chỉ best-effort.
- SQL startup: HR áp dụng migrations, seed Initial HR/Demo nếu bật và Authorization baseline; có retry transient SQL khi khởi tạo. Leave áp dụng migrations lúc startup.
- Exception middleware trả Problem Details đã sanitize.
- /health hiện map generic ASP.NET health checks; chưa tách liveness/readiness hoặc health check cụ thể cho database/Redis.
- OpenAPI/Swagger chỉ bật ở Development.

## 9. Business rules về năm và timezone

HR Leave Policy dùng business calendar UTC+07:00. Policy chỉ được tạo version cho năm hiện tại hoặc tương lai; `EffectiveFrom` không được trước ngày hiện tại (hoặc 01/01 của policy year nếu ngày hiện tại còn trước mốc đó) và không vượt khỏi năm policy. PUT policy tạo version mới. PUT `/edit` chỉ sửa version còn ở tương lai (`EffectiveFrom > business today`) và giữ nguyên năm, loại nghỉ, ngày hiệu lực. DELETE chỉ cho version tương lai và phải xác nhận Leave không có allocation tham chiếu. Policy version đã effective là historical/current và không được edit/delete.

Leave Request validation, default year của Leave Balance và business date/year trong `leaveService.ts` đều dùng UTC+07 (`Asia/Ho_Chi_Minh`). Cần duy trì kiểm thử ở ranh giới 31/12–01/01 và rà soát các màn hình frontend khác để không dùng browser-local time cho nghiệp vụ.

## 10. Giới hạn và non-goals V1

Chưa có API Gateway riêng, message broker, outbox, asynchronous authorization read model, balance reconciliation worker, report/export backend, notification delivery, Role/Permission catalog CRUD, UI quản trị UserRole assignment, History read endpoint, hoặc readiness/liveness endpoints riêng.

Root Compose hiện phục vụ local/demo/benchmark; không phải production hardening. Trước production cần secret management, TLS, network restrictions, backups/restore, observability và quy trình migration/rollback.

## 11. Tài liệu liên quan

- Dynamic Authorization detail: AUTHORIZATION_DESIGN.md
- Database design: DATABASE_DESIGN.md
- API reference: ../API_REFERENCE.md
- Future backlog: ../planning/BACKLOG.md
- Validation/benchmark: ../testing/dynamic-authorization-validation.md

## 12. Đối chiếu implementation trên nhánh benchmark/approval-constant-vus — 25/11/2026

Phần này đối chiếu các luồng trong tài liệu và Mermaid với source hiện tại trên head PR #13 (`61f22ad0fb264241744f56e4fb1a2c969d3d3046`). Các file Mermaid trong `diagrams/mermaid/` là source sơ đồ; EF Core DbContext, entity configuration và migrations mới là nguồn quyết định schema.

| Luồng / thành phần | Implementation được đối chiếu | Quy tắc thực tế cần giữ trong sơ đồ |
|---|---|---|
| JWT và permission | `HrService.Application/Services/AuthorizationService.cs`, `HrService.Infrastructure/Authentication/JwtTokenGenerator.cs`, `HrService.Api/Authorization/PermissionAuthorization.cs`, `LeaveService.Api/Authorization/LeavePermissionAuthorization.cs` | HR tính lại effective grants khi login/refresh; JWT có `authz_v=1` và `perm`; role claim cũ chỉ phục vụ tương thích/UI; hai service kiểm tra JWT cục bộ. |
| HR schema | `HrService.Infrastructure/Persistence/HrDbContext.cs` và HR migrations | Dynamic RBAC gồm Roles, Permissions, UserRoles, RolePermissions, PermissionScopeRules, RolePermissionScopes và AuthzAuditLog; LeavePolicies có `EffectiveFrom`. |
| Leave schema | `LeaveService.Infrastructure/Persistence/LeaveDbContext.cs`, `LeaveBalance.cs`, `LeaveRequestYearAllocation.cs` | `PolicyDays`, `Used`, `Pending`, `RowVersion` được lưu; `Entitled` và `Available` là giá trị `[NotMapped]`; allocation giữ `PolicyVersionId` và `PolicyDaysAtCreation` làm snapshot lịch sử. |
| Tạo đơn | `LeaveService.Application/Services/LeaveRequestService.cs` và `LeaveRequestRepository.cs` | Policy snapshot được chọn theo ngày tạo đơn trong UTC+07, không chọn theo ngày nghỉ trong tương lai; ngày nghỉ được tính theo business days sau khi loại holidays. |
| Team decision | `LeaveRequestService.DecideAsync` và `LeaveRequestRepository.TransitionOnceAsync` | Với Team scope, HR xác minh nhân viên active và ManagerId hiện tại; không tin `ApproverId` snapshot. `All` scope không cần Team lookup. Chặn tự duyệt. |
| Balance / approve / reject / cancel | `LeaveBalanceService.cs`, `LeaveRequestRepository.UpdateBalancesAsync`, `TransitionOnceAsync`, `CancelOnceAsync` | Đọc authoritative HR policy trước transaction; thay đổi request, `PolicyDays`, Used/Pending và history trong transaction `SERIALIZABLE`. Approve chuyển Pending sang Used; Reject/Cancel chỉ giải phóng Pending. |
| Policy timeline | `HrService.Application/Services/LeaveCatalogService.cs`, `LeavePolicyTimeline.cs`, `LeavePolicyVersionDeletionService.cs` | Version đã effective không thể edit/delete; edit chỉ dành cho version tương lai và không đổi year/type/effective date; delete kiểm tra cả ngày hiệu lực lẫn allocation reference từ Leave. |

### Các điểm đã xác nhận

- `LeaveBalances` có unique key logic `(EmployeeId, LeaveTypeId, Year)`; `PolicyDays`, `Used`, `Pending` đều có check constraint không âm; `RowVersion` hỗ trợ concurrency.
- `Entitled = max(PolicyDays, Used + Pending)` và `Available = Entitled - Used - Pending`; giảm policy không làm giảm usage/pending đã ghi nhận hoặc tạo Available âm.
- `GET /api/v1/leave-balances/mine` đồng bộ catalog authoritative trước khi trả số dư; đồng bộ chỉ tạo/cập nhật `PolicyDays`, không ghi đè Used/Pending. Loại nghỉ thiếu policy hiệu lực được bỏ qua và trả thông tin qua header `X-Leave-Policy-Missing`.
- Việc approve/reject/cancel lấy policy hiện hành theo ngày nghiệp vụ tại thời điểm xử lý để cập nhật `PolicyDays`, nhưng không ghi đè `PolicyVersionId` / `PolicyDaysAtCreation` snapshot của allocation đã tạo.
- HR kiểm tra `LeavePolicyVersionId` qua Leave API trước khi xóa. Nếu Leave không khả dụng hoặc báo còn reference, HR fail closed và không xóa.
- Chưa có bulk balance reconciliation worker và chưa có API đọc history; tài liệu không mô tả hai tính năng này như chức năng đã triển khai.
