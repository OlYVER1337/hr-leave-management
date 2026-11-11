# Dynamic Authorization Design — V1

## 1. Mục tiêu và phạm vi

V1 thay thế dần phân quyền phụ thuộc Role enum bằng Dynamic RBAC kết hợp Resource-based Data Scope.

Mục tiêu:

- Thêm Role mới bằng dữ liệu trong HR_DB, không phải thêm enum hoặc viết nhánh if (role == ...).
- Một endpoint có thể phục vụ nhiều Role; backend tự kiểm tra Permission và Scope.
- Không trả dữ liệu ngoài phạm vi được cấp.
- Giữ quyền sở hữu dữ liệu: HR Service sở hữu HR_DB; Leave Service chỉ gọi HR Service qua HTTP, không truy cập trực tiếp HR_DB.
- Không thêm message broker hoặc cơ chế đồng bộ bất đồng bộ vào V1.

## 2. Nguồn dữ liệu và giao tiếp giữa service

HR Service là nguồn dữ liệu có thẩm quyền cho:

- Danh tính và trạng thái hoạt động của nhân viên.
- Role và Permission.
- Phòng ban và quan hệ quản lý hiện tại (Employee.ManagerId).

Leave Service sở hữu Leave_DB và không tạo bản sao quan hệ quản lý trong V1.

### Truy vấn danh sách

Khi Scope yêu cầu Team, Leave Service gọi HR Service để lấy một lần danh sách nhân viên thuộc Team hiện tại, sau đó truyền danh sách EmployeeId cho repository lọc trong SQL. Không gọi HR theo từng đơn.

### Quyết định phê duyệt

Khi Manager quyết định một đơn Pending, Leave Service gọi HR Service tại thời điểm quyết định để xác minh ManagerId hiện tại của nhân viên. Không dùng ApproverId snapshot hoặc dữ liệu cache để quyết định quyền.

Nếu HR Service không khả dụng, Leave Service trả 503 Dependency unavailable và không thay đổi trạng thái, số dư hoặc lịch sử đơn.

## 3. Schema authorization trong HR_DB

| Bảng | Trách nhiệm |
|---|---|
| Roles | Danh mục Role động |
| Permissions | Danh mục Permission dạng resource.action |
| UserRoles | Gán một hoặc nhiều Role cho nhân viên, hỗ trợ thời hạn và thu hồi |
| RolePermissions | Gán Permission cho Role |
| PermissionScopeRules | Danh mục Scope hợp lệ theo từng Permission |
| RolePermissionScopes | Gán Scope cho Role-Permission; khóa ngoại kép bảo đảm Permission được gán và Scope hợp lệ |
| AuthzAuditLog | Lưu người thực hiện, đối tượng bị thay đổi và giá trị trước/sau |

Employee.Role enum được giữ tạm trong giai đoạn chuyển đổi. Không xóa trường cũ cho đến khi dữ liệu hiện tại đã được backfill sang UserRoles và mọi API, seeder, test, JWT đều đọc Dynamic RBAC.

Scope được lưu dưới dạng chuỗi có constraint: Self, Team, Department, All. V1 chỉ bật các Scope đã được định nghĩa nghiệp vụ và đã được service hỗ trợ. Chưa cấp Department trước khi chốt quy tắc phòng ban hiện tại hay snapshot.

## 4. Hợp nhiều Role

- Effective permissions là hợp Permission của tất cả Role còn hiệu lực của User.
- Effective scopes của một Permission là hợp các Scope từ các Role có Permission đó.
- Không xếp hạng Scope bằng enum hoặc số thứ tự.
- Repository áp dụng hợp điều kiện bằng OR và luôn lọc trong SQL.
- Không có Permission hoặc không có Scope hợp lệ thì từ chối (fail closed).

## 5. Permission baseline

Permission sử dụng mã resource.action:

