# Tài liệu tham chiếu API

## Base URL và OpenAPI

Các endpoint khi chạy bằng Docker Compose trên máy local:

- **HR Service:** `http://localhost:8081`
- **Leave Service:** `http://localhost:8082`
- **Thông qua Web/Nginx:** `/hr-api` và `/leave-api` trên `http://localhost:3000`

Mỗi service cung cấp tài liệu OpenAPI tại `/openapi/v1.json` chỉ trong môi trường **Development**.

Ví dụ:

`http://localhost:8081/openapi/v1.json`

Cấu hình triển khai mặc định sử dụng môi trường Development cho việc chạy local. Không nên vô tình công khai các endpoint OpenAPI của môi trường Development trong production.

Trừ khi có ghi chú khác, các route yêu cầu:

```http
Authorization: Bearer <access-token>
```

Danh sách nhân viên yêu cầu cả hai query parameter `page` và `pageSize`; `page` bắt đầu từ 1 và `pageSize` nằm trong khoảng 1–100. API trả danh sách tại response body và tổng số bản ghi trong header `X-Total-Count`. Endpoint summary sử dụng cùng phạm vi `employees.read` nhưng không nhận tham số phân trang.

Các vai trò trong hệ thống gồm:

- `HR`
- `Manager`
- `Employee`

HR chịu trách nhiệm quản lý tổ chức và danh mục cấu hình. Chỉ HR và Manager có thể truy cập hộp thư phê duyệt và thực hiện quyết định phê duyệt; ngoài ra hệ thống còn kiểm tra Manager có thực sự được phân công xử lý yêu cầu đó hay không.

Refresh token được thiết lập dưới dạng **HttpOnly Cookie** khi đăng nhập vào HR Service và không được trả về trong JSON.

---

# HR Service

| Phương thức và Route | Quyền truy cập | Mục đích |
|---|---|---|
| `POST /api/v1/auth/login` | Không cần đăng nhập | Xác thực người dùng; trả về access token và thiết lập refresh cookie |
| `POST /api/v1/auth/refresh` | Refresh cookie | Làm mới refresh token và trả về access token mới |
| `POST /api/v1/auth/logout` | Refresh cookie | Thu hồi refresh token và xóa cookie |
| `GET /api/v1/me` | Mọi người dùng đã xác thực | Lấy thông tin hồ sơ HR của người dùng hiện tại |
| `GET /api/v1/me/team` | Manager | Lấy danh sách nhân viên đang hoạt động được phân công trực tiếp cho Manager hiện tại; không cho phép tìm kiếm toàn bộ tổ chức hoặc chỉnh sửa dữ liệu |
| `PUT /api/v1/me/password` | Người dùng đã xác thực | Thay đổi mật khẩu của chính mình |
| `GET /api/v1/departments[/{id}]` | Đã xác thực | Xem phòng ban |
| `POST /api/v1/departments` | HR | Tạo phòng ban |
| `PUT /api/v1/departments/{id}` | HR | Cập nhật phòng ban |
| `DELETE /api/v1/departments/{id}` | HR | Xóa phòng ban khi thỏa mãn các điều kiện nghiệp vụ |
| `GET /api/v1/employees?page=1&pageSize=20` | `employees.read` với scope `Self`, `Team` hoặc `All` | Xem danh sách nhân viên trong phạm vi được cấp; hỗ trợ tìm kiếm, lọc và sắp xếp |
| `GET /api/v1/employees/summary` | `employees.read` với scope `Self`, `Team` hoặc `All` | Lấy tổng số nhân viên, số đang hoạt động và số Manager đang hoạt động trong cùng phạm vi quyền |
| `GET /api/v1/employees/{id}` | `employees.read` với scope phù hợp | Xem thông tin một nhân viên thuộc phạm vi được cấp |
| `POST /api/v1/employees` | Permission tạo nhân viên | Tạo nhân viên; mã nhân viên được hệ thống tự động tạo |
| `PUT /api/v1/employees/{id}` | HR | Cập nhật nhân viên |
| `POST /api/v1/employees/{id}/deactivate` | HR | Vô hiệu hóa nhân viên và thu hồi refresh token |
| `PUT /api/v1/employees/{id}/password` | HR | Đặt lại mật khẩu nhân viên |
| `GET /api/v1/lookup/employees/{id}` | Service đã xác thực | Lấy snapshot thông tin nhân viên cho Leave Service |
| `GET /api/v1/lookup/employees/{managerId}/team` | Service đã xác thực | Tra cứu danh sách nhân viên thuộc Manager |
| `GET /api/v1/leave-types[/{id}]` | Đã xác thực | Xem danh mục loại nghỉ |
| `POST /api/v1/leave-types` | HR | Khôi phục một loại nghỉ mặc định bị thiếu (`ANNUAL`, `SICK`, `UNPAID`); không cho phép tạo loại nghỉ tùy ý |
| `PUT /api/v1/leave-types/{id}` | HR | Cập nhật loại nghỉ |
| `GET /api/v1/leave-policies[/{id}]` | Đã xác thực | Xem chính sách nghỉ; hỗ trợ query parameter `year` và/hoặc `authoritative` |
| `POST /api/v1/leave-policies` | HR | Thêm chính sách theo năm bị thiếu cho loại nghỉ mặc định đang hoạt động và có trừ số dư; chỉ cho phép năm tương lai; không cho phép tạo trùng hoặc tạo policy cho `UNPAID` |
| `PUT /api/v1/leave-policies/{id}` | HR | Chỉ cập nhật policy khi cả năm của policy hiện tại và năm được yêu cầu đều là năm tương lai |
| `GET /api/v1/holidays[/{id}]` | Đã xác thực | Xem ngày nghỉ lễ |
| `POST /api/v1/holidays` | HR | Tạo ngày nghỉ lễ |
| `PUT /api/v1/holidays/{id}` | HR | Cập nhật ngày nghỉ lễ |
| `DELETE /api/v1/holidays/{id}` | HR | Xóa ngày nghỉ lễ |

