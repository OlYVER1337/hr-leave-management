# Tài liệu bàn giao hệ thống HR Leave Management

*Cập nhật: 26/11/2026*

Tài liệu dành cho người tiếp nhận source, dựng môi trường, kiểm thử, demo và kiểm tra giới hạn trước khi nghiệm thu. Nội dung tách biệt **tính năng đã có**, **bằng chứng đã chạy** và **việc chưa được xác minh**.

## 1. Repository và revision

| Thuộc tính | Giá trị |
|---|---|
| Repository | [OlYVER1337/hr-leave-management](https://github.com/OlYVER1337/hr-leave-management) |
| Branch source đã tích hợp | **master** |
| HEAD master tại thời điểm cập nhật | **d34ca9100f1db34261b7792dcc2058855bcd29aa** |
| Branch cập nhật tài liệu | **Update-docs** |
| Trạng thái tài liệu | Các thay đổi tài liệu đang ở Update-docs; chưa được tính là đã tích hợp vào master cho đến khi PR được merge. |
| Merge Dynamic Authorization V1 | [PR #6](https://github.com/OlYVER1337/hr-leave-management/pull/6), merge commit **81b987043ce4663ad11800bb331ffdbc013bd1d2** |
| Trạng thái production | Chưa có bằng chứng triển khai hoặc nghiệm thu production. |

**Quy tắc đối chiếu:** Approval benchmark ngày 26/11 chạy trên source SHA **d34ca91** và image ID ghi tại Mục 8. Không lấy HEAD của branch tài liệu thay cho revision benchmark. Khi tài liệu khác với implementation, đối chiếu source code và EF Core migrations.

## 2. Dựng hệ thống local lần đầu

### 2.1. Yêu cầu

- Docker Desktop hoặc Docker Engine có Docker Compose plugin.
- Git để clone repository.
- Internet trong lần build đầu để tải image và package.
- .NET 10 SDK nếu chạy backend tests trực tiếp trên host.
- Node.js 20+ và npm nếu chạy frontend tests trực tiếp. Không cần cài npm trên host nếu dùng Docker như Mục 7.

### 2.2. Clone source và tạo cấu hình

Chạy PowerShell:

~~~powershell
git clone https://github.com/OlYVER1337/hr-leave-management.git
Set-Location .\hr-leave-management
Copy-Item .env.example .env
notepad .env
~~~

Trong file .env, cấu hình:

| Biến | Cách cấu hình |
|---|---|
| SA_PASSWORD | Mật khẩu SQL Server đủ mạnh, riêng cho môi trường local. |
| JWT_KEY | Chuỗi ký JWT ngẫu nhiên, tối thiểu 32 ký tự; dùng chung cho hai backend. |
| ASPNETCORE_ENVIRONMENT | Để Development khi cần Swagger/OpenAPI local. |
| VITE_DEMO_MODE | Để false để frontend gọi API thật. |
| INITIAL_HR_ENABLED | Đặt true trên database mới và điền INITIAL_HR_EMAIL/PASSWORD/FULL_NAME/CODE. |
| DEMO_DATA_ENABLED | Đặt true nếu cần seed thêm Manager và Employee; điền DEMO_MANAGER_* và DEMO_EMPLOYEE_*. |

Thông tin đăng nhập phải do người triển khai tự đặt. Không dùng mật khẩu mẫu trong môi trường dùng chung hoặc production. Nếu database đã có HR phù hợp, không bật seed HR lần nữa chỉ để chạy demo.

### 2.3. Build và khởi động

~~~powershell
docker compose up --build -d
docker compose ps
Invoke-RestMethod http://localhost:8081/health
Invoke-RestMethod http://localhost:8082/health
~~~

Mở giao diện tại [http://localhost:3000](http://localhost:3000). Trong API mode, đăng nhập bằng tài khoản được seed vào SQL Server từ cấu hình local.

Các lệnh thường dùng:

~~~powershell
docker compose logs -f hr-service leave-service
docker compose restart hr-service
docker compose down
~~~

Lệnh down dừng container nhưng giữ volume. Chỉ dùng down -v khi chủ động muốn xóa dữ liệu local; không chạy trên môi trường cần giữ database.

## 3. Địa chỉ dịch vụ

### 3.1. Stack local thông thường

| Thành phần | Địa chỉ | Ghi chú |
|---|---|---|
| Web | [http://localhost:3000](http://localhost:3000) | Frontend React |
| HR health | [http://localhost:8081/health](http://localhost:8081/health) | Health endpoint |
| HR Swagger | [http://localhost:8081/swagger](http://localhost:8081/swagger) | Chỉ khi Development |
| HR OpenAPI | [http://localhost:8081/openapi/v1.json](http://localhost:8081/openapi/v1.json) | Chỉ khi Development |
| Leave health | [http://localhost:8082/health](http://localhost:8082/health) | Health endpoint |
| Leave Swagger | [http://localhost:8082/swagger](http://localhost:8082/swagger) | Chỉ khi Development |
| Leave OpenAPI | [http://localhost:8082/openapi/v1.json](http://localhost:8082/openapi/v1.json) | Chỉ khi Development |
| SQL Server | localhost,1433 | Kết nối từ host |
| Redis | localhost:6379 | Kết nối từ host |

### 3.2. Stack benchmark cô lập

Benchmark dùng Compose project **hrbench**, tách khỏi stack local thông thường:

| Thành phần | Cổng host |
|---|---:|
| Web | 13000 |
| HR Service | 18081 |
| Leave Service | 18082 |
| SQL Server | 11433 |
| Redis | 16379 |

Không thay stack này bằng môi trường production. Chi tiết dựng stack và chạy k6 xem [Approval benchmark guide](testing/approval-benchmark-guide.md).

## 4. Demo mode, API mode và vai trò

### 4.1. Phân biệt hai chế độ

| Chế độ | Nguồn dữ liệu | Tài khoản |
|---|---|---|
| Demo mode (VITE_DEMO_MODE=true) | Dữ liệu demo phía trình duyệt, lưu trong localStorage; không ghi xuống SQL Server và không xác thực JWT backend. | HR: hr@company.com; Manager: manager@company.com; Employee: employee@company.com. Mật khẩu demo: Demo123!. Chỉ dùng trong Demo mode. |
| API mode (VITE_DEMO_MODE=false) | HR_DB/Leave_DB và HR/Leave API thật. | HR và tài khoản Manager/Employee lấy từ biến môi trường local. |

VITE_DEMO_MODE được nhúng vào bundle lúc build. Sau khi thay đổi biến này phải build lại Web:

~~~powershell
docker compose build --no-cache web
docker compose up -d web
~~~

### 4.2. Luồng chính theo vai trò

| Vai trò | Nghiệp vụ chính |
|---|---|
| HR | Quản lý nhân viên, phòng ban, loại nghỉ và chính sách nhân sự theo quyền được cấp. |
| Manager | Xem đơn trong phạm vi Team và phê duyệt/từ chối đơn có thể xử lý; xem thông tin nhóm theo quyền. |
| Employee | Xem thông tin cá nhân/số dư, tạo đơn, theo dõi trạng thái và hủy đơn Pending của chính mình. |

Backend Dynamic RBAC thực thi permission và scope Self/Team/All. React hiện vẫn dùng Role legacy HR/Manager/Employee cho menu và route guard; UI chưa chuyển hoàn toàn sang permission-driven. Không coi việc ẩn nút trên frontend là lớp bảo vệ API.

## 5. Swagger và xác thực API

Swagger/OpenAPI chỉ được map khi backend chạy ở Development. Dùng các địa chỉ Swagger ở Mục 3.

Quy trình thử endpoint được bảo vệ:

1. Gọi HR **POST /api/v1/auth/login** bằng tài khoản API mode đã seed.
2. Sao chép accessToken trong response.
3. Chọn **Authorize** trong Swagger UI và nhập Bearer token theo định dạng **Bearer <accessToken>**.
4. Gửi thử endpoint theo permission của tài khoản.

Refresh token được giữ trong HttpOnly cookie. Không ghi token, cookie, mật khẩu hoặc secret vào tài liệu/commit. OpenAPI JSON nằm tại /openapi/v1.json trên từng service khi Development.

## 6. Chạy kiểm thử

### 6.1. Backend — .NET 10

Dựng stack hrbench theo [validation guide](testing/dynamic-authorization-validation.md), rồi cấu hình connection string kiểm thử trong phiên PowerShell:

~~~powershell
$env:HR_TEST_SQL_CONNECTION_STRING = "Server=localhost,11433;Database=master;User Id=sa;Password=<SA_PASSWORD>;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5"
$env:HR_TEST_REDIS_CONNECTION_STRING = "localhost:16379,abortConnect=false,connectTimeout=5000"

dotnet test .\src\hr-service\HrService.slnx
if ($LASTEXITCODE -ne 0) { throw "HR Service tests failed" }

dotnet test .\src\leave-service\LeaveService.slnx
if ($LASTEXITCODE -ne 0) { throw "Leave Service tests failed" }
~~~

Thay placeholder SA_PASSWORD bằng mật khẩu local. Integration tests có thể tạo và xóa database kiểm thử; HR integration tests dùng Redis database 15. Chỉ dùng SQL Server/Redis riêng cho test, tuyệt đối không trỏ production. Với checkout mới, chạy restore bình thường; chỉ thêm --no-restore khi đã restore đúng dependency.

**Điều kiện ghi nhận kết quả:** lưu số passed/failed/skipped, ngày chạy, git rev-parse HEAD, lệnh chạy và TRX/log. Skipped lớn hơn 0 nghĩa là có test chưa thực thi, không được ghi thành full integration pass.

### 6.2. Frontend — Node.js 20

Nếu host đã cài Node.js 20 và npm:

~~~powershell
Push-Location .\src\web
npm ci
npm audit
npm test -- --run
npm run build
Pop-Location
~~~

Nếu không cài npm trên host, chạy bằng container Node 20:

~~~powershell
docker run --rm -v "$($PWD.Path)\src\web:/app" -w /app --tmpfs /app/node_modules:exec node:20-alpine sh -lc "npm ci && npm audit && npm test -- --run && npm run build"
~~~

Lệnh lần lượt cài package theo lockfile, quét dependency, chạy Vitest một lần và build production. Không bỏ qua exit code của bước nào khi ghi nhận kết quả.

## 7. Chạy Approval benchmark

Chỉ chạy trên stack cô lập hrbench. Không benchmark đồng thời với demo hoặc workload khác trên cùng database.

Dựng stack:

~~~powershell
$compose = @(
  "-p", "hrbench",
  "-f", "docker-compose.yml",
  "-f", "docker-compose.loadtest.yml"
)

docker compose @compose up --build -d
docker compose @compose ps
Invoke-RestMethod http://localhost:18081/health
Invoke-RestMethod http://localhost:18082/health
~~~

Hai lệnh Approval arrival-rate dưới đây dùng cùng tốc độ 50 approval bắt đầu mỗi giây trong 300 giây; chỉ khác kích thước VU pool:

~~~powershell
.\scripts\load-test\run-approval-benchmark.ps1 -Mode RunConstantArrivalRate -PreAllocatedVus 50 -MaxVus 50
.\scripts\load-test\run-approval-benchmark.ps1 -Mode RunConstantArrivalRate -PreAllocatedVus 100 -MaxVus 100
~~~

Script tự seed lại, kiểm tra đủ Pending theo từng Manager rồi mới chạy k6. Chạy tuần tự, không chạy hai lệnh cùng lúc. Không seed SQL thủ công ngoài quy trình script khi đang so sánh benchmark.

- Tham số seed/workload dùng chung: scripts/load-test/approval-benchmark.config.json.
- Hướng dẫn constant-vus, constant-arrival-rate, verifier, capacity và metrics: [Approval benchmark guide](testing/approval-benchmark-guide.md).
- Các kiểm thử Dynamic RBAC/integration khác: [Dynamic authorization validation](testing/dynamic-authorization-validation.md).
- Raw và summary JSON: docs/testing/evidence/dynamic-authorization/.

Không lấy DURATION=5m của executor per-vu-iterations làm bằng chứng soak test. Benchmark arrival-rate ngày 26/11 là workload 300 giây theo tốc độ cố định; không xác nhận production capacity.

## 8. Kết quả benchmark đã xác minh — 26/11/2026

### 8.1. Điều kiện chung

| Thuộc tính | Giá trị |
|---|---|
| Git SHA | d34ca9100f1db34261b7792dcc2058855bcd29aa |
| HR image | sha256:8eccc09571553866449c312854526453e167ee01f1009491318b22dda8de4d04 |
| Leave image | sha256:fbb2ed6121920bbabc9923fdd3396c300331ebab098b91403c4c2a35dcb30911 |
| Workload | 50 approvals/s × 300 giây |
| Planned approvals | 15.000 mỗi lượt |
| Dataset sau seed | 5.000 nhân viên, 100.000 đơn, 50.000 Pending |
| Phân bổ Pending | 25 Manager, 2.000 Pending/Manager |
| Pending cần cho workload | 600 ID/Manager |

### 8.2. Kết quả hai VU pool

| Metric | Pool 50/50 | Pool 100/100 |
|---|---:|---:|
| Approval HTTP attempts | 15.000 | 15.000 |
| Approval thành công | 15.000 | 15.000 |
| Throughput | 50/s | 50/s |
| Dropped iterations | 0 | 0 |
| Approval errors | 0 | 0 |
| Error rate | 0% | 0% |
| Approval p50 | 12,88 ms | 13,43 ms |
| Approval p95 | 16,21 ms | 20,50 ms |
| Leave → HR calls | 30.000 | 30.000 |
| Leave → HR average | 2,22 ms | 2,39 ms |
| Leave → HR p95 | 3,31 ms | 3,91 ms |
| k6 iterations | 15.000 | 15.001 |
| Out-of-plan iterations | 0 | 1 |

**Kết luận trong phạm vi phép đo:** cả hai pool hoàn thành đủ 15.000 approval, giữ target 50/s, không dropped iteration và không có approval error. Pool 50 VU đã đáp ứng workload; tăng lên 100 VU không cải thiện throughput và có p95 cao hơn trong hai lượt đo này.

Pool 100 VU có một lần gọi iteration ngoài kế hoạch (index 15.000). Guard trong k6 bỏ qua lần đó trước khi gửi HTTP; do đó HTTP attempts và approval thành công vẫn bằng 15.000, không có approval thứ 15.001.

Raw/summary report:

- Pool 50: approval-arrival-rate-50rps-50-vu-pool-20261126-131504
- Pool 100: approval-arrival-rate-50rps-100-vu-pool-20261126-132221

Chưa thu thập CPU/RAM/SQL waits đồng bộ để kết luận bottleneck. Đây là benchmark local với revision và image nêu trên, không phải cam kết năng lực production.

## 9. Kiến trúc và giới hạn cần bàn giao

- HR Service sở hữu HR_DB; Leave Service sở hữu Leave_DB. Leave gọi HR qua HTTP để đọc catalog/chính sách authoritative, không truy cập trực tiếp HR database.
- Redis là cache best-effort; SQL Server vẫn là nguồn dữ liệu nghiệp vụ chính.
- Backend Dynamic RBAC V1 quản lý Roles, Permissions, UserRoles, scope rules và audit. Scope đang thực thi gồm Self, Team, All; Department chưa thành bộ lọc dữ liệu nghiệp vụ.
- Frontend vẫn dùng Role legacy cho navigation/route guards; chưa có UI quản trị Role/Permission catalog hoặc permission-driven UI đầy đủ.
- Leave request lưu policy version và quota snapshot theo yearly allocation. Policy được chọn theo ngày tạo đơn ở business timezone UTC+07; request cũ giữ snapshot đã tạo.
- Balance dùng PolicyDays, Used và Pending; Entitled được suy ra để tránh số dư âm khi quota giảm. Chưa có bulk reconciliation cho toàn bộ nhân viên.
- Access JWT xác thực stateless; thay đổi/revoke UserRole không thu hồi tức thời access token đã phát, token cũ còn hiệu lực đến expiry. Refresh token có thể bị thu hồi.
- Health endpoint chưa thay thế đầy đủ readiness/liveness checks theo dependency; HR HttpClient chưa có circuit breaker.
- Chưa có bằng chứng kiểm thử triển khai từ clean environment, TLS, secret manager, backup/restore và rollback rehearsal.

Các gap đã xác nhận và thứ tự ưu tiên phải đối chiếu [Backlog](planning/BACKLOG.md); không tự ghi đề xuất thành tính năng đã hoàn thành.

## 10. Sự cố benchmark và cách diễn giải

Một lượt Workforce load test trước đây ghi nhận 21 HTTP 500 khi Docker bị dừng giữa chừng trong lúc benchmark. Nguyên nhân gián đoạn môi trường đã được xác nhận; không cần mở lại điều tra sự cố cũ. Chỉ xem xét lại khi HTTP 500 tái xuất hiện trong một lượt chạy ổn định, có log/stack trace và có thể tái hiện.

Benchmark và integration tests có thể thay đổi dữ liệu trong database test. Chỉ chạy seed/cleanup trên stack kiểm thử riêng. Không dùng docker compose down -v để dọn dữ liệu nếu chưa xác định chính xác project và volume.

## 11. Checklist sign-off cho người tiếp nhận

- [ ] Clone đúng repository và xác nhận branch/revision source.
- [ ] Tạo .env local; xác nhận không có secret thật được commit.
- [ ] Dựng Compose; HR và Leave trả health response; frontend mở được.
- [ ] Xác nhận đang dùng đúng Demo mode hoặc API mode.
- [ ] Đăng nhập và thực hiện luồng cơ bản bằng HR, Manager và Employee trong API mode.
- [ ] Mở Swagger ở Development, đăng nhập lấy Bearer token và thử endpoint được cấp quyền.
- [ ] Chạy HR/Leave tests với SQL Server + Redis test; ghi passed/failed/skipped, SHA và TRX.
- [ ] Chạy Web audit, tests và production build; lưu output.
- [ ] Chỉ khi cần benchmark: seed/verify, chạy workload tuần tự, lưu raw/summary, Git SHA và image IDs.
- [ ] Đối chiếu giới hạn ở Mục 9 và backlog trước khi ký nghiệm thu.
- [ ] Nếu yêu cầu production: hoàn thành TLS, secrets, backup/restore, monitoring, migration và rollback verification riêng.

Không đánh dấu checklist hoàn tất chỉ dựa trên tài liệu hoặc kết quả lịch sử; người tiếp nhận cần chạy lại trên môi trường/revision bàn giao thực tế.

## 12. Tài liệu tham chiếu

- [README — cài đặt và chạy local](../README.md)
- [Docs index](README.md)
- [Hướng dẫn sử dụng, test và demo](USER-GUIDE.md)
- [API reference](API_REFERENCE.md)
- [System design](architecture/SYSTEM_DESIGN.md)
- [Database design](architecture/DATABASE_DESIGN.md)
- [Dynamic Authorization design](architecture/AUTHORIZATION_DESIGN.md)
- [Dynamic RBAC validation và benchmark](testing/dynamic-authorization-validation.md)
- [Approval benchmark guide và kết quả 26/11](testing/approval-benchmark-guide.md)
- [Demo, kết quả kiểm thử và acceptance](testing/demo-and-acceptance.md)
- [Backlog](planning/BACKLOG.md)
