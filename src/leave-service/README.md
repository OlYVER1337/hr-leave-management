# Dịch vụ nghỉ phép (Leave Service)

Leave Service quản lý đơn nghỉ phép, số dư phép và lịch sử xử lý trong
`Leave_DB`. Thông tin nhân viên và danh mục nghỉ phép do HR Service quản lý.

## API

Tất cả endpoint đều yêu cầu bearer token do HR Service cấp:

- `POST /api/v1/leave-requests`: tạo đơn nghỉ cho nhân viên đang đăng nhập.
- `GET /api/v1/leave-requests/mine`: liệt kê các đơn nghỉ của nhân viên đang
  đăng nhập.
- `GET /api/v1/leave-requests/{id}`: xem chi tiết một đơn nghỉ của nhân viên đó.
- `POST /api/v1/leave-requests/{id}/cancel`: hủy đơn đang chờ duyệt của nhân viên.
- `GET /api/v1/leave-requests/pending-approval`: liệt kê các đơn được giao cho
  quản lý đang đăng nhập; HR có thể xem tất cả đơn đang chờ duyệt.
- `POST /api/v1/leave-requests/{id}/approve` và
  `POST /api/v1/leave-requests/{id}/reject`: nhận `{"note":"..."}` để duyệt hoặc
  từ chối đơn đang chờ. Quản lý chỉ được xử lý đơn được giao cho mình; HR có thể
  xử lý mọi đơn đang chờ duyệt.
- `POST /api/v1/leave-requests/{id}/cancel`: nhận `{"note":"..."}`; chỉ chủ đơn
  mới được hủy đơn đang chờ duyệt của mình.
- `GET /api/v1/leave-balances/mine?year=2026`: trả về số dư phép của nhân viên
  đang đăng nhập trong năm được yêu cầu.

Ví dụ tạo đơn:

```json
{
  "leaveTypeId": 1,
  "startDate": "2026-11-02",
  "endDate": "2026-11-03",
  "reason": "Nghỉ việc cá nhân"
}
```

Dịch vụ tính các ngày trong tuần, loại trừ ngày lễ do HR cấu hình và hỗ trợ đơn
nghỉ kéo dài qua nhiều năm dương lịch bằng cách phân bổ số ngày vào chính sách
của từng năm. Với loại nghỉ trừ vào số dư, nếu nhân viên chưa có số dư thì dịch
vụ khởi tạo số dư từ chính sách HR. Sau đó, dịch vụ tạo đơn chờ duyệt, tăng số
dư đang chờ và ghi lịch sử tạo đơn trong cùng một giao dịch. Đơn bị trùng ngày
hoặc không đủ số dư trả về `409 Conflict`. Khoảng thời gian chỉ có ngày không
làm việc và dữ liệu đầu vào không hợp lệ trả về `422 Unprocessable Entity`.

Các API được Nginx cung cấp dưới tiền tố `/leave-api/`; ví dụ:
`/leave-api/api/v1/leave-requests/mine`.

Quyết định duyệt/từ chối, thao tác hủy, cập nhật số dư và ghi lịch sử được thực
hiện nguyên tử trong giao dịch. Năm và số ngày được phân bổ cho từng năm được
lưu cùng đơn nghỉ, nhờ đó khi duyệt hoặc từ chối, dịch vụ có thể giải phóng đúng
số ngày đã giữ chỗ, kể cả khi danh mục ngày lễ của HR thay đổi sau đó.

## Kiểm thử

Chạy unit test mà không cần Docker:

```powershell
dotnet test src\leave-service\LeaveService.Tests\LeaveService.Tests.csproj
```

Các kiểm thử lưu trữ trên SQL Server và kiểm thử đồng thời sẽ chạy khi thiết lập
biến môi trường `HR_TEST_SQL_CONNECTION_STRING`. Bộ kiểm thử tạo và xóa một cơ
sở dữ liệu riêng biệt; chỉ sử dụng SQL Server cục bộ hoặc môi trường kiểm thử.
