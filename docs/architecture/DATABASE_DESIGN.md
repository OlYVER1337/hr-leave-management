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
| `LeavePolicies` | Chính sách số ngày nghỉ theo năm: `Id` tự tăng, `LeaveTypeId`, `Year`, `DaysPerYear` (`decimal(5,1)`) |
| `Holidays` | Các ngày không làm việc: `Id` tự tăng, `HolidayDate`, `Name` (150) |
| `RefreshTokens` | Vòng đời Refresh Token đã được hash: `Id` GUID, `EmployeeId`, `TokenHash`, thời gian tạo/hết hạn/thu hồi và hash của token thay thế |

### Các khóa và ràng buộc quan trọng

- Unique index:
  - `Departments.Name`
  - `Employees.Code`
  - `Employees.Email`
  - `LeaveTypes.Code`
  - `LeavePolicies(LeaveTypeId, Year)`
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

---

# Cấu trúc Leave_DB

| Bảng | Mục đích và các cột quan trọng |
|---|---|
| `LeaveBalances` | Số dư phép theo nhân viên/loại nghỉ/năm, bao gồm quyền được hưởng và các bộ đếm `Entitled`, `Used`, `Pending`, sử dụng `decimal(6,1)`; có SQL `rowversion` |
| `LeaveRequests` | Thông tin đơn nghỉ: ID nhân viên/người phê duyệt, snapshot phòng ban, ID loại nghỉ, khoảng thời gian nghỉ, tổng số ngày, lý do (tối đa 1.000 ký tự), trạng thái và timestamps; có SQL `rowversion` |
| `LeaveRequestHistories` | Lịch sử thao tác/trạng thái theo kiểu append, ID người thực hiện, ghi chú tùy chọn (tối đa 1.000 ký tự), thời gian thực hiện |
| `LeaveRequestYearAllocations` | Số ngày được phân bổ cho từng năm dương lịch đối với đơn nghỉ kéo dài qua nhiều năm |

### Các khóa và ràng buộc quan trọng

- Unique index:

```text
LeaveBalances(EmployeeId, LeaveTypeId, Year)
```

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
```

### Leave Service

```text
20261027041915_InitialLeaveSchema
20261027060954_AddApprovalAndYearAllocations
```

`DatabaseInitializer` của HR Service sẽ:

1. Áp dụng migrations.
2. Sau đó thực hiện seeding dữ liệu.

Leave API cũng áp dụng migrations trước khi bắt đầu phục vụ request.

Mỗi project có một **Model Snapshot**.

Đối với các thay đổi schema trong tương lai:

> Cần tạo migration mới, không chỉnh sửa các migration đã được áp dụng.

Source của migrations nằm trong thư mục:

```text
Infrastructure/Persistence/Migrations
```

của từng service.

---

# Seed Data

Các Seeder có tính chất **idempotent** và sẽ không chạy nếu chưa được cấu hình bật.

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