`GET /api/v1/leave-types` hỗ trợ:

- `includeInactive` — chỉ HR được sử dụng.
- `authoritative` — đọc dữ liệu trực tiếp từ nguồn chính.

Khi:

```text
authoritative=true
```

hệ thống sẽ bỏ qua Redis và đọc dữ liệu trực tiếp từ SQL.

---

# Leave Service

| Phương thức và Route | Quyền truy cập | Mục đích |
|---|---|---|
| `GET /api/v1/leave-requests` | Đã xác thực | Lấy các đơn nghỉ mà người dùng có quyền xem |
| `GET /api/v1/leave-requests/calendar?from=YYYY-MM-DD&to=YYYY-MM-DD` | Đã xác thực | Lấy dữ liệu đơn nghỉ cho lịch, có thể giới hạn theo khoảng thời gian |
| `GET /api/v1/leave-requests/{id}` | Đã xác thực | Xem chi tiết đơn nghỉ theo quy tắc phân quyền |
| `GET /api/v1/leave-requests/mine` | Đã xác thực | Lấy các đơn nghỉ của người dùng hiện tại |
| `POST /api/v1/leave-requests` | Nhân viên đã xác thực | Gửi đơn xin nghỉ |
| `GET /api/v1/leave-requests/pending-approval` | Manager, HR | Xem hộp thư các đơn đang chờ phê duyệt |
| `POST /api/v1/leave-requests/{id}/approve` | Manager hoặc HR được phân công | Phê duyệt đơn nghỉ |
| `POST /api/v1/leave-requests/{id}/reject` | Manager hoặc HR được phân công | Từ chối đơn nghỉ |
| `POST /api/v1/leave-requests/{id}/cancel` | Chủ đơn đã xác thực | Hủy đơn nghỉ khi thỏa mãn điều kiện |
| `GET /api/v1/leave-balances/mine?year=2026` | Đã xác thực | Xem số dư phép của người dùng hiện tại theo từng loại nghỉ |

Hiện tại hệ thống **không có endpoint riêng cho báo cáo hoặc xuất dữ liệu**.

Các chỉ số trên Dashboard được tổng hợp từ các API hiện có về:

- Nhân viên
- Đơn nghỉ
- Số dư phép
- Lịch nghỉ

Các chức năng Export và Notification hiển thị trên UI hiện chưa được triển khai.

---

# Ví dụ Request và Response

## Đăng nhập

```http
POST /api/v1/auth/login
Content-Type: application/json
```

```json
{
  "email": "employee@example.test",
  "password": "<configured-password>"
}
```

Response:

```json
{
  "accessToken": "<JWT>",
  "expiresAtUtc": "2026-11-03T07:00:00Z",
  "fullName": "Demo Employee",
  "email": "employee@example.test",
  "userId": "00000000-0000-0000-0000-000000000001",
  "role": "Employee"
}
```

Response đồng thời thiết lập HttpOnly Cookie:

```text
hr_leave_refresh
```

Không nên sao chép token thật vào:

- Log
- Ticket
- Tài liệu
- Screenshot công khai

---

## Tạo đơn xin nghỉ

```http
POST /api/v1/leave-requests
Authorization: Bearer <JWT>
Content-Type: application/json
```

Ví dụ:

```json
{
  "leaveTypeId": 1,
  "startDate": "2026-11-09",
  "endDate": "2026-11-09",
  "reason": "Personal leave"
}
```

Khi tạo thành công, API trả về:

```http
201 Created
```

với `LeaveRequestResponse`:

```json
{
  "id": "00000000-0000-0000-0000-000000000002",
  "employeeId": "00000000-0000-0000-0000-000000000001",
  "approverId": "00000000-0000-0000-0000-000000000003",
  "departmentId": 1,
  "leaveTypeId": 1,
  "startDate": "2026-11-09",
  "endDate": "2026-11-09",
  "totalDays": 1.0,
  "reason": "Personal leave",
  "status": 0,
  "createdAtUtc": "2026-11-03T07:00:00Z",
  "balances": [
    {
      "leaveTypeId": 1,
      "year": 2026,
      "entitled": 12.0,
      "used": 0.0,
      "pending": 1.0,
      "available": 11.0
    }
  ]
}
```

`status` sử dụng enum `LeaveRequestStatus`:

| Giá trị | Trạng thái |
|---:|---|
| `0` | Pending — Đang chờ xử lý |
| `1` | Approved — Đã phê duyệt |
| `2` | Rejected — Đã từ chối |
| `3` | Cancelled — Đã hủy |

Các giá trị và số dư trả về có thể thay đổi tùy theo trạng thái thực tế của database và chính sách nghỉ của người dùng.

---

## Phê duyệt hoặc từ chối đơn

API phê duyệt và từ chối hỗ trợ ghi chú tùy chọn:

```json
{
  "note": "Đã kiểm tra và xác nhận"
}
```

---

# Quy ước lỗi

Các lỗi của ứng dụng sử dụng định dạng:

```text
application/problem+json
```

Ví dụ:

```json
{
  "type": "about:blank",
  "title": "Validation failed",
  "status": 422,
  "detail": "Invalid request data",
  "errors": {
    "startDate": ["Start date must not be in the past."]
  }
}
```

Ý nghĩa các mã HTTP:

| HTTP Status | Ý nghĩa |
|---:|---|
| `400` | Request không đúng định dạng hoặc lỗi validation khi binding dữ liệu |
| `401` | Thiếu token, token hết hạn/không hợp lệ hoặc thông tin đăng nhập sai |
| `403` | Người dùng đã đăng nhập nhưng không có quyền hoặc không thỏa mãn điều kiện nghiệp vụ |
| `404` | Không tìm thấy dữ liệu hoặc người dùng không có quyền xem |
| `409` | Dữ liệu bị trùng, trạng thái xung đột hoặc có thao tác nghiệp vụ chồng chéo |
| `422` | Dữ liệu hoặc quy tắc nghiệp vụ không hợp lệ; response có thể chứa `errors` |
| `503` | Leave Service không thể truy cập HR Service cần thiết |
| `500` | Lỗi hệ thống không mong muốn; chi tiết được ghi trong server log |

Các trường `title` và `detail` có thể khác nhau tùy endpoint và không nên được xem là mã định danh cố định cho client.

Client nên xử lý dựa trên:

- HTTP Status Code
- Các field validation trong `errors`

---

# Danh mục loại nghỉ

Danh mục loại nghỉ được cố định gồm:

- `ANNUAL` — Nghỉ phép năm
- `SICK` — Nghỉ ốm
- `UNPAID` — Nghỉ không lương

Các API tạo dữ liệu của HR chỉ cho phép:

- Khôi phục một loại nghỉ mặc định bị thiếu.
- Thêm một policy bị thiếu cho loại nghỉ đang hoạt động và có trừ số dư.

Hệ thống **không cho phép**:

- Tạo loại nghỉ tùy ý.
- Tạo thêm loại nghỉ ngoài ba loại mặc định.
- Tạo policy số ngày phép cho `UNPAID`.

---

# Quy tắc năm của Leave Policy

Hệ thống sử dụng thời gian nghiệp vụ theo múi giờ Việt Nam:

```text
UTC+07:00
```

HR chỉ có thể **tạo hoặc chỉnh sửa Leave Policy cho năm tương lai**, tức là năm phải lớn hơn năm nghiệp vụ hiện tại.

Policy của:

- Năm hiện tại
- Các năm trong quá khứ

đều ở trạng thái **chỉ xem (read-only)**.

Hệ thống cũng ngăn trường hợp cố gắng vượt qua quy tắc bằng cách sửa một policy cũ rồi đổi `year` trong request thành năm tương lai.

Backend kiểm tra cả:

1. Năm hiện tại đang được lưu của policy.
2. Năm mới được gửi trong request.

Policy hiện tại phải là policy của năm tương lai thì mới được phép cập nhật.

Ví dụ, trong năm **2026**:

| Năm Policy | Có thể tạo | Có thể sửa |
|---|---|---|
| 2025 trở về trước | Không | Không |
| 2026 | Không | Không |
| 2027 trở đi | Có | Có |

Sau khi policy được tạo hoặc cập nhật thành công, hệ thống sẽ **vô hiệu hóa (invalidate) Redis cache của Leave Policy**.

Redis chỉ đóng vai trò:

> Cache tối ưu hiệu năng, không phải nguồn dữ liệu chính.

Nguồn dữ liệu chính vẫn là:

```text
SQL Server
```

Khi Redis gặp sự cố, hệ thống vẫn phải tiếp tục hoạt động bằng cách đọc dữ liệu từ nguồn chính.