- employees.read
- employees.create
- employees.update
- employees.deactivate
- employees.password.reset
- leaves.read
- leaves.create
- leaves.decide
- leaves.cancel
- roles.manage

V1 sử dụng leaves.decide cho cả approve và reject; thao tác quyết định vẫn ghi rõ trạng thái và Actor vào LeaveRequestHistory.

### Role và Scope được seed trong V1

Các Role hệ thống ban đầu là Employee, Manager, HR và Auditor. Bảng dưới đây là baseline quyền; Scope chỉ được hợp nhất giữa các Role mà người dùng đang sở hữu.

| Role | Permission | Scope |
|---|---|---|
| Employee | employees.read | Self |
| Employee | leaves.read | Self |
| Employee | leaves.create | Self |
| Employee | leaves.cancel | Self |
| Manager | employees.read | Self, Team |
| Manager | leaves.read | Self, Team |
| Manager | leaves.create | Self |
| Manager | leaves.cancel | Self |
| Manager | leaves.decide | Team |
| HR | employees.read | All |
| HR | employees.create | All |
| HR | employees.update | All |
| HR | employees.deactivate | All |
| HR | employees.password.reset | All |
| HR | leaves.read | All |
| HR | leaves.create | Self |
| HR | leaves.decide | All |
| HR | leaves.cancel | Self |
| HR | roles.manage | All |
| Auditor | employees.read | All |
| Auditor | leaves.read | All |

Danh mục Scope hợp lệ của V1:

- employees.read: Self, Team, All.
- leaves.read: Self, Team, All.
- leaves.decide: Team, All.
- employees.create, employees.update, employees.deactivate, employees.password.reset, roles.manage: All.
- leaves.create, leaves.cancel: Self.

Department chưa được cấp cho Permission nào trong V1. Chỉ mở Scope này sau khi chốt và triển khai quy tắc Department theo từng resource.

Backfill từ Employee.Role chỉ tạo UserRole nếu cặp EmployeeId/RoleId chưa từng tồn tại. Bản ghi đã thu hồi hoặc hết hạn không bị seeder tự động khôi phục; điều này bảo đảm khởi động lại ứng dụng không cấp lại quyền đã bị thu hồi. Seeder có thể chạy lặp lại mà không nhân bản Role, Permission, Scope hoặc Role assignment.

## 6. Authorization Service — trạng thái triển khai

Authorization Service nằm ở Application layer và chỉ đọc quyền hiệu lực qua IAuthorizationRepository.

Repository chỉ trả các cặp Permission/Scope khi đáp ứng toàn bộ điều kiện:

- Nhân viên đang hoạt động.
- UserRole chưa bị thu hồi.
- UserRole chưa hết hạn tại thời điểm truy vấn (ExpiresAtUtc > UTC now).
- Role đang hoạt động.
- Permission đang hoạt động.
- Role-Permission có Scope được gán.

Authorization Service hợp các Scope trùng Permission giữa nhiều Role, loại bỏ Scope trùng và mặc định không có quyền nếu không có Scope hợp lệ. V1 hiện hỗ trợ Self, Team và All; Department bị từ chối cho đến khi có bộ lọc thực thi theo từng resource.

Authorization Service đã được nối vào luồng phát access token của AuthenticationService. Cả login và refresh đều gọi lại GetEffectiveAuthorizationAsync trước khi phát JWT; refresh không sao chép claim perm từ access token cũ.

HR Employees API đã chuyển sang Permission + Scope: GET collection và GET theo ID dùng employees.read; collection được lọc trong SQL theo hợp Self/Team/All, còn truy cập một nhân viên cụ thể được giới hạn theo cùng Scope. Các thao tác create/update/deactivate/reset-password tạm thời vẫn yêu cầu Role HR vì EmployeeService hiện ghi Employee.Role enum và chưa đồng bộ thay đổi Role sang UserRoles. Chỉ chuyển thao tác ghi sang Permission sau khi hoàn thành luồng quản lý UserRoles, tránh để Role enum và quyền động lệch nhau. /api/v1/me/team yêu cầu employees.read với Scope Team. Các endpoint đọc đã chuyển kiểm tra authz_v=1 và perm; claim role không cấp quyền cho các endpoint đọc này.

