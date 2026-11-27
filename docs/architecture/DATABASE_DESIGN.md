# Thiết kế cơ sở dữ liệu

## Quyền sở hữu dữ liệu và ERD

SQL Server chứa hai cơ sở dữ liệu:

- HR Service sở hữu `HR_DB`.
- Leave Service sở hữu `Leave_DB`.

Các sơ đồ được duy trì bằng Mermaid:

- [HR ERD](diagrams/png/erd-hr-db.png) ([mã nguồn](diagrams/mermaid/erd-hr-db.mmd))
- [Leave ERD](diagrams/png/erd-leave-db.png) ([mã nguồn](diagrams/mermaid/erd-leave-db.mmd))

Các ID tham chiếu giữa các service trong `Leave_DB` chỉ là **tham chiếu theo quy ước**, không phải SQL Foreign Key.

Chỉ các mối quan hệ mà cùng một service sở hữu hoàn toàn mới được áp dụng database constraint trực tiếp.

---

# Cấu trúc HR_DB

| Bảng | Mục đích và các cột quan trọng |
|---|---|
| `Departments` | Các đơn vị tổ chức: `Id` tự tăng, `Name` bắt buộc (tối đa 100 ký tự) |
| `Employees` | Thông tin nhân sự và tài khoản: `Id` kiểu GUID, `Code` (20), `FullName` (150), `Email` (200), `PasswordHash`, `Role` dạng chuỗi, `HireDate`, `IsActive`, `DepartmentId`, `ManagerId` tùy chọn |
| `LeaveTypes` | Danh mục loại nghỉ: `Id` tự tăng, `Code` (20), `Name` (100), `IsPaid`, `DeductsBalance`, `IsActive` |
| `LeavePolicies` | Các phiên bản chính sách theo loại nghỉ/năm: `Id`, `LeaveTypeId`, `Year`, `DaysPerYear` (`decimal(5,1)`), `EffectiveFrom` (`date`), `CreatedAtUtc` (`datetime2`), `Reason` (tối đa 500 ký tự). Policy cũ được chuyển thành phiên bản nền có hiệu lực từ 01/01/2000. |
| `Holidays` | Các ngày không làm việc: `Id` tự tăng, `HolidayDate`, `Name` (150) |
| `RefreshTokens` | Vòng đời Refresh Token đã được hash: `Id` GUID, `EmployeeId`, `TokenHash`, thời gian tạo/hết hạn/thu hồi và hash của token thay thế |

### Các khóa và ràng buộc quan trọng

- Unique index:
  - `Departments.Name`
  - `Employees.Code`
  - `Employees.Email`
  - `LeaveTypes.Code`
  - `LeavePolicies(LeaveTypeId, Year, EffectiveFrom)`
  - `Holidays.HolidayDate`
  - `RefreshTokens.TokenHash`

- `Employees.DepartmentId` tham chiếu đến `Departments`.

- `Employees.ManagerId` tự tham chiếu đến `Employees`.

Hai quan hệ trên sử dụng hành vi xóa:

```text
Restrict
```

- `LeavePolicies.LeaveTypeId` tham chiếu đến `LeaveTypes` với hành vi:

```text
Restrict
```

- `RefreshTokens.EmployeeId` tham chiếu đến `Employees` với hành vi:

```text
Cascade Delete
```

- Index hỗ trợ cho Refresh Token:

```text
(EmployeeId, RevokedAtUtc, ExpiresAtUtc)
```


## Dynamic Authorization tables trong HR_DB

Migration `20261111043715_AddDynamicAuthorizationSchema` bổ sung:

