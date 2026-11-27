# Hướng dẫn sử dụng, kiểm thử và demo

Tài liệu này dành cho người dùng và người trình diễn hệ thống HR Leave
Management. Hướng dẫn triển khai chi tiết, cấu hình `.env`, địa chỉ service và
xử lý lỗi nằm trong [README dự án](../README.md).

## 1. Chọn cách chạy

### Chế độ dùng thử giao diện

Chế độ này phù hợp để xem nhanh giao diện và luồng theo vai trò, không cần
backend. Khi `VITE_DEMO_MODE=true`, đăng nhập bằng các tài khoản mẫu:

| Vai trò | Email | Mật khẩu |
|---|---|---|
| HR | `hr@company.com` | `Demo123!` |
| Manager | `manager@company.com` | `Demo123!` |
| Employee | `employee@company.com` | `Demo123!` |

Giao diện có nút chọn nhanh tài khoản ở trang đăng nhập. Dữ liệu demo được lưu
trong trình duyệt (`localStorage`), không ghi vào SQL Server và không kiểm tra
xác thực JWT của backend. Chỉ dùng các tài khoản này khi giao diện hiển thị
nhãn **Chế độ dùng thử**.

### Chế độ API thật

Chế độ này kết nối frontend với HR Service, Leave Service, SQL Server và Redis.
Tạo `.env` từ `.env.example`, đặt mật khẩu/khóa riêng và bật seed tài khoản HR
cùng dữ liệu demo Manager/Employee nếu đây là database mới. Ví dụ biến cần tự
cấu hình:

```dotenv
INITIAL_HR_ENABLED=true
INITIAL_HR_EMAIL=<email-hr-cua-ban>
INITIAL_HR_PASSWORD=<mat-khau-rieng-tu-12-den-30-ky-tu>
INITIAL_HR_FULL_NAME=HR Administrator
INITIAL_HR_CODE=HR001

DEMO_DATA_ENABLED=true
DEMO_MANAGER_EMAIL=<email-manager-cua-ban>
DEMO_MANAGER_PASSWORD=<mat-khau-rieng-tu-12-den-30-ky-tu>
DEMO_MANAGER_FULL_NAME=Demo Manager
DEMO_MANAGER_CODE=MGR001
DEMO_EMPLOYEE_EMAIL=<email-employee-cua-ban>
DEMO_EMPLOYEE_PASSWORD=<mat-khau-rieng-tu-12-den-30-ky-tu>
DEMO_EMPLOYEE_FULL_NAME=Demo Employee
DEMO_EMPLOYEE_CODE=EMP001

VITE_DEMO_MODE=false
```

Thay tất cả giá trị trong dấu `<...>` bằng thông tin riêng, không dùng dấu
ngoặc trong `.env`. Manager và Employee cần có email, mã nhân viên khác nhau.
Không đưa `.env` hoặc mật khẩu thật lên Git.

Từ PowerShell tại thư mục gốc dự án, khởi chạy:

```powershell
Copy-Item .env.example .env
notepad .env
docker compose up --build -d
docker compose ps
```

Đợi các service ở trạng thái healthy, sau đó mở
[http://localhost:3000](http://localhost:3000). Đăng nhập bằng các tài khoản đã
cấu hình trong `.env`. Nếu thay đổi `VITE_DEMO_MODE`, cần build lại web:

```powershell
docker compose build --no-cache web
docker compose up -d web
```

Chỉ chạy seed demo trên môi trường local/test phù hợp. Không bật tài khoản mẫu
hoặc dùng mật khẩu mẫu trong production. Để dừng stack nhưng giữ dữ liệu:
`docker compose down`. Lệnh `docker compose down -v` xóa volume SQL/Redis; chỉ
dùng khi chủ ý xóa dữ liệu local.

### Swagger UI — xem và thử API

Khi backend chạy với `ASPNETCORE_ENVIRONMENT=Development`, mở:

- [HR Swagger UI](http://localhost:8081/swagger)
- [Leave Swagger UI](http://localhost:8082/swagger)

Chọn endpoint để xem request/response schema. Với endpoint cần đăng nhập, gọi HR `POST /api/v1/auth/login` trước, lấy access token từ response rồi chọn **Authorize** và nhập `Bearer <access-token>`. Refresh token được lưu trong HttpOnly cookie; không chia sẻ token/cookie hoặc chụp màn hình có bí mật.

OpenAPI JSON:
- HR: `http://localhost:8081/openapi/v1.json`
- Leave: `http://localhost:8082/openapi/v1.json`

Swagger/OpenAPI chỉ được bật ở Development; nếu không truy cập được, kiểm tra `ASPNETCORE_ENVIRONMENT` trong `.env` và khởi động lại service.

## 2. Thao tác theo vai trò

Các mục menu, route và thao tác được hiển thị theo permission/scope hiệu lực trong JWT, không chỉ dựa trên nhãn Role. Các vai trò Employee, Manager và HR dưới đây là cấu hình baseline thường gặp; tài khoản có nhiều Role có thể có tập quyền kết hợp khác nhau.

### HR

1. Vào **Phòng ban** để tạo hoặc sửa phòng ban trước khi thêm nhân viên.
2. Vào **Nhân viên** → **Thêm nhân viên**. Nhập họ tên, email, vai trò hiển thị legacy, phòng ban, ngày vào làm và người quản lý trực tiếp nếu có. Mã nhân viên được hệ thống tự sinh; không cần nhập mã. Có thể tìm kiếm, lọc, phân trang, sửa hồ sơ hoặc vô hiệu hóa tài khoản. Trường vai trò trên form nhân viên không thay thế thao tác gán Role động trong `UserRoles`.
3. Vào **Chính sách nghỉ phép** để quản lý ba loại nghỉ cố định:
   `ANNUAL` (nghỉ phép năm), `SICK` (nghỉ ốm), `UNPAID` (nghỉ không lương).
   Màn hình mặc định hiển thị các chính sách đang có hiệu lực tại ngày hiện tại
   (UTC+07:00). Bộ lọc chung có hai chế độ: **Đang có hiệu lực** và
   **Tất cả lịch sử**. Chế độ lịch sử nhóm version theo từng loại nghỉ; từ thẻ
   ANNUAL hoặc SICK có thể mở lịch sử riêng, xem ngày hiệu lực, số ngày, lý do
   và nhận biết version hiện hành. UNPAID không quản lý quota nên không có version.
   Khi tạo phiên bản mới, modal hiển thị quota/ngày hiệu lực hiện tại, cho chọn
   **Năm áp dụng**, số ngày, **Ngày hiệu lực** và lý do thay đổi. Lịch sử
   bao gồm mọi năm; số thứ tự version được đánh lại riêng cho từng loại nghỉ
   trong từng năm.
   Năm hiện tại chỉ nhận ngày hiệu lực từ hôm nay; năm tương lai chỉ nhận ngày
   từ 01/01 của chính năm đó. Không cho đặt hiệu lực hồi tố hoặc sang năm khác.
   Với mỗi năm phân bổ, nếu năm đó đã bắt đầu thì đơn dùng phiên bản có hiệu lực
   tại ngày gửi đơn; nếu đơn được gửi trước khi năm phân bổ bắt đầu, hệ thống
   dùng mốc 01/01 của năm đó. Phiên bản đã áp dụng được lưu snapshot, phiên bản
   cũ không bị ghi đè. Nếu thiếu một loại chuẩn, HR có thể khôi phục loại đó
   cho năm hiện tại hoặc năm tương lai.
   Tab **Ngày nghỉ lễ** dùng để thêm, sửa hoặc xóa ngày lễ.
4. Dùng **Tất cả đơn nghỉ** để theo dõi các đơn trong tổ chức và
   **Hộp thư phê duyệt** để xử lý đơn chờ.

### Nhân viên

1. **Tổng quan** hiển thị thông tin và số dư theo quyền của tài khoản.
2. Chọn **Tạo đơn nghỉ**, chọn loại nghỉ, ngày bắt đầu/kết thúc và nhập lý do,
   sau đó chọn **Gửi yêu cầu**. Ngày nghỉ phải từ hôm nay trở đi và có ít nhất
   một ngày làm việc; cuối tuần/ngày lễ không tính vào thời lượng. Hệ thống
   kiểm tra lại số dư, ngày trùng và các quy tắc ở backend.
3. Theo dõi trạng thái trong **Đơn nghỉ của tôi**; đơn đang chờ duyệt có thể
   được hủy theo các điều kiện của hệ thống.
4. **Lịch nghỉ của nhóm** hiển thị lịch nghỉ; **Hồ sơ của tôi** hiển thị hồ sơ
   tài khoản.

### Quản lý

1. Dùng **Hộp thư phê duyệt** để xem các đơn được giao cho mình. Mở yêu cầu,
   kiểm tra nhân viên, loại nghỉ, khoảng ngày và lý do; có thể nhập ghi chú
   không bắt buộc rồi chọn **Phê duyệt đơn** hoặc **Từ chối**.
2. Mở **Nhân viên phụ trách** để tra cứu các nhân viên được HR phân công trực tiếp cho mình. Có thể tìm theo tên, email, mã nhân viên hoặc phòng ban; chọn **Xem hồ sơ** để xem thông tin cơ bản như vai trò, phòng ban, ngày vào làm và trạng thái. Danh sách chỉ bao gồm nhân viên đang hoạt động báo cáo trực tiếp; đây là quyền chỉ đọc, không có thao tác thêm, sửa hoặc vô hiệu hóa.
3. Dùng **Lịch nghỉ của nhóm** để xem lịch đội ngũ.
4. Quản lý cũng có thể tạo đơn nghỉ cho chính mình và theo dõi đơn của mình.

HR có thể xem và xử lý mọi đơn đang chờ; quản lý chỉ xử lý đơn được phân công.


## Lưu ý về phân quyền hiện tại

Backend sử dụng Dynamic RBAC: các thao tác API được kiểm tra theo permission và data scope trong JWT (Self, Team, All). HR là nguồn quản lý Role assignment và quan hệ quản lý hiện tại; Leave Service gọi HR để xác minh Team khi cần.

React đọc `authz_v=1` và `perm` để hiển thị menu, bảo vệ route và ẩn/hiện từng thao tác theo permission/scope. Nhãn HR/Manager/Employee vẫn có thể xuất hiện trong hồ sơ và danh sách nhân viên như thông tin vai trò legacy; nhãn này không tự quyết định quyền.

- Một API call trực tiếp vẫn phải thỏa permission/scope backend; việc thấy hoặc không thấy menu không cấp quyền.
- API gán/xem/thu hồi UserRole đã có ở backend nhưng chưa có trang React quản trị.
- Role/Permission catalog CRUD và cấu hình Role grants chưa có UI/API quản trị tổng quát.
- `Employee.Role` vẫn được giữ cho tương thích, nhãn hiển thị, lọc/thống kê và một số nghiệp vụ legacy; quyền hiệu lực lấy từ UserRole assignments.
- Access JWT đã phát vẫn có hiệu lực đến khi hết hạn; gán/thu hồi Role không cập nhật token đang tồn tại tại chỗ.

## 3. Kịch bản demo nhanh

### Demo giao diện (không cần backend)

1. Mở ứng dụng đã build ở chế độ `VITE_DEMO_MODE=true`.
2. Đăng nhập HR bằng nút tài khoản mẫu, xem **Tổng quan**, **Nhân viên**,
   **Phòng ban**, **Chính sách nghỉ phép** và **Lịch nghỉ của nhóm**.
3. Đăng xuất, chọn tài khoản Employee, tạo một đơn nghỉ có ngày hợp lệ và lý
   do; xem đơn trong **Đơn nghỉ của tôi**.
4. Đăng xuất, chọn tài khoản Manager, mở **Nhân viên phụ trách** để kiểm tra
   danh sách nhân viên báo cáo trực tiếp, sau đó mở **Hộp thư phê duyệt** để
   xem xét đơn rồi phê duyệt hoặc từ chối.
5. Đăng nhập lại Employee để xác nhận trạng thái đã cập nhật.

Chế độ dùng thử lưu dữ liệu theo trình duyệt; làm mới trang không đồng nghĩa
với việc khôi phục dữ liệu ban đầu. Đăng xuất giữa mỗi vai trò để kiểm tra phân
quyền.

### Demo API thật (đầy đủ backend)

1. Bật `INITIAL_HR_ENABLED=true`, `DEMO_DATA_ENABLED=true` và
   `VITE_DEMO_MODE=false` trong `.env`; dùng email/mật khẩu riêng cho từng tài
   khoản.
2. Khởi chạy Compose và chờ service healthy theo mục 1.
3. Đăng nhập Employee, gửi đơn nghỉ cho một ngày làm việc trong tương lai. Chọn
   loại có đủ số dư, nhập lý do; tránh ngày lễ đã cấu hình.
4. Đăng nhập Manager được gán cho Employee đó, mở **Hộp thư phê duyệt** và
   phê duyệt đơn.
5. Đăng nhập Employee lần nữa, xác nhận trạng thái đơn; kiểm tra số dư nếu loại
   nghỉ có trừ hạn mức.
6. Nếu cần minh họa quản trị, đăng nhập HR để xem nhân viên/phòng ban, chính
   sách hoặc lịch ngày lễ.

Ngày demo phải là ngày làm việc trong tương lai, không phải ngày nghỉ lễ đã
khai báo; không dùng đơn HR không thuộc tuyến phê duyệt Manager để trình diễn
luồng Manager.

## 4. Chạy kiểm thử

Chạy tại thư mục gốc repository. Backend cần .NET 10 SDK:

```powershell
dotnet test src\hr-service\HrService.slnx
dotnet test src\leave-service\LeaveService.slnx
```

Frontend cần Node.js 20 trở lên và npm. Có thể chạy trực tiếp:

~~~powershell
Push-Location src\web
npm ci
npm audit
npm test -- --run
npm run build
Pop-Location
~~~

Hoặc dùng Docker Node 20 nếu máy chưa cài npm:

~~~powershell
docker run --rm -v "${PWD}\src\web:/app" -w /app --tmpfs /app/node_modules:exec node:20-alpine sh -lc "npm ci && npm audit && npm test -- --run && npm run build"
~~~

`npm test -- --run` chạy Vitest một lần; `npm run build` chạy TypeScript check và
Vite production build.

Một số integration test backend cần SQL Server/Redis dành riêng cho kiểm thử và
các biến kết nối `HR_TEST_SQL_CONNECTION_STRING`,
`HR_TEST_REDIS_CONNECTION_STRING`. Nếu thiếu phụ thuộc, một số test có thể bị
skip; không xem test bị skip là đã xác nhận tích hợp thành công. Test tích hợp
có thể tạo/xóa database hoặc dữ liệu; không trỏ tới production hay database có
dữ liệu cần giữ. Hướng dẫn và bằng chứng kiểm thử chi tiết xem tại
[kịch bản demo và nghiệm thu](testing/demo-and-acceptance.md).

## 5. Kiểm tra sau khi chạy

- Mở `http://localhost:3000`, đăng nhập đúng chế độ và đúng vai trò.
- Kiểm tra health endpoint: `http://localhost:8081/health` (HR) và
  `http://localhost:8082/health` (Leave).
- Dùng Swagger UI ở `http://localhost:8081/swagger` (HR) và
  `http://localhost:8082/swagger` (Leave) khi chạy Development.
- Nếu đăng nhập demo không gọi được backend, xác nhận web có nhãn **Chế độ dùng
  thử**; nếu cần dữ liệu thật, rebuild web với `VITE_DEMO_MODE=false`.
- Nếu API lỗi, xem log bằng `docker compose logs --tail 100 hr-service
  leave-service web`; kiểm tra SQL Server/Redis bằng `docker compose ps`.
- Tài khoản được seed phải đăng nhập bằng mật khẩu đã cấu hình; chế độ API thật
  không sử dụng mật khẩu `Demo123!` của demo UI.