Các controller Departments, Holidays, LeavePolicies và LeaveTypes vẫn còn policy Role cũ; cần chuyển lần lượt sang Permission ở các bước tiếp theo. Không coi toàn bộ HR API đã hoàn tất migration khỏi Role cho đến khi các endpoint này được chuyển.

## 7. JWT và hiệu lực quyền

HR Service phát Permission và Scope hiệu lực vào JWT dưới dạng claim có cấu trúc và phiên bản schema:

- authz_v = 1.
- perm là một mảng JSON; mỗi phần tử có p là mã Permission và s là danh sách Scope.
- Ví dụ: [{"p":"leaves.read","s":["Self","Team"]}].
- Không có Permission hiệu lực thì không phát claim perm.
- Claim role được giữ tạm cho giao diện và các controller HR chưa chuyển đổi. Các endpoint đã chuyển sang Permission + Scope không sử dụng claim role để authorization. Mục tiêu cuối cùng của V1 vẫn là loại bỏ Role claim khỏi mọi quyết định authorization.

Login và refresh đều gọi lại AuthorizationService.GetEffectiveAuthorizationAsync trước khi phát token. Vì vậy sau khi Role bị thu hồi, access token phát lại từ refresh chỉ chứa Permission còn hiệu lực; không sao chép quyền từ token cũ.

Access JWT được kiểm tra cục bộ tại mỗi service. Thay đổi Role/Permission có hiệu lực khi access token mới được phát hành; thu hồi refresh token không vô hiệu hóa access token đã được cấp. V1 giữ access-token lifetime ngắn hiện có và ghi nhận giới hạn này.

## 8. Quy tắc Team

Team được định nghĩa theo quan hệ quản lý hiện tại:

- Employees Team: Employees.ManagerId == ActorId.
- Leave Team: EmployeeId của đơn phải thuộc danh sách nhân viên hiện đang có ManagerId == ActorId trong HR Service.

LeaveRequests.ApproverId là snapshot tại thời điểm tạo đơn, chỉ phục vụ lịch sử. Không dùng trường này làm điều kiện authorization hoặc Team filter.

## 9. Thứ tự triển khai V1

1. Thêm schema authorization và migration HR_DB; giữ tương thích Role enum.
2. Seed Role/Permission/Scope baseline và backfill UserRoles từ Employee.Role.
3. Thêm Authorization Service để hợp Permission và Scope, mặc định deny.
4. Phát perm claim từ HR login/refresh; cập nhật token tests.
5. Refactor HR Employees API đọc (GET list/detail) và /api/v1/me/team sang Permission + Scope; thao tác ghi sẽ chuyển sau khi UserRoles được quản lý đầy đủ.
6. Refactor Leave read visibility dùng Scope và HR team lookup; giữ HR lookup tại thời điểm approve/reject.
7. Cập nhật characterization/integration tests, sau đó benchmark 50/100 VU.

Outbox, asynchronous read model, backfill/reconciliation giữa hai service không thuộc V1; chỉ xem xét nếu benchmark chứng minh HTTP lookup là nút thắt.

## 10. Ràng buộc bảo mật

- Không cho phép gán Role hoặc Permission vượt quá quyền người cấp.
- Không cho phép người dùng tự cấp quyền cho chính mình.
- Không xóa quản trị viên cuối cùng.
- Ghi audit cho thay đổi Role/Permission/Scope.
- Không trả PasswordHash hoặc entity trực tiếp.
- Resource không tồn tại hoặc ngoài quyền có thể trả 404 để tránh lộ sự tồn tại; thiếu permission trả 403.
- Không tin cậy role/scope do client gửi trong query hoặc body.