| Bảng | Khóa / dữ liệu chính | Vai trò |
|---|---|---|
| `Roles` | `Id` int PK; `Code` nvarchar(50) unique; Name, Description, IsSystem, IsActive, CreatedAtUtc | Danh mục Role |
| `Permissions` | `Id` int PK; `Code` nvarchar(100) unique; Name, Description, IsActive | Danh mục permission dạng resource.action |
| `UserRoles` | `Id` bigint PK; EmployeeId, RoleId; GrantedBy/At, ExpiresAtUtc, RevokedBy/At | Assignment Role theo nhân viên, hỗ trợ expiry/revoke |
| `RolePermissions` | Composite PK (RoleId, PermissionId); GrantedByEmployeeId, GrantedAtUtc | Permission được gắn cho Role |
| `PermissionScopeRules` | Composite PK (PermissionId, Scope); Scope có check constraint | Scope hợp lệ theo permission |
| `RolePermissionScopes` | Composite PK (RoleId, PermissionId, Scope) | Scope được cấp cho Role-Permission |
| `AuthzAuditLog` | bigint PK; ActorEmployeeId, TargetEmployeeId, Action, TargetType, TargetId, BeforeJson, AfterJson, CorrelationId, CreatedAtUtc | Audit thay đổi authorization |

Index và constraint quan trọng:
- `Roles.Code`, `Permissions.Code` là unique.
- `UserRoles(EmployeeId, RoleId)` là unique có filter `RevokedAtUtc IS NULL`; có index lookup theo EmployeeId/RevokedAtUtc/ExpiresAtUtc và RoleId.
- `RolePermissions` có khóa ghép RoleId + PermissionId.
- `PermissionScopeRules` và `RolePermissionScopes` giới hạn Scope bằng check constraint: Self, Team, Department, All.
- `RolePermissionScopes` có composite FK tới RolePermission và PermissionScopeRule, ngăn cấp scope không có permission hoặc scope không hợp lệ.
- Các FK employee/role/permission dùng Restrict theo mapping hiện tại.
- Audit log có index theo TargetEmployeeId + CreatedAtUtc và ActorEmployeeId + CreatedAtUtc.

Các Scope đang được thực thi trong API là Self, Team và All. Department tồn tại trong schema để mở rộng nhưng chưa được dùng làm điều kiện lọc dữ liệu.

`Employee.Role` là trường enum/string legacy trong `Employees`. AuthorizationSeeder dùng nó để backfill Role assignment ban đầu vào `UserRoles` theo hướng idempotent; seeder không khôi phục assignment đã revoke hoặc hết hạn. Sau backfill, quyền backend được tính từ `UserRoles` → `Roles` → `RolePermissions` → `RolePermissionScopes`, có đối chiếu `PermissionScopeRules`.

Trường legacy này vẫn được giữ cho nhãn vai trò trong UI/DTO, lọc và thống kê nhân viên, dữ liệu tạo/cập nhật nhân viên, claim `role` tương thích và một số quy tắc nghiệp vụ cũ. Nó không phải nguồn authorization và không tự đồng bộ với các UserRole assignment được cấp/thu hồi động; vì vậy không suy ra effective permission từ `Employees.Role`. Chỉ xóa cột sau khi các mục đích tương thích và nghiệp vụ còn lại được thay thế bằng thiết kế mới và có migration riêng.

---

# Cấu trúc Leave_DB

| Bảng | Mục đích và các cột quan trọng |
|---|---|
| `LeaveBalances` | Số dư theo nhân viên/loại nghỉ/năm, gồm `PolicyDays`, `Used`, `Pending` (`decimal(6,1)`) và SQL `rowversion`. SQL không lưu `Entitled` hoặc `Available`; đây là thuộc tính `[NotMapped]`: `Entitled = max(PolicyDays, Used + Pending)`, `Available = Entitled - Used - Pending`. |
| `LeaveRequests` | Thông tin đơn nghỉ: ID nhân viên/người phê duyệt, snapshot phòng ban, ID loại nghỉ, khoảng thời gian nghỉ, tổng số ngày, lý do (tối đa 1.000 ký tự), trạng thái và timestamps; có SQL `rowversion` |
| `LeaveRequestHistories` | Lịch sử thao tác/trạng thái theo kiểu append, ID người thực hiện, ghi chú tùy chọn (tối đa 1.000 ký tự), thời gian thực hiện |
| `LeaveRequestYearAllocations` | PK ghép `(LeaveRequestId, Year)`; phân bổ `Days` theo từng năm, gồm `PolicyVersionId` (scalar reference tới HR, nullable với dữ liệu lịch sử) và `PolicyDaysAtCreation` (quota snapshot lúc tạo đơn, nullable với dữ liệu lịch sử). FK duy nhất của bảng là `LeaveRequestId` tới `LeaveRequests` với Cascade. |

