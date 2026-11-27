# Tài liệu bàn giao hệ thống HR Leave Management

*Cập nhật: 27/11/2026*

Tài liệu dành cho người tiếp nhận source, dựng môi trường, kiểm thử, demo và kiểm tra giới hạn trước khi nghiệm thu. Nội dung tách biệt **tính năng đã có**, **bằng chứng đã chạy** và **việc chưa được xác minh**.

## 1. Repository và revision

| Thuộc tính | Giá trị |
|---|---|
| Repository | [OlYVER1337/hr-leave-management](https://github.com/OlYVER1337/hr-leave-management) |
| Master revision hiện tại | **de15fd4169ea8db3cc702f95f7922ca4ab9a1e8c** (sau khi merge PR #21 ngày 27/11/2026) |
| Hợp nhất Employees/direct reports | [PR #19](https://github.com/OlYVER1337/hr-leave-management/pull/19), merge commit **30720e176106cc3357ff028ddc9a6ebdce95a93b** |
| Sửa phân trang Manager | [PR #20](https://github.com/OlYVER1337/hr-leave-management/pull/20), merge commit **918b9193049915b4ded9aef70536386e813f3014** |
| Đồng bộ pageSize toàn bộ danh sách | [PR #21](https://github.com/OlYVER1337/hr-leave-management/pull/21), merge commit **de15fd4169ea8db3cc702f95f7922ca4ab9a1e8c** |
| PR permission-driven UI | [PR #18](https://github.com/OlYVER1337/hr-leave-management/pull/18), merge commit **d69156aa8990e2d92d21ad0dac1f9e0a7e1d2c5e** |
| Sửa lỗi HR OpenAPI | [0a34e13](https://github.com/OlYVER1337/hr-leave-management/commit/0a34e131fb7550695492d42614a3d74e385beab0) và [01be3cf](https://github.com/OlYVER1337/hr-leave-management/commit/01be3cfa7a03efcf6ad63d3a16b4d6eafb706444). |
| Merge Dynamic Authorization V1 | [PR #6](https://github.com/OlYVER1337/hr-leave-management/pull/6), merge commit **81b987043ce4663ad11800bb331ffdbc013bd1d2** |
| Trạng thái production | Chưa có bằng chứng triển khai hoặc nghiệm thu production. |
| Nhánh remote đã dọn | `Update-docs`, `benchmark/approval-constant-vus`, `feature/approval-arrival-rate-config` không còn trên remote. |
| Nhánh giữ lại | `fix/approval-benchmark-metadata` được giữ để tránh mất 3 commit chưa tích hợp. |

**Quy tắc đối chiếu:** Approval benchmark ngày 26/11 chạy trên source SHA **d34ca91** và image ID ghi tại Mục 8. Không lấy HEAD của branch tài liệu thay cho revision benchmark. Khi tài liệu khác với implementation, đối chiếu source code và EF Core migrations.

**Ghi chú Workforce benchmark sau PR #19:** `scripts/load-test/workforce.js` đổi route `manager_team` trong `paginationContractRoutes` từ `/api/v1/me/team` sang `/api/v1/employees?directReportsOnly=true`; request preflight cố ý không gửi `page/pageSize` và mong đợi HTTP 400 để kiểm tra contract phân trang. Mảng `routes` thực sự chạy trong VU workload không thay đổi, vì vậy route mix và custom metrics của phần measured workload lịch sử vẫn giữ nguyên. Evidence cũ không cung cấp latency/throughput riêng cho endpoint Employees/directReports mới. Nếu cần đo endpoint mới, phải thêm request phân trang vào measured workload và lưu raw/summary cùng Git SHA, image IDs mới. Raw k6 built-in metrics có thể bao gồm request preflight/setup.

**Bằng chứng tích hợp và regression đến 27/11/2026:** PR #19 hợp nhất API Employees/direct reports; Manager dùng `GET /api/v1/employees?directReportsOnly=true`, endpoint `/api/v1/me/team` đã gỡ khỏi controller/runtime/OpenAPI. Trên HR Bench, request Manager trả HTTP 200 và response có `managerId` trùng JWT `sub`. Người dùng xác nhận tài khoản HR scope `All` có thể thêm/xem/sửa/xóa nhân viên và phòng ban, đồng thời duyệt/từ chối đơn nghỉ phép.

Backend regression: HR **162/162 passed**, Leave **129/129 passed**, đều 0 failed/0 skipped; hai suite được chạy trên revision `30720e176106cc3357ff028ddc9a6ebdce95a93b` với SQL Server/Redis của stack `hrbench`. Đây là revision trước PR #20/#21; hai PR sau chỉ thay đổi Web, không thay đổi backend.

Frontend: Web **107/107 passed** (20/20 test files), `npm ci`, `npm audit`, TypeScript `tsc --noEmit` và Vite production build thành công trên nhánh PR #21, head `8f69c5420119817faa0371f1675c96f7f5253d52`. PR #20/#21 đều đã merge; master hiện tại là `de15fd4169ea8db3cc702f95f7922ca4ab9a1e8c`. Chưa có xác nhận HR Bench Web đã được build lại từ master sau PR #21; cũng chưa lưu ảnh Network cuối cùng cho cả bốn màn hình. TRX/log riêng của các lượt regression mới chưa được lưu trong repository.

## 2. Dựng hệ thống local lần đầu

### 2.1. Yêu cầu

- Docker Desktop hoặc Docker Engine có Docker Compose plugin.
- Git để clone repository.
- Internet trong lần build đầu để tải image và package.
- .NET 10 SDK nếu chạy backend tests trực tiếp trên host.
- Node.js 20+ và npm nếu chạy frontend tests trực tiếp. Không cần cài npm trên host nếu dùng Docker như Mục 6.2.

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

Backend Dynamic RBAC thực thi permission và scope Self/Team/All. React đã dùng permission/scope trong JWT cho menu, route guard và action (PR #18 đã merge vào master). `Employee.Role` và Role claim legacy vẫn được giữ cho tương thích/hiển thị, lọc/thống kê và một số nghiệp vụ legacy; không phải nguồn authorization. Không coi việc ẩn nút trên frontend là lớp bảo vệ API.

## 5. Swagger và xác thực API

Swagger/OpenAPI chỉ được map khi backend chạy ở Development. Khi chạy stack local thông thường, mở:

- HR Swagger UI: [http://localhost:8081/swagger](http://localhost:8081/swagger)
- Leave Swagger UI: [http://localhost:8082/swagger](http://localhost:8082/swagger)
- HR OpenAPI JSON: [http://localhost:8081/openapi/v1.json](http://localhost:8081/openapi/v1.json)
- Leave OpenAPI JSON: [http://localhost:8082/openapi/v1.json](http://localhost:8082/openapi/v1.json)

Khi chạy stack benchmark cô lập `hrbench`, dùng cổng host thay thế: HR `18081`, Leave `18082`. Ví dụ:

~~~powershell
Start-Process http://localhost:18081/swagger
Start-Process http://localhost:18082/swagger
curl.exe -f http://localhost:18081/openapi/v1.json -o NUL
curl.exe -f http://localhost:18082/openapi/v1.json -o NUL
~~~

HTTP 200 của trang `/swagger` chỉ xác nhận HTML UI được phục vụ. Cần kiểm tra riêng `/openapi/v1.json`; đây mới là tài liệu JSON mà Swagger UI tải để hiển thị endpoint/schema.

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

### 6.3. Kết quả backend và OpenAPI — 26/11/2026

Người triển khai đã chạy lại hai solution sau các thay đổi sửa schema OpenAPI của HR:

| Suite | Lệnh | Passed | Failed | Skipped | Thời gian test |
|---|---|---:|---:|---:|---:|
| HR Service | `dotnet test src\hr-service\HrService.slnx --no-restore` | 158 | 0 | 0 | 7,3 giây |
| Leave Service | `dotnet test src\leave-service\LeaveService.slnx --no-restore` | 129 | 0 | 0 | 19,0 giây |
| **Tổng** | | **287** | **0** | **0** | |

Cả hai solution build thành công trên .NET 10.0.13. Thời gian build được báo cáo: HR 9,1 giây; Leave 26,1 giây. Tổng cộng **287 tests passed, 0 failed, 0 skipped**.

Source HR đã sửa hai tham số mặc định trong `LeavePolicyResponse`:
- [0a34e13](https://github.com/OlYVER1337/hr-leave-management/commit/0a34e131fb7550695492d42614a3d74e385beab0): bỏ `= default` khỏi `DateOnly EffectiveFrom`.
- [01be3cf](https://github.com/OlYVER1337/hr-leave-management/commit/01be3cfa7a03efcf6ad63d3a16b4d6eafb706444): bỏ `= default` khỏi `DateTime CreatedAtUtc`.

**Ghi chú provenance:** kết quả test và runtime do người triển khai cung cấp từ PowerShell ngày 26/11/2026, sau khi áp dụng hai commit trên. Terminal output không in `git rev-parse HEAD`; khi cần gắn chính xác kết quả với revision, chạy lệnh đó tại checkout đã test và lưu kèm log/TRX.

### 6.4. Swagger smoke test và demo end-to-end

**Điều kiện:** API mode (`VITE_DEMO_MODE=false`), `ASPNETCORE_ENVIRONMENT=Development`, HR/Leave services healthy và đã seed ba tài khoản API thật từ `.env`. Không dùng tài khoản Demo mode/localStorage cho bước này.

Kiểm tra endpoint từ PowerShell:

~~~powershell
$hrSwagger = Invoke-WebRequest http://localhost:8081/swagger
$leaveSwagger = Invoke-WebRequest http://localhost:8082/swagger
$hrOpenApi = Invoke-RestMethod http://localhost:8081/openapi/v1.json
$leaveOpenApi = Invoke-RestMethod http://localhost:8082/openapi/v1.json

if ($hrSwagger.StatusCode -ne 200 -or $leaveSwagger.StatusCode -ne 200) {
    throw "Swagger UI chưa trả HTTP 200."
}
if (-not $hrOpenApi.openapi -or -not $leaveOpenApi.openapi) {
    throw "OpenAPI document chưa được tải đúng."
}
"Swagger UI và OpenAPI JSON đều phản hồi."
~~~

Hai service chỉ map Swagger/OpenAPI khi Development. Source mapping đã được kiểm tra; các lệnh trên phải chạy trên máy nhận bàn giao để xác nhận runtime. HR login endpoint là `POST /api/v1/auth/login`; có thể đăng nhập trong Swagger bằng tài khoản HR API, lấy access token rồi dùng nút **Authorize** với dạng `Bearer <accessToken>`. Cùng token được dùng để thử các endpoint Leave yêu cầu xác thực.

**Kịch bản demo ngắn (5–7 phút):**

1. HR đăng nhập API mode; xác nhận phòng ban và nhân viên Employee được gán đúng Manager.
2. Employee đăng nhập; mở số dư, tạo một đơn nghỉ ANNUAL hợp lệ trong một ngày làm việc tương lai, ghi nhận trạng thái Pending.
3. Manager đăng nhập; mở Approval Inbox, xác nhận nhìn thấy đơn trong phạm vi Team và duyệt đơn.
4. Employee đăng nhập lại; xác nhận đơn chuyển Approved và số dư cập nhật: Pending giảm, Used tăng, Available giảm theo số ngày được duyệt.
5. HR mở danh sách đơn nghỉ hoặc dashboard để xác nhận đơn đã được xử lý.

Dùng tài khoản API mode đã cấu hình trong `.env`, đăng xuất/chuyển profile trình duyệt giữa các vai trò, và chọn ngày nghỉ chưa có đơn trùng. Không tạo dữ liệu trên production.

**Kết quả runtime mới nhất do người triển khai cung cấp:** trên stack `hrbench`, `curl.exe -v http://localhost:18081/openapi/v1.json -o NUL` trả **HTTP 200**. JSON được parse thành công và schema `components.schemas.LeavePolicyResponse` xác nhận:
- `effectiveFrom`: `type=string`, `format=date`.
- `createdAtUtc`: `type=string`, `format=date-time`.

Điều này xác nhận HR OpenAPI JSON đã sinh thành công sau hai commit sửa DTO. Log mới nhất chưa kèm lệnh kiểm tra Swagger UI và Leave OpenAPI trên cổng `hrbench`; khi nghiệm thu đầy đủ profile này, chạy cả bốn URL UI/JSON ở đầu Mục 5. Kịch bản đăng nhập ba vai trò và duyệt đơn vẫn cần được thực hiện trực tiếp trên stack local.

### 6.5. Hợp đồng phân trang giao diện sau PR #20/#21

Frontend dùng server-side pagination cho các màn hình Danh sách nhân viên, Nhân viên phụ trách, Đơn nghỉ phép và Hộp thư phê duyệt. Hook `usePagedBlock` mặc định tải đúng một trang giao diện trên mỗi request; vì vậy lựa chọn 10/25/50 phải gửi `pageSize=10/25/50`. Giới hạn backend vẫn là tối đa 100 bản ghi/request. Cơ chế tải nhiều trang theo block chỉ sử dụng khi màn hình chủ động cấu hình.

PR #20 sửa riêng Manager Team; PR #21 áp dụng mặc định thống nhất cho các danh sách còn lại và cập nhật test. Web regression sau thay đổi đạt **107/107 tests**, 20/20 test files; TypeScript và Vite production build thành công. Kết quả local được chạy trên head PR #21 `8f69c5420119817faa0371f1675c96f7f5253d52`, trước merge commit `de15fd4169ea8db3cc702f95f7922ca4ab9a1e8c`.

Khi nghiệm thu lại trên HR Bench, mở DevTools → Network, xóa log cũ rồi lần lượt chọn 10, 25 và 50 ở từng màn hình. Xác nhận request mới nhất mang `pageSize` bằng đúng lựa chọn, `page` tăng đúng khi chuyển trang và response/header tổng số vẫn khớp. Ảnh/log Network chưa được lưu thành artifact trong repository; không coi kết quả build container đơn thuần là bằng chứng Network cuối cùng.
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

Đây là seed Approval arrival-rate (5.000 × 20 = 100.000 requests, 50% Pending), không phải seed Workforce/legacy 1.000 × 5 = 5.000 requests.

### 8.2. Kết quả hai VU pool

| Metric | Pool 50/50 | Pool 100/100 |
|---|---:|---:|
| Approval HTTP attempts | 15.000 | 15.000 |
| Approval thành công | 15.000 | 15.000 |
| Approval rate chuẩn hóa theo cửa sổ workload 300s (15.000 / 300) | 50/s | 50/s |
| Raw k6 `approval_requests.rate` (toàn `testRunDuration`) | 46,43/s | 46,52/s |
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


`approval_requests.rate` trong raw JSON được k6 tính trên toàn bộ `testRunDurationMs`, gồm cả thời gian setup/teardown: pool 50 chạy 323,048 giây nên raw rate là 46,4328/s; pool 100 chạy 322,435 giây nên raw rate là 46,5210/s. Đây khác với rate chuẩn hóa theo cửa sổ workload 300 giây (15.000 / 300 = 50/s). Vì vậy 50/s là target/throughput chuẩn hóa theo scenario, không phải counter rate của toàn bộ tiến trình k6.

**Kết luận trong phạm vi phép đo:** cả hai pool hoàn thành đủ 15.000 approval, giữ target 50/s, không dropped iteration và không có approval error. Pool 50 VU đã đáp ứng workload; tăng lên 100 VU không cải thiện throughput và có p95 cao hơn trong hai lượt đo này.

Pool 100 VU có một lần gọi iteration ngoài kế hoạch (index 15.000). Guard trong k6 bỏ qua lần đó trước khi gửi HTTP; do đó HTTP attempts và approval thành công vẫn bằng 15.000, không có approval thứ 15.001.

Raw/summary report:

- Pool 50: approval-arrival-rate-50rps-50-vu-pool-20261126-131504
- Pool 100: approval-arrival-rate-50rps-100-vu-pool-20261126-132221

Bảng đầy đủ các lượt arrival-rate ngày 26/11 (cả lượt hợp lệ và bị loại), commit đo từng lượt và lý do loại lượt 11:18:07 vì 27 dropped iterations được ghi trong [Benchmark results, Mục 15.2](testing/dynamic-authorization-benchmark-results.md). Chưa thu thập CPU/RAM/SQL waits đồng bộ để kết luận bottleneck. Đây là benchmark local với revision và image nêu trên, không phải cam kết năng lực production.

## 9. Kiến trúc và giới hạn cần bàn giao

- HR Service sở hữu HR_DB; Leave Service sở hữu Leave_DB. Leave gọi HR qua HTTP để đọc catalog/chính sách authoritative, không truy cập trực tiếp HR database.
- Redis là cache best-effort; SQL Server vẫn là nguồn dữ liệu nghiệp vụ chính.
- Backend Dynamic RBAC V1 quản lý Roles, Permissions, UserRoles, scope rules và audit. Scope đang thực thi gồm Self, Team, All; Department chưa thành bộ lọc dữ liệu nghiệp vụ.
- React đã dùng permission/scope trong JWT cho menu, route guard và action (PR #18 đã merge vào master). Role claim/`Employee.Role` legacy chỉ còn phục vụ tương thích/hiển thị và các chức năng dữ liệu nhân viên; không phải nguồn authorization.
- PR #19 đã hợp nhất `/my-team` vào API dùng chung `GET /api/v1/employees?directReportsOnly=true`; endpoint `GET /api/v1/me/team` cũ đã gỡ khỏi controller/runtime/OpenAPI. Manager direct-report API đã được kiểm tra trên HR Bench; người dùng đã nghiệm thu tài khoản HR scope `All` cho CRUD nhân viên/phòng ban và duyệt/từ chối đơn nghỉ phép.
- PR #20 sửa trang Nhân viên phụ trách gửi đúng `pageSize` theo dropdown.
- PR #21 đổi mặc định `usePagedBlock` để mỗi request gửi đúng một trang UI; áp dụng cho Danh sách nhân viên, Nhân viên phụ trách, Đơn nghỉ phép và Hộp thư phê duyệt. Dropdown 10/25/50 tương ứng `pageSize=10/25/50`; khả năng tải gộp block chỉ còn khi màn hình cấu hình tường minh.
- Chưa có UI quản trị UserRole assignment hoặc CRUD Role/Permission catalog.
- Leave request lưu policy version và quota snapshot theo yearly allocation. Policy được chọn theo ngày tạo đơn ở business timezone UTC+07; request cũ giữ snapshot đã tạo.
- Balance dùng PolicyDays, Used và Pending; Entitled được suy ra để tránh số dư âm khi quota giảm. Chưa có bulk reconciliation cho toàn bộ nhân viên.
- Access JWT xác thực stateless; thay đổi/revoke UserRole không thu hồi tức thời access token đã phát, token cũ còn hiệu lực đến expiry. Refresh token có thể bị thu hồi.
- Health endpoint chưa thay thế đầy đủ readiness/liveness checks theo dependency; HR HttpClient chưa có circuit breaker.
- Chưa có bằng chứng kiểm thử triển khai từ clean environment, TLS, secret manager, backup/restore và rollback rehearsal.

Các gap đã xác nhận và thứ tự ưu tiên phải đối chiếu [Backlog](planning/BACKLOG.md); không tự ghi đề xuất thành tính năng đã hoàn thành.


## 10. Đối chiếu đầu ra theo đề tài thực tập

Đối chiếu với mục **9. Đầu ra mong muốn** trong [đề tài thực tập 2026](requirements/internship-project-2026.docx):

| # | Đầu ra bắt buộc | Tài liệu/source/evidence trong repository | Trạng thái khi bàn giao |
|---:|---|---|---|
| 1 | Source code Git: frontend, hai backend, test và cấu hình triển khai | Thư mục `src/`, `scripts/`, lịch sử Git, `docker-compose.yml`, Dockerfiles | Đã có trong repository; người nhận cần clone và xác nhận revision. |
| 2 | Ứng dụng chạy được: Employee gửi đơn, Manager xử lý, HR quản trị | [User guide](USER-GUIDE.md), [Acceptance/demo](testing/demo-and-acceptance.md) | Các luồng chính đã triển khai và có bằng chứng demo API lịch sử; cần chạy lại từ môi trường nhận bàn giao. |
| 3 | System design: kiến trúc, service ownership, giao tiếp và lỗi | [System design](architecture/SYSTEM_DESIGN.md), [Architecture diagrams](architecture/README.md) | Tài liệu đã có; đối chiếu lại với source khi có thay đổi. |
| 4 | Database design: ERD, schema, index/constraint, migrations và seed | [Database design](architecture/DATABASE_DESIGN.md), EF Core migrations trong từng service, scripts seed | Đã có tài liệu/source; migration và seed cần xác nhận chạy được trên database mới. |
| 5 | API documentation: Swagger/OpenAPI, request/response và lỗi | [API reference](API_REFERENCE.md), Swagger UI và OpenAPI JSON ở Development | Đã có; Swagger runtime chỉ được bật trong Development. |
| 6 | Frontend Design System và components tái sử dụng | [Design System](design/DESIGN_SYSTEM.md), [Wireframes](design/WIREFRAMES.md), `src/web/src/` | Đã có tài liệu và UI permission-driven; menu/route/action dùng permission/scope từ JWT. UserRole assignment UI và Role/Permission catalog CRUD chưa có. |
| 7 | Redis design/evidence: key, TTL, invalidation, miss và fallback | [Redis performance report](performance/redis-performance.md), cache implementation và tests | Đã có báo cáo Catalog Redis ON/OFF, cache key/TTL/invalidation/fallback; không dùng cache làm căn cứ duy nhất để quyết định số dư/duyệt đơn. |
| 8 | Test & performance report: unit/integration, k6, Redis ON/OFF | [Demo/acceptance](testing/demo-and-acceptance.md), [Validation guide](testing/dynamic-authorization-validation.md), [Approval benchmark guide](testing/approval-benchmark-guide.md), [evidence](testing/evidence/) | Đã có kết quả lịch sử và benchmark Approval ngày 26/11; regression tests cần chạy lại và gắn SHA nếu cần sign-off theo revision hiện tại. |
| 9 | Deployment package: Dockerfile, Compose, `.env.example`, README và chạy máy mới | `docker-compose.yml`, `docker-compose.loadtest.yml`, Dockerfiles, `.env.example`, [README](../README.md) | Đã có cấu hình; chưa có bằng chứng clean-machine deployment/production. |
| 10 | Demo cuối kỳ và trình bày quyết định, sự cố, cách xử lý, cải tiến | Kịch bản demo trong [Acceptance/demo](testing/demo-and-acceptance.md), kiến trúc, backlog và benchmark reports | Nội dung kỹ thuật đã có để chuẩn bị demo; buổi trình bày/sign-off phải thực hiện trực tiếp, không thể coi file tài liệu là bằng chứng đã trình bày. |

## 11. Kịch bản nghiệm thu tối thiểu theo đề tài

Khi nghiệm thu API mode, thực hiện lần lượt các kịch bản sau. Dùng tài khoản và dữ liệu demo local được tạo từ `.env`; không dùng dữ liệu production.

- [ ] HR tạo nhân viên, gán phòng ban/quản lý trực tiếp và cấu hình chính sách nghỉ phép.
- [ ] Employee đăng nhập, xem số dư, gửi đơn hợp lệ và đọc trạng thái đơn.
- [ ] Đơn trùng ngày hoặc vượt số phép bị từ chối với thông báo phù hợp.
- [ ] Manager chỉ nhìn thấy và xử lý đơn thuộc phạm vi quản lý được cấp.
- [ ] User không có quyền không thể duyệt đơn hoặc xem dữ liệu ngoài phạm vi.
- [ ] Hai thao tác đồng thời không làm sai trạng thái đơn hoặc số dư.
- [ ] Thay đổi chính sách được phản ánh qua cơ chế cache invalidation; cache cũ không làm sai quyết định nghiệp vụ.
- [ ] Dừng Redis không làm mất các chức năng cốt lõi dựa trên SQL fallback.
- [ ] Có thể dựng hệ thống từ Docker Compose và hoàn thành luồng demo end-to-end theo README.

Các kiểm thử concurrency, authorization và Redis outage có bằng chứng automation trong test suites/reports. Người tiếp nhận vẫn nên chạy lại các luồng trực quan và API trên revision/môi trường thực tế trước khi ký nghiệm thu.

## 12. Cơ cấu đánh giá tham chiếu

Đề tài phân bổ trọng số đánh giá như sau:

| Nhóm | Trọng số |
|---|---:|
| Kiến trúc hệ thống và thiết kế database | 25% |
| Backend .NET, nghiệp vụ, API và phân quyền | 25% |
| Redis, xử lý đồng thời, testing và hiệu năng | 20% |
| React UI/UX và Design System | 15% |
| Docker Compose, tài liệu, chất lượng bàn giao và demo | 15% |
| **Tổng** | **100%** |

Đây là trọng số trong tài liệu đề tài, không phải điểm tự đánh giá hoặc kết quả chấm thực tế.

## 13. Sự cố benchmark và cách diễn giải

Một lượt Workforce load test trước đây ghi nhận 21 HTTP 500 khi Docker bị dừng giữa chừng trong lúc benchmark. Nguyên nhân gián đoạn môi trường đã được xác nhận; không cần mở lại điều tra sự cố cũ. Chỉ xem xét lại khi HTTP 500 tái xuất hiện trong một lượt chạy ổn định, có log/stack trace và có thể tái hiện.

Benchmark và integration tests có thể thay đổi dữ liệu trong database test. Chỉ chạy seed/cleanup trên stack kiểm thử riêng. Không dùng docker compose down -v để dọn dữ liệu nếu chưa xác định chính xác project và volume.

## 14. Checklist sign-off cho người tiếp nhận

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

## 15. Tài liệu tham chiếu

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
