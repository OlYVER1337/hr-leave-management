# Dịch vụ nghỉ phép (Leave Service)

Leave Service quản lý đơn nghỉ phép, số dư phép và lịch sử xử lý trong `Leave_DB`.
HR Service là nguồn dữ liệu có thẩm quyền cho nhân viên, quan hệ quản lý, loại nghỉ,
chính sách nghỉ và ngày lễ. Leave Service chỉ gọi HR qua HTTP; không truy cập trực
tiếp `HR_DB`.

## API

Các route dưới đây dùng base path `/api/v1` của Leave Service và yêu cầu bearer
access token do HR Service cấp. Các route được publish qua Nginx với tiền tố
`/leave-api`.

### Đơn nghỉ

| Method / route | Permission + scope | Mô tả |
|---|---|---|
| `GET /leave-requests?page=1&pageSize=20` | `leaves.read:Self/Team/All` | Danh sách theo data scope; hỗ trợ `search`, `status`, `leaveTypeId` |
| `GET /leave-requests/mine?page=1&pageSize=20` | `leaves.read:Self/All` | Chỉ đơn của nhân viên đang đăng nhập |
| `GET /leave-requests/calendar?from=YYYY-MM-DD&to=YYYY-MM-DD` | `leaves.read:Self/Team/All` | Dữ liệu lịch trong khoảng ngày hợp lệ; tối đa 62 ngày |
| `GET /leave-requests/{id}` | `leaves.read:Self/Team/All` | Chi tiết đơn trong phạm vi được cấp; ngoài scope trả 404 |
| `POST /leave-requests` | `leaves.create:Self` | Tạo đơn cho chính caller |
| `GET /leave-requests/pending-approval?page=1&pageSize=20` | `leaves.decide:Team/All` | Hộp thư đơn Pending; hỗ trợ `search` |
| `GET /leave-requests/summary` | `leaves.decide:Team/All` | Tổng hợp Pending trong decide scope; Approved/Total phụ thuộc read scope |
| `PATCH /leave-requests/{id}/decision` | `leaves.decide:Team/All` | Duyệt hoặc từ chối đơn Pending |
| `POST /leave-requests/{id}/cancel` | `leaves.cancel:Self` | Chủ đơn hủy đơn Pending của mình |

Body tạo đơn:

```json
{
  "leaveTypeId": 1,
  "startDate": "2026-11-02",
  "endDate": "2026-11-03",
  "reason": "Nghỉ việc cá nhân"
}
```

Body quyết định:

```json
{
  "decision": "approve",
  "note": "Đã sắp xếp nhân sự thay ca."
}
```

`decision` nhận `approve` hoặc `reject`; `note` không bắt buộc.

### Số dư phép

| Method / route | Permission + scope | Mô tả |
|---|---|---|
| `GET /leave-balances/mine?year=2026` | `leaves.read:Self/All` | Chỉ đọc các balance row hiện có của caller |
| `POST /leave-balances/mine/synchronize?year=2026` | `leaves.read:Self/All` | Khởi tạo/đồng bộ balance của caller từ HR catalog authoritative |

GET balance không tạo dữ liệu. POST synchronize chỉ tác động EmployeeId lấy từ JWT
của người gọi, kể cả khi caller có scope All; nếu bỏ `year`, service dùng business
year UTC+07.

Luồng synchronize chỉ tạo/cập nhật balance cho leave type đang active và
`DeductsBalance=true`. Mỗi loại nghỉ cần đúng một policy cho năm được đồng bộ;
quota phải nằm trong khoảng hợp lệ của HR. Giao dịch dùng isolation
`SERIALIZABLE`, tạo row còn thiếu, cập nhật `Entitled` nhưng bảo toàn `Used` và
`Pending`. Nếu quota mới thấp hơn `Used + Pending`, toàn bộ thao tác rollback
và trả `409 Conflict`. Gọi lại nhiều lần là idempotent.

Luồng `POST /leave-requests` cũng đọc policy authoritative và refresh
`Entitled` trong transaction trước khi giữ thêm ngày `Pending`; người dùng không
cần mở màn hình số dư trước khi tạo đơn. Hiện chưa có API/worker bulk
reconciliation để đồng bộ balance cho toàn bộ nhân viên khi HR thay đổi policy.

## Quy tắc nghiệp vụ

- Employee phải active và leave type phải active khi tạo đơn.
- Backend tính ngày làm việc từ thứ Hai đến thứ Sáu, loại trừ holidays do HR quản lý.
- Request phải có ít nhất một ngày làm việc; ngày kết thúc không trước ngày bắt đầu.
- Khoảng ngày tối đa 366 ngày; lý do bắt buộc, tối đa 1.000 ký tự.
- Request qua nhiều năm được chia thành yearly allocation và dùng policy của từng năm.
- Không cho các đơn Pending/Approved của cùng nhân viên chồng ngày.
- Create giữ chỗ bằng cách tăng `Pending`; Approve chuyển `Pending` sang `Used`;
  Reject/Cancel giải phóng `Pending`.
- `ApproverId` là snapshot tại thời điểm tạo đơn, không phải nguồn authorization hiện tại.
  Với Team scope, Leave Service hỏi HR về reporting line hiện tại; HR không khả dụng
  thì thao tác cần Team scope fail closed.
- Cập nhật trạng thái, balance và history được thực hiện trong cùng transaction.
  RowVersion, unique index, Serializable transaction và retry xử lý các tình huống
  cạnh tranh đã được triển khai.

## Phân trang và lỗi

Các list endpoint có phân trang yêu cầu cả `page` và `pageSize`; `page >= 1`,
`pageSize` từ 1 đến 100. Response trả danh sách trong body và tổng số record
trong header `X-Total-Count`. Thiếu hoặc sai tham số phân trang trả HTTP 400.

Các status thường gặp: 401 khi token không hợp lệ; 403 khi thiếu permission/scope;
404 khi không tồn tại hoặc ngoài data scope; 409 khi có conflict nghiệp vụ/trạng
thái/concurrency; 422 khi validation nghiệp vụ thất bại; 503 khi HR dependency
không khả dụng.

## Kiểm thử

Chạy toàn bộ Leave Service tests:

```powershell
dotnet test src\leave-service\LeaveService.slnx --no-restore
```

Các integration test cần SQL Server thông qua biến
`HR_TEST_SQL_CONNECTION_STRING`. Test có thể tạo/xóa database ngẫu nhiên; chỉ
dùng SQL Server dành riêng cho kiểm thử, không trỏ vào production.