### Các khóa và ràng buộc quan trọng

- Unique index:

```text
LeaveBalances(EmployeeId, LeaveTypeId, Year)
```

- CHECK constraints:
  - `CK_LeaveBalances_PolicyDays_NonNegative`: `PolicyDays >= 0`
  - `CK_LeaveBalances_Used_NonNegative`: `Used >= 0`
  - `CK_LeaveBalances_Pending_NonNegative`: `Pending >= 0`

`Entitled = max(PolicyDays, Used + Pending)`; `Available = Entitled - Used - Pending`. Leave Service không tạo foreign key xuyên database tới HR `LeavePolicies`; `PolicyVersionId` là scalar reference do HR sở hữu.

Ngăn việc tạo trùng số dư phép cho cùng:

- Nhân viên
- Loại nghỉ
- Năm

- Index:

```text
LeaveRequests(EmployeeId, Status, StartDate, EndDate)
```

hỗ trợ truy vấn:

- Các đơn nghỉ của nhân viên.
- Kiểm tra các khoảng thời gian nghỉ bị chồng chéo.

- Index:

```text
LeaveRequestHistories(LeaveRequestId, CreatedAtUtc)
```

hỗ trợ lấy lịch sử theo thứ tự thời gian.

- `LeaveRequestYearAllocations` có Composite Primary Key:

```text
(LeaveRequestId, Year)
```

- Các bản ghi History và Year Allocation tham chiếu đến `LeaveRequests` và sử dụng:

```text
Cascade Delete
```

- Các ID:

  - Employee
  - Approver
  - Department
  - Leave Type

là dữ liệu thuộc sở hữu của HR Service và chỉ được tham chiếu theo ID, **không có Cross-Database Foreign Key**.

- Giá trị trạng thái được lưu dưới dạng chuỗi.

Các giá trị của `LeaveRequestStatus` gồm:

```text
Pending
Approved
Rejected
Cancelled
```

---

# Migration và khởi tạo Database

EF Core Migrations được version theo từng Infrastructure Project sở hữu database.

### HR Service

```text
20261026043924_InitialHrSchema
20261102014444_AddRefreshTokens
20261111043715_AddDynamicAuthorizationSchema
20261118090000_AddLeavePolicyVersions
```

### Leave Service

```text
20261027041915_InitialLeaveSchema
20261027060954_AddApprovalAndYearAllocations
20261117031304_AddLeaveBalanceNonNegativeConstraints
20261118093000_AddPolicySnapshotsToLeaveBalances
```

`DatabaseInitializer` của HR Service sẽ:

1. Áp dụng migrations.
2. Sau đó thực hiện seeding dữ liệu.

Leave API cũng áp dụng migrations trước khi bắt đầu phục vụ request.

Mỗi project có một **Model Snapshot**. HR model snapshot bao gồm các bảng Dynamic Authorization; Leave DB không chứa bảng Role/Permission và không tạo cross-database foreign keys.

Đối với các thay đổi schema trong tương lai:

> Cần tạo migration mới, không chỉnh sửa các migration đã được áp dụng.

Source của migrations nằm trong thư mục:

```text
Infrastructure/Persistence/Migrations
```

của từng service.

---

# Seed Data

Initial HR và Demo Seeder có tính chất **idempotent** và chỉ chạy khi được bật qua cấu hình. AuthorizationSeeder chạy khi HR database khởi tạo để seed baseline Role/Permission/Scope và backfill UserRoles; không tự khôi phục assignment đã revoke/hết hạn.

