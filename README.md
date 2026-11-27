# HR Leave Management

Hệ thống quản lý nhân sự và nghỉ phép, được xây dựng theo kiến trúc Clean Architecture với hai backend .NET, frontend React và các dịch vụ hạ tầng chạy bằng Docker Compose.

## Trạng thái triển khai hiện tại

Dynamic Authorization V1 đã được tích hợp vào `master` qua [PR #6](https://github.com/OlYVER1337/hr-leave-management/pull/6). Permission-driven React UI đã được merge qua [PR #18](https://github.com/OlYVER1337/hr-leave-management/pull/18), merge commit `d69156aa8990e2d92d21ad0dac1f9e0a7e1d2c5e`. Đây là trạng thái source trên Git, không phải xác nhận triển khai production.

Backend HR/Leave sử dụng Dynamic RBAC: HR_DB quản lý Roles, Permissions, UserRoles, Scope rules và audit log; login/refresh tính lại quyền hiệu lực rồi phát JWT với `authz_v=1` và claim `perm`. Các scope được thực thi là Self, Team, All; Department có trong schema nhưng chưa có bộ lọc nghiệp vụ.

React đã đọc permission/scope từ JWT để điều khiển menu, route guard và từng action; quyền được dựng lại khi restore session và cập nhật sau refresh. Role claim và `Employee.Role` vẫn được giữ cho tương thích/hiển thị và một số chức năng quản lý nhân viên, nhưng không phải nguồn quyết định quyền truy cập. Backend vẫn là lớp enforcement cuối cùng.

`Employee.Role` chưa bị xóa: hiện còn phục vụ nhãn vai trò, lọc/thống kê danh sách nhân viên, DTO tạo/cập nhật, một số kiểm tra nghiệp vụ legacy và backfill ban đầu sang `UserRoles`. Giá trị này không tự động đại diện cho toàn bộ Role assignment hoặc effective permission của nhân viên.

Nhánh `feature/consolidate-employees-team-api` đang hợp nhất danh sách nhân viên và direct reports: trang `/my-team` dùng `GET /api/v1/employees?directReportsOnly=true`; endpoint cũ `GET /api/v1/me/team` đã bị loại khỏi controller/runtime và OpenAPI trên nhánh này. Thay đổi API này chưa được merge vào `master`; trạng thái kiểm thử và nghiệm thu được ghi trong [Handoff checklist](docs/HANDOFF.md). Chưa có UI quản trị UserRole assignment hoặc CRUD catalog Role/Permission.

Leave Service lưu requests, balances, yearly allocations và histories trong Leave_DB. Nhân viên có thể khởi tạo/đồng bộ balance của chính mình theo HR catalog authoritative qua `POST /api/v1/leave-balances/mine/synchronize`; cả GET và POST balance đều đồng bộ PolicyDays hiện hành từ HR trước khi trả dữ liệu. Request lưu PolicyVersionId và PolicyDaysAtCreation theo từng năm; Entitled được tính bằng `max(PolicyDays, Used + Pending)` để giữ nguyên usage lịch sử khi quota giảm. Luồng tạo đơn, duyệt, từ chối và hủy đều refresh quota trong transaction cập nhật Used/Pending. Chưa có API HR bulk reconciliation cho toàn bộ nhân viên. Lịch sử thao tác được lưu trong `LeaveRequestHistories` và trả kèm response của `GET /api/v1/leave-requests/{id}`; không có endpoint history riêng.

Các khoảng trống và thứ tự hoàn thiện được theo dõi tại [Backlog](docs/planning/BACKLOG.md). Mô tả kiến trúc, database, authorization và API chi tiết nằm trong [docs index](docs/README.md).

## Mục lục

- [Bắt đầu nhanh (API thật)](#bắt-đầu-nhanh-api-thật)
- [1. Công nghệ sử dụng](#1-công-nghệ-sử-dụng)
- [2. Kiến trúc và cấu trúc thư mục](#2-kiến-trúc-và-cấu-trúc-thư-mục)
- [3. Yêu cầu môi trường](#3-yêu-cầu-môi-trường)
- [4. Cấu hình lần đầu](#4-cấu-hình-lần-đầu)
- [5. Chạy toàn bộ hệ thống bằng Docker Compose](#5-chạy-toàn-bộ-hệ-thống-bằng-docker-compose)
- [6. Chế độ demo và chế độ API thật](#6-chế-độ-demo-và-chế-độ-api-thật)
- [7. Địa chỉ truy cập và kiểm tra dịch vụ](#7-địa-chỉ-truy-cập-và-kiểm-tra-dịch-vụ)
- [8. Chạy automated tests](#8-chạy-automated-tests)
- [9. Lệnh Docker thường dùng](#9-lệnh-docker-thường-dùng)
- [10. Xử lý lỗi thường gặp](#10-xử-lý-lỗi-thường-gặp)
- [11. Lưu ý bảo mật](#11-lưu-ý-bảo-mật)

## Bắt đầu nhanh (API thật)

Dành cho lần chạy đầu trên máy local. Docker Compose mặc định build frontend ở chế độ API thật (`VITE_DEMO_MODE=false`).

1. Clone repository và mở PowerShell tại thư mục dự án:

   ~~~powershell
   git clone https://github.com/OlYVER1337/hr-leave-management.git
   Set-Location .\hr-leave-management
   ~~~

2. Tạo file cấu hình local:

   ~~~powershell
   Copy-Item .env.example .env
   notepad .env
   ~~~

3. Trong `.env`, đặt `SA_PASSWORD` và `JWT_KEY` riêng. Với database mới, bật `INITIAL_HR_ENABLED=true` và điền thông tin HR. Để thực hành đủ ba vai trò, bật `DEMO_DATA_ENABLED=true` rồi điền email/mật khẩu riêng cho Manager và Employee. Giữ `VITE_DEMO_MODE=false`.

4. Build và khởi chạy:

   ~~~powershell
   docker compose up --build -d
   docker compose ps
   ~~~

5. Kiểm tra hai backend:

   ~~~powershell
   Invoke-RestMethod http://localhost:8081/health
   Invoke-RestMethod http://localhost:8082/health
   ~~~

6. Mở [http://localhost:3000](http://localhost:3000). Đăng nhập bằng thông tin HR/Manager/Employee đã đặt trong `.env`.

> Chỉ bật seed tài khoản trên database local/test mới hoặc database bạn chủ động quản lý. Không dùng mật khẩu ví dụ ở môi trường thật. Nếu database đã có tài khoản HR, không cần tạo lại HR khởi tạo.

---

## 1. Công nghệ sử dụng

| Thành phần | Công nghệ | Vai trò |
|---|---|---|
| Frontend | React, TypeScript, Vite, Axios | Giao diện quản trị và gửi HTTP request đến API |
| Backend HR | ASP.NET Core / .NET 10 | Đăng nhập, JWT, nhân viên, phòng ban và danh mục nhân sự |
| Backend nghỉ phép | ASP.NET Core / .NET 10 | Đơn nghỉ phép, phê duyệt, số dư và lịch sử nghỉ phép |
| ORM | Entity Framework Core | Truy vấn và lưu dữ liệu SQL Server |
| Database | Microsoft SQL Server 2022 | Lưu dữ liệu nghiệp vụ chính |
| Cache | Redis 7 | Cache dữ liệu danh mục để giảm truy vấn lặp lại |
| Container | Docker, Docker Compose | Chạy các thành phần trong môi trường thống nhất |
| Testing | xUnit, Moq, ASP.NET Core TestHost | Unit test, API integration test và database/cache integration test |

## 2. Kiến trúc và cấu trúc thư mục

Tài liệu bàn giao theo hạng mục: [System design](docs/architecture/SYSTEM_DESIGN.md),
[Database design](docs/architecture/DATABASE_DESIGN.md),
[API reference](docs/API_REFERENCE.md), [Design System](docs/design/DESIGN_SYSTEM.md),
[Redis/performance report](docs/performance/redis-performance.md) và
[Handover checklist](docs/HANDOFF.md).

```text
hr-leave-management/
├── docker-compose.yml
├── .env.example
├── docker/
│   └── dotnet.Dockerfile
├── docs/
│   ├── README.md
│   ├── requirements/       # Yêu cầu và mô tả hệ thống gốc
│   ├── architecture/       # Sơ đồ Mermaid, PNG và SVG
│   ├── design/             # Design System và wireframe
│   ├── performance/        # Báo cáo Redis/load test
│   ├── testing/            # Kết quả test, chẩn đoán và demo
│   └── planning/           # Backlog
├── src/
│   ├── hr-service/
│   │   ├── HrService.Api/
│   │   ├── HrService.Application/
│   │   ├── HrService.Domain/
│   │   ├── HrService.Infrastructure/
│   │   └── HrService.Tests/
│   ├── leave-service/
│   │   ├── LeaveService.Api/
│   │   ├── LeaveService.Application/
│   │   ├── LeaveService.Domain/
│   │   ├── LeaveService.Infrastructure/
│   │   └── LeaveService.Tests/
│   └── web/
│       ├── src/
│       ├── Dockerfile
│       ├── nginx.conf
│       └── package.json
└── README.md
```

Vai trò các layer trong mỗi backend:

- **Api**: Controllers, middleware, xác thực JWT và HTTP endpoints.
- **Application**: Services, DTOs và các interface Repository/Unit of Work.
- **Domain**: Entity, enum và mô hình nghiệp vụ cốt lõi.
- **Infrastructure**: EF Core, Repository implementations, Unit of Work, tích hợp Redis và các thành phần kỹ thuật.
- **Tests**: Kiểm tra nghiệp vụ và luồng tích hợp.

Các database được cấu hình riêng:

- `HR_DB`: dữ liệu nhân viên, phòng ban và danh mục HR.
- `Leave_DB`: dữ liệu đơn nghỉ phép, số dư và lịch sử xử lý.
- Redis dùng cache; không thay thế SQL Server làm nguồn dữ liệu chính.

## 3. Yêu cầu môi trường

Để chạy toàn bộ hệ thống theo cách khuyến nghị, cần:

- Docker Desktop đã cài đặt và đang chạy (Windows/macOS), hoặc Docker Engine và Docker Compose plugin (Linux).
- Git nếu clone source từ repository.
- Kết nối Internet trong lần đầu build để tải image Docker và package cần thiết.

Nếu chỉ chạy các test hoặc phát triển trực tiếp ngoài Docker, cần cài thêm .NET 10 SDK. Nếu chạy frontend trực tiếp bằng Vite, cần Node.js 20 trở lên và npm.

## 4. Cấu hình lần đầu

### 4.1. Mở thư mục gốc dự án

Mở PowerShell hoặc Terminal tại thư mục chứa `docker-compose.yml`.

### 4.2. Tạo file `.env`

PowerShell:

```powershell
Copy-Item .env.example .env
notepad .env
```

macOS/Linux:

```bash
cp .env.example .env
nano .env
```

`docker-compose.yml` đọc các biến trong file `.env`. Không commit file `.env` chứa mật khẩu hoặc khóa thật lên Git.

### 4.3. Cấu hình thông tin bắt buộc

Đặt mật khẩu SQL Server đủ mạnh và khóa ký JWT ngẫu nhiên có ít nhất 32 ký tự. Ví dụ dưới đây chỉ dành cho máy local/dev; hãy đổi các giá trị trước khi sử dụng:

```dotenv
SA_PASSWORD=Local_StrongSql_2026!
JWT_KEY=replace-this-with-a-random-secret-at-least-32-characters
ASPNETCORE_ENVIRONMENT=Development
CACHE_ENABLED=true
```

Trong `.env.example`, các biến này đã có sẵn. Giữ định dạng `KEY=value`, không thêm khoảng trắng ở tên biến.

### 4.4. Tạo tài khoản HR ban đầu

Nếu database mới chưa có người dùng HR, cấu hình các biến sau trong `.env` để bật tạo tài khoản HR khởi tạo:

```dotenv
INITIAL_HR_ENABLED=true
INITIAL_HR_EMAIL=hr.admin@company.com
INITIAL_HR_PASSWORD=ChangeThis_HR2026!
INITIAL_HR_FULL_NAME=HR Administrator
INITIAL_HR_CODE=HR001
INITIAL_HR_DEPARTMENT_NAME=HR
```

Đăng nhập bằng email và mật khẩu bạn đặt ở đây sau khi dịch vụ khởi động thành công. Nếu không cần tạo tài khoản HR khởi tạo, có thể để `INITIAL_HR_ENABLED=false` và phải bảo đảm database đã có tài khoản phù hợp.

### 4.5. (Tùy chọn) Seed dữ liệu demo

Nếu muốn tạo thêm một nhân viên Manager, một Employee cùng dữ liệu loại nghỉ/chính sách ban đầu, cấu hình `DEMO_DATA_ENABLED=true` và cung cấp thông tin đăng nhập cho hai tài khoản:

```dotenv
DEMO_DATA_ENABLED=true
DEMO_DATA_DEPARTMENT_NAME=Demo
DEMO_DATA_SICK_DAYS_PER_YEAR=10

DEMO_MANAGER_EMAIL=manager@company.com
DEMO_MANAGER_PASSWORD=ChangeThis_Manager2026!
DEMO_MANAGER_FULL_NAME=Demo Manager
DEMO_MANAGER_CODE=MGR001

DEMO_EMPLOYEE_EMAIL=employee@company.com
DEMO_EMPLOYEE_PASSWORD=ChangeThis_Employee2026!
DEMO_EMPLOYEE_FULL_NAME=Demo Employee
DEMO_EMPLOYEE_CODE=EMP001
```

Các mật khẩu trên là ví dụ. Hãy thay bằng mật khẩu riêng trên máy của bạn. Không dùng chúng trong production.

## 5. Chạy toàn bộ hệ thống bằng Docker Compose

Sau khi đã tạo và lưu `.env`, chạy ở thư mục gốc repository:

```bash
docker compose up --build -d
```

Lệnh này build và khởi động các container được khai báo trong Compose, bao gồm SQL Server, Redis, HR Service, Leave Service và frontend. Các backend được cấu hình chờ SQL Server/Redis healthy trước khi khởi động; Leave Service cũng phụ thuộc vào HR Service.

Kiểm tra trạng thái container:

```bash
docker compose ps
```

Xem log các service:

```bash
docker compose logs -f sqlserver redis hr-service leave-service web
```

Đợi các container ở trạng thái chạy/healthy và các backend hoàn tất khởi tạo database/migrations trước khi đăng nhập. Dừng theo dõi log bằng `Ctrl+C`; thao tác này không dừng container.

## 6. Chế độ demo và chế độ API thật

Frontend có hai cách hoạt động:

### Demo mode

Khi `VITE_DEMO_MODE=true`, frontend dùng dữ liệu demo được lưu trong `localStorage` của trình duyệt. Dữ liệu này không được ghi xuống SQL Server và luồng đăng nhập demo không thay thế JWT ở backend.

Tài khoản demo mặc định của giao diện là:

| Vai trò | Email | Mật khẩu |
|---|---|---|
| HR | `hr@company.com` | `Demo123!` |
| Manager | `manager@company.com` | `Demo123!` |
| Employee | `employee@company.com` | `Demo123!` |

Chỉ dùng các tài khoản trên khi demo mode được bật.

### API mode

Khi `VITE_DEMO_MODE=false`, frontend gọi API thật. Chế độ này yêu cầu backend đang chạy, tài khoản đã được tạo trong SQL Server và các URL API được cấu hình đúng.

Trong cấu hình Docker Compose, kiểm tra phần `web.build.args` của `docker-compose.yml` và bảo đảm `VITE_DEMO_MODE` là `false` khi muốn dùng backend thật. Sau khi thay đổi build argument, rebuild frontend:

```bash
docker compose build --no-cache web
docker compose up -d web
```

Nếu ứng dụng vẫn hiển thị nhãn **Demo mode**, frontend đang được build ở chế độ demo; hãy kiểm tra lại build argument rồi rebuild.

## 7. Địa chỉ truy cập và kiểm tra dịch vụ

| Thành phần | Địa chỉ local | Mục đích |
|---|---|---|
| Frontend | http://localhost:3000 | Giao diện người dùng |
| HR Service | http://localhost:8081 | API HR được expose ra máy host |
| HR health | http://localhost:8081/health | Kiểm tra trạng thái HR Service |
| HR Swagger UI | http://localhost:8081/swagger | Giao diện khám phá và gửi thử request HR API khi Development |
| HR OpenAPI JSON | http://localhost:8081/openapi/v1.json | Đặc tả OpenAPI của HR khi Development |
| Leave Service | http://localhost:8082 | API nghỉ phép được expose ra máy host |
| Leave health | http://localhost:8082/health | Kiểm tra trạng thái Leave Service |
| Leave Swagger UI | http://localhost:8082/swagger | Giao diện khám phá và gửi thử request Leave API khi Development |
| Leave OpenAPI JSON | http://localhost:8082/openapi/v1.json | Đặc tả OpenAPI của Leave khi Development |
| SQL Server | `localhost,1433` | Kết nối từ công cụ SQL trên máy host |
| Redis | `localhost:6379` | Kết nối từ công cụ Redis trên máy host |

Đăng nhập bằng tài khoản HR mà bạn khai báo trong `INITIAL_HR_EMAIL` và `INITIAL_HR_PASSWORD` nếu đã bật tạo tài khoản ban đầu. Nếu bật seed dữ liệu demo, các tài khoản Manager và Employee dùng thông tin tương ứng trong `.env`.

### Swagger UI và đăng nhập API

Khi `ASPNETCORE_ENVIRONMENT=Development`, mở:

- HR Swagger UI: [http://localhost:8081/swagger](http://localhost:8081/swagger)
- Leave Swagger UI: [http://localhost:8082/swagger](http://localhost:8082/swagger)

Dùng Swagger UI để xem route/schema và gửi thử API. Với endpoint cần xác thực, gọi HR `POST /api/v1/auth/login` trước; lấy access token trong response rồi nhập vào nút **Authorize** của Swagger UI theo dạng Bearer token. Refresh token được lưu trong HttpOnly cookie; không sao chép cookie này vào tài liệu hoặc Git.

OpenAPI JSON tương ứng là `/openapi/v1.json` trên từng service.

Endpoint đăng nhập HR:

```http
POST /api/v1/auth/login
Content-Type: application/json
```

```json
{
  "email": "hr.admin@company.com",
  "password": "ChangeThis_HR2026!"
}
```

Thay email và mật khẩu trong ví dụ bằng giá trị thật đã cấu hình. Khi API mode được bật, frontend sẽ nhận access token từ HR Service rồi gửi token trong header `Authorization: Bearer <token>` ở các request cần xác thực.

## 8. Chạy automated tests

### Backend — .NET 10 SDK

Chạy tại thư mục gốc repository. Unit tests có thể chạy trực tiếp bằng .NET CLI; để các integration tests dùng SQL Server và Redis thật, trước tiên khởi động stack kiểm thử cô lập `hrbench` (không dùng chung volume/cổng với stack local `hr-leave`):

~~~powershell
if (-not (Test-Path .env)) { throw "Chưa có .env. Hãy copy .env.example thành .env và cấu hình trước." }

$compose = @(
  "-p", "hrbench",
  "-f", "docker-compose.yml",
  "-f", "docker-compose.loadtest.yml"
)

docker compose --env-file .env @compose up --build -d
docker compose --env-file .env @compose ps

Invoke-RestMethod http://localhost:18081/health
Invoke-RestMethod http://localhost:18082/health
~~~

Compose đọc cấu hình từ file `.env` qua `--env-file .env`. Để test persistence kết nối đúng SQL Server/Redis của project `hrbench`, nạp mật khẩu SQL từ chính file này vào phiên PowerShell, sau đó thiết lập connection strings:

~~~powershell
$saPasswordLine = Get-Content .env | Where-Object { $_ -match '^SA_PASSWORD=' } | Select-Object -First 1
if (-not $saPasswordLine) { throw "Không tìm thấy SA_PASSWORD trong .env." }

$env:SA_PASSWORD = $saPasswordLine.Substring("SA_PASSWORD=".Length).Trim('"').Trim("'")
if ([string]::IsNullOrWhiteSpace($env:SA_PASSWORD)) { throw "SA_PASSWORD trong .env đang để trống." }

$env:HR_TEST_SQL_CONNECTION_STRING = "Server=localhost,11433;Database=master;User Id=sa;Password=$env:SA_PASSWORD;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5"
$env:HR_TEST_REDIS_CONNECTION_STRING = "localhost:16379,abortConnect=false,connectTimeout=5000"
~~~

Chạy hai solution và dừng ngay khi một suite có lỗi:

~~~powershell
dotnet test .\src\hr-service\HrService.slnx
if ($LASTEXITCODE -ne 0) { throw "HR Service tests failed" }

dotnet test .\src\leave-service\LeaveService.slnx
if ($LASTEXITCODE -ne 0) { throw "Leave Service tests failed" }
~~~

Một số integration test phụ thuộc SQL Server/Redis. Nếu thiếu connection string hoặc dịch vụ, test có thể bị `Skipped`; skipped không có nghĩa là tích hợp đã được xác minh. HR integration tests dùng Redis database 15; test persistence có thể tạo/xóa database kiểm thử. Chỉ dùng SQL Server/Redis riêng cho test, tuyệt đối không trỏ production.

Khi hoàn tất, có thể dừng riêng stack kiểm thử nhưng giữ nguyên volume/database:

~~~powershell
docker compose --env-file .env @compose down
~~~

Không thêm `-v` nếu cần giữ database để kiểm tra hoặc chạy test tiếp.

### Frontend — Node.js 20

Có thể chạy trực tiếp nếu máy đã cài Node.js 20 và npm:

~~~powershell
Push-Location .\src\web
npm ci
npm audit
npm test -- --run
npm run build
Pop-Location
~~~

Hoặc chạy bằng Docker, không cần cài npm trên máy host:

~~~powershell
docker run --rm -v "${PWD}\src\web:/app" -w /app --tmpfs /app/node_modules:exec node:20-alpine sh -lc "npm ci && npm audit && npm test -- --run && npm run build"
~~~

Lệnh chạy audit dependency, Vitest một lần và TypeScript/Vite production build. Nếu bất kỳ bước nào lỗi, dừng và xử lý trước khi coi frontend đã đạt.

## 9. Lệnh Docker thường dùng

Xem trạng thái:

```bash
docker compose ps
```

Xem log một dịch vụ:

```bash
docker compose logs -f hr-service
```

Khởi động lại dịch vụ:

```bash
docker compose restart hr-service
```

Dừng các container nhưng giữ lại dữ liệu trong volume:

```bash
docker compose down
```

Dừng và **xóa toàn bộ volume dữ liệu SQL Server/Redis** (chỉ dùng khi thực sự muốn reset môi trường local):

```bash
docker compose down -v
```

> Cảnh báo: `docker compose down -v` sẽ xóa dữ liệu được giữ trong volume của stack. Không chạy lệnh này nếu cần giữ database hiện tại.

## 10. Xử lý lỗi thường gặp

### Container SQL Server không healthy

- Kiểm tra `SA_PASSWORD` trong `.env` đáp ứng yêu cầu của SQL Server.
- Kiểm tra cổng `1433` chưa bị một dịch vụ khác chiếm.
- Xem log bằng `docker compose logs -f sqlserver`.

### Backend không kết nối được database

- Khi backend chạy **bên trong Docker Compose**, hostname database là `sqlserver`, không phải `localhost`.
- Khi chạy backend trực tiếp trên máy host, dùng địa chỉ host như `localhost,1433`.
- Kiểm tra `ConnectionStrings__Default` và mật khẩu SQL Server.

### Lỗi cấu hình JWT hoặc login trả 401

- Kiểm tra `JWT_KEY` có ít nhất 32 ký tự và được dùng thống nhất ở cả hai backend.
- Kiểm tra `Jwt__Issuer` và `Jwt__Audience` có cùng cấu hình giữa nơi phát hành token và nơi xác thực token.
- Bảo đảm tài khoản tồn tại, đang hoạt động và mật khẩu chính xác.
- Nếu frontend đang ở demo mode, đăng nhập demo không dùng JWT backend.

### Frontend mở được nhưng API lỗi

- Kiểm tra `hr-service` và `leave-service` đang chạy/healthy.
- Mở các URL `/health` trong bảng ở trên.
- Kiểm tra cấu hình API base URL/proxy trong `src/web/src/services/config.ts`, `src/web/nginx.conf` và build arguments.
- Nếu vừa thay đổi biến `VITE_*`, cần build lại frontend vì các biến này được đưa vào bundle lúc build.

### Redis không kết nối được

- Kiểm tra container Redis: `docker compose ps redis`.
- Xem log: `docker compose logs -f redis`.
- Trong Docker Compose, kết nối nội bộ dùng `redis:6379`; từ máy host thường dùng `localhost:6379`.

## 11. Lưu ý bảo mật

- Không commit `.env`, password, JWT signing key hay connection string chứa bí mật lên Git.
- Dùng khóa JWT ngẫu nhiên, đủ dài và riêng cho từng môi trường triển khai.
- Chỉ dùng chế độ `Development`, tài khoản demo và mật khẩu mẫu ở môi trường local.
- Không dùng database production cho integration test.
- Cấu hình HTTPS, secrets management, logging và backup phù hợp trước khi triển khai thực tế.

## Tài liệu liên quan

Tài liệu được phân loại theo chủ đề trong [`docs/`](docs/README.md). Bắt đầu từ
[mục lục tài liệu](docs/README.md) để tìm yêu cầu, sơ đồ, hướng dẫn, báo cáo và
checklist nghiệm thu. Xem [hướng dẫn sử dụng, chạy test và demo](docs/USER-GUIDE.md)
để thực hành các luồng theo vai trò.