## Tài khoản HR ban đầu

```text
INITIAL_HR_ENABLED=true
```

sẽ tạo:

- Tài khoản HR ban đầu.
- Department tương ứng.

Việc tạo chỉ diễn ra khi email chưa tồn tại trong hệ thống.

---

## Demo Data

```text
DEMO_DATA_ENABLED=true
```

sẽ tạo:

- Tài khoản Manager demo.
- Tài khoản Employee demo.
- Department.
- Các loại nghỉ chuẩn:

```text
Annual
Sick
Unpaid
```

- Các Leave Policy phù hợp.

---

## Dữ liệu Load Test

Dữ liệu mở rộng phục vụ Load Test được tạo riêng bằng:

```text
scripts/load-test/seed-scale-data.sql
```

**Không được chạy script này trên dữ liệu Production.**

---

# Bảo mật Seed Data

Nên sử dụng mật khẩu riêng cho:

- Local environment.
- Demo environment.

Mật khẩu nên được lưu trong:

- File `.env` không được commit.
- Secret Store.

Seed settings không phải là cơ chế quản lý tài khoản dành cho Production.

---

# Nguồn dữ liệu quyết định Schema

Các hình ảnh ERD chỉ đóng vai trò:

> Tổng quan trực quan dành cho con người.

Nguồn quyết định chính xác cho các thông tin sau là:

- EF Core Model Configuration.
- EF Core Migration Files.

Đây là nguồn dữ liệu authoritative cho:

- SQL data types.
- Indexes.
- Constraints.
- Schema thực tế được triển khai.

Nói cách khác:

```text
ERD
    ↓
Tổng quan trực quan

EF Core Configuration + Migrations
    ↓
Nguồn quyết định schema thực tế
```
## Policy version và snapshot giữa HR_DB / Leave_DB

- HR Service sở hữu `LeavePolicies`; mỗi version có `LeaveTypeId`, `Year`, `DaysPerYear`, `EffectiveFrom`, `CreatedAtUtc`, `Reason`.
- Unique index `(LeaveTypeId, Year, EffectiveFrom)` cho phép nhiều version trong cùng năm nhưng không cho hai version cùng ngày hiệu lực.
- Policy GET với `asOfDate` trả phiên bản hiệu lực mới nhất theo loại nghỉ/năm tại ngày được yêu cầu. Nghiệp vụ Leave gửi `authoritative=true` để bỏ qua Redis cache.
- `POST /api/v1/leave-policies` tạo version cho loại nghỉ/năm trong body; có thể là version đầu tiên hoặc version tiếp theo, chỉ chặn trùng `LeaveTypeId + Year + EffectiveFrom`.
- `PUT /api/v1/leave-policies/{id}` lấy loại nghỉ/năm từ version `{id}` rồi thêm một version mới; không cập nhật bản ghi gốc và không cho chuyển version sang năm/loại nghỉ khác.
- `PUT /api/v1/leave-policies/{id}/edit` chỉnh version đã tồn tại nhưng chỉ khi `EffectiveFrom` còn trong tương lai; chỉ đổi số ngày/lý do, giữ nguyên năm, loại nghỉ và ngày hiệu lực. Năm hiện tại và tương lai được tạo version; năm đã kết thúc chỉ xem. Version đã effective không được edit/delete.
- DELETE version tương lai yêu cầu Leave Service đếm số `LeaveRequestYearAllocations.PolicyVersionId` tham chiếu. Có allocation tham chiếu thì HR từ chối 409; Leave không khả dụng thì thao tác fail closed. Giữa bước COUNT ở Leave và DELETE ở HR vẫn có race window nhỏ vì hai database không dùng distributed transaction.
- Leave lưu `PolicyVersionId` và `PolicyDaysAtCreation` ở yearly allocation như snapshot lịch sử. Đây không phải FK vì HR và Leave sở hữu database riêng; xóa version đã được tham chiếu bị ngăn qua kiểm tra liên service.
- `LeaveBalances.PolicyDays` là quota hiện hành, không phải quota snapshot lịch sử. Khi policy giảm thấp hơn `Used + Pending`, Leave cập nhật `PolicyDays` nhưng không giảm `Used/Pending`; `Entitled` được suy ra để vẫn bao phủ số ngày đã dùng/đang chờ.

## Đối chiếu ERD với EF Core model và migrations — 25/11/2026

Đã rà soát hai source ERD `diagrams/mermaid/erd-hr-db.mmd` và `diagrams/mermaid/erd-leave-db.mmd` với `HrDbContext`, `LeaveDbContext` và các migration trên nhánh PR #13 (`61f22ad0fb264241744f56e4fb1a2c969d3d3046`). Source Mermaid hiện thể hiện các cấu phần chính của schema đang triển khai; không dùng hình ERD thay cho migration khi cần xác định kiểu dữ liệu hoặc constraint chi tiết.

### HR_DB

- Bảng nền: `Departments`, `Employees`, `RefreshTokens`, `LeaveTypes`, `LeavePolicies`, `Holidays`.
- Dynamic Authorization: `Roles`, `Permissions`, `UserRoles`, `RolePermissions`, `PermissionScopeRules`, `RolePermissionScopes`, `AuthzAuditLog`.
- `LeavePolicies` có `EffectiveFrom` kiểu `date`; unique index `(LeaveTypeId, Year, EffectiveFrom)` ngăn hai version cùng loại nghỉ/năm có cùng ngày hiệu lực.
- `UserRoles` có unique filtered index cho `(EmployeeId, RoleId)` khi `RevokedAtUtc IS NULL`; `RolePermissionScopes` có composite key `(RoleId, PermissionId, Scope)` và scope check constraint.

### Leave_DB

- Bảng: `LeaveRequests`, `LeaveBalances`, `LeaveRequestYearAllocations`, `LeaveRequestHistories`.
- `LeaveBalances` có unique index `(EmployeeId, LeaveTypeId, Year)`, `PolicyDays`, `Used`, `Pending`, `RowVersion`; ba cột số ngày có check constraint không âm.
- `Entitled` và `Available` không phải cột SQL: chúng là thuộc tính `[NotMapped]`, được tính lần lượt bằng `max(PolicyDays, Used + Pending)` và `Entitled - Used - Pending`.
- `LeaveRequestYearAllocations` có composite primary key `(LeaveRequestId, Year)`, lưu `Days`, `PolicyVersionId` nullable và `PolicyDaysAtCreation` nullable. `PolicyVersionId` là scalar reference sang HR_DB, không phải foreign key xuyên database.
- `LeaveRequests` có index `(EmployeeId, Status, StartDate, EndDate)` và RowVersion; `LeaveRequestHistories` có index `(LeaveRequestId, CreatedAtUtc)`. History và yearly allocations cascade-delete theo LeaveRequest.

### Migration hiện có trên source

| Database | Migration |
|---|---|
| HR_DB | `20261026043924_InitialHrSchema` |
| HR_DB | `20261102014444_AddRefreshTokens` |
| HR_DB | `20261111043715_AddDynamicAuthorizationSchema` |
| HR_DB | `20261118090000_AddLeavePolicyVersions` |
| Leave_DB | `20261027041915_InitialLeaveSchema` |
| Leave_DB | `20261027060954_AddApprovalAndYearAllocations` |
| Leave_DB | `20261117031304_AddLeaveBalanceNonNegativeConstraints` |
| Leave_DB | `20261118093000_AddPolicySnapshotsToLeaveBalances` |

Migration list trên được xác nhận từ thư mục `Infrastructure/Persistence/Migrations` của hai service tại revision đối chiếu. Khi schema có thay đổi, cập nhật migration và Model Snapshot trước, rồi mới cập nhật ERD; không sửa migration đã áp dụng. PNG/SVG là file được render từ Mermaid source và không phải nguồn xác định schema.
