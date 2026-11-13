# Xác minh Dynamic RBAC và benchmark HR lookup

Tài liệu này dùng cho branch `feature/dynamic-authorization-v1`. Các integration test tạo/xóa database kiểm thử ngẫu nhiên; chỉ chạy với SQL Server và Redis riêng cho môi trường test/benchmark.

**Báo cáo số liệu đã chạy:** [Kết quả benchmark Dynamic RBAC và HR lookup](dynamic-authorization-benchmark-results.md). Báo cáo ghi cả kết quả lần đầu và repeat, phương pháp, giới hạn diễn giải và các việc còn thiếu để nghiệm thu.

## 1. Cập nhật mã nguồn và dựng stack cô lập

PowerShell tại thư mục gốc repository:

```powershell
git pull origin feature/dynamic-authorization-v1

$compose = @(
    "-p", "hrbench",
    "-f", "docker-compose.yml",
    "-f", "docker-compose.loadtest.yml"
)

docker compose @compose up --build -d
docker compose @compose ps
```

Project Compose `hrbench` dùng các cổng riêng: HR 18081, Leave 18082, SQL Server 11433 và Redis 16379. Không dừng hoặc xóa project `hr-leave` đang dùng cho demo/phát triển.

Chờ các dịch vụ healthy trước khi chạy test:

```powershell
Invoke-RestMethod http://localhost:18081/health
Invoke-RestMethod http://localhost:18082/health
```

## 2. Chạy toàn bộ test với SQL Server và Redis thật

Chạy trực tiếp bằng .NET CLI. Hai biến môi trường dưới đây cho phép các integration test kết nối tới SQL Server và Redis trong stack `hrbench`. Thay `<SA_PASSWORD>` bằng giá trị cục bộ trong file `.env`; không commit mật khẩu hoặc connection string thật.

```powershell
$env:HR_TEST_SQL_CONNECTION_STRING = "Server=localhost,11433;Database=master;User Id=sa;Password=<SA_PASSWORD>;Encrypt=False;TrustServerCertificate=True;Connect Timeout=5"
$env:HR_TEST_REDIS_CONNECTION_STRING = "localhost:16379,abortConnect=false,connectTimeout=5000"

dotnet test .\src\hr-service\HrService.slnx
dotnet test .\src\leave-service\LeaveService.slnx
```

Điều kiện nghiệm thu: HR Service và Leave Service đều có `failed = 0`, `skipped = 0`. Nếu chưa cấu hình các biến môi trường, một số integration test sẽ bị `Skipped`; không xem đó là đã xác minh tích hợp. Lưu lại toàn bộ log Test summary để làm bằng chứng.

## 3. Seed dữ liệu tải

Seed tạo/cập nhật bộ dữ liệu 1.000 nhân viên load-test và 5.000 đơn nghỉ, trong đó 1.250 Pending (25%). Nó chỉ xóa đơn và số dư thuộc nhóm nhân viên mã `LT*` / lý do `LOADTEST:*` đã được chuẩn bị cho benchmark.

```powershell
docker compose @compose cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql

docker compose @compose exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d master -i /tmp/seed-scale-data.sql'
```

Lệnh SQL lấy mật khẩu từ biến môi trường trong container SQL Server; không cần nhập hoặc in mật khẩu trong terminal.

Kết quả thành công có dạng:

```text
SeededEmployees SeededLeaveRequests LeaveYear
1000             5000                2026
```

Không nên ghép thêm nhiều tầng dấu nháy vào lệnh `sqlcmd -Q` trên PowerShell. Nếu cần kiểm tra dữ liệu, ưu tiên chạy câu SELECT trong SSMS kết nối `localhost,11433` hoặc dùng một file `.sql` riêng được copy vào container rồi chạy bằng `-i`.

## 4. Workforce reads — 50 và 100 VU

Workload phân phối request đến HR Employees, Leave Requests, Leave Calendar và Manager Approval Inbox. Metric tách biệt request trực tiếp vào HR và các HTTP call nội bộ từ Leave Service sang HR.

Service `k6` được khai báo trong profile `loadtest`, vì vậy phải thêm `--profile loadtest` vào lệnh Compose:

```powershell
docker compose @compose --profile loadtest run --rm `
  -e VUS=50 `
  -e DURATION=5m `
  -e REPORT_JSON=/reports/workforce-50.json `
  k6 run /scripts/workforce.js

docker compose @compose --profile loadtest run --rm `
  -e VUS=100 `
  -e DURATION=5m `
  -e REPORT_JSON=/reports/workforce-100.json `
  k6 run /scripts/workforce.js
```

Đọc các trường trong JSON summary:

- `hr_direct_requests`, `hr_direct_total_time_ms`, `hr_direct_avg_ms`, `hr_direct_p95_ms`: số request trực tiếp tới HR API, tổng thời gian, trung bình và p95.
- `leave_to_hr_calls`, `leave_to_hr_calls_per_second`: tổng outbound HR calls phát sinh bên trong Leave Service.
- `leave_to_hr_total_time_ms`, `leave_to_hr_avg_ms`, `leave_to_hr_p95_ms`: tổng thời gian tích lũy và phân vị thời gian từng HTTP call Leave → HR.
- `p50_ms`, `p95_ms`, `error_rate`: độ trễ và tỷ lệ lỗi của workload tổng.

Các số liệu outbound HR được đọc từ response headers chỉ bật trong Compose benchmark. Header không được bật ở cấu hình thông thường.

## 5. Approval decisions — 50 và 100 VU

Workload này đo riêng lookup HR tại thời điểm duyệt. Script dùng executor `per-vu-iterations`; mặc định mỗi VU duyệt 10 đơn LOADTEST Pending bằng request ID riêng:

- 50 VU × 10 iterations = 500 approval attempts.
- 100 VU × 10 iterations = 1.000 approval attempts.
- `DURATION=5m` là `maxDuration`, tức giới hạn tối đa để hoàn thành scenario, **không phải tải duy trì liên tục 5 phút**. Các lượt đã chạy thực tế hoàn thành trong khoảng 1–2 giây.
- P50/p95 chỉ đo thời gian HTTP approve trong workload, không tính login và lần đọc pending inbox ở setup.

**Phải seed trước từng lần chạy Approval**, vì mỗi lần approve làm đơn chuyển khỏi trạng thái Pending. Quy tắc này áp dụng cả lần chạy đầu và mỗi lần repeat.

Seed, sau đó chạy 50 VU:

```powershell
docker compose @compose cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql

docker compose @compose exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d master -i /tmp/seed-scale-data.sql'

docker compose @compose --profile loadtest run --rm `
  -e VUS=50 `
  -e ITERATIONS_PER_VU=10 `
  -e DURATION=5m `
  -e REPORT_JSON=/reports/approval-50.json `
  k6 run /scripts/approval.js
```

Seed lại lần nữa rồi chạy 100 VU:

```powershell
docker compose @compose cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql

docker compose @compose exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d master -i /tmp/seed-scale-data.sql'

docker compose @compose --profile loadtest run --rm `
  -e VUS=100 `
  -e ITERATIONS_PER_VU=10 `
  -e DURATION=5m `
  -e REPORT_JSON=/reports/approval-100.json `
  k6 run /scripts/approval.js
```

Đối với repeat, dùng tên report khác, ví dụ `approval-50-repeat.json` và `approval-100-repeat.json`, đồng thời vẫn seed lại trước mỗi lượt.

Đọc `approval_attempts`, `completed_approvals`, `approval_errors`, `p50_ms`, `p95_ms`, `leave_to_hr_calls`, `leave_to_hr_total_time_ms`, `leave_to_hr_avg_ms` và `leave_to_hr_p95_ms`. Với mỗi lần approve Team, kỳ vọng một HR lookup để xác nhận ManagerId hiện tại; Scope All không thực hiện lookup này.

## 6. Cách ghi nhận kết quả

- Ghi riêng Workforce và Approval; không gộp throughput của hai workload vì hành vi và thời lượng chạy khác nhau.
- Với Approval, giữ các lần chạy đầu và repeat thành các dòng riêng. Không lấy trung bình hoặc thay thế kết quả bất lợi bằng kết quả repeat.
- Ghi commit SHA, branch, ngày/giờ và timezone, cấu hình Compose, số nhân viên/đơn seed, cache state và các lần chạy lỗi setup (nếu có).
- Raw k6 JSON được lưu trong `docs/testing/evidence/dynamic-authorization/`. Các file report cần giữ nguyên để có thể kiểm tra percentile và metrics gốc.
- Kết quả thực tế ngày 12/11/2026 được ghi ở Mục 8. Thời điểm bắt đầu/kết thúc từng lần chạy và SHA từ terminal chưa được nhúng vào k6 JSON; cần đối chiếu SHA tại máy benchmark bằng `git rev-parse HEAD` trước khi coi đây là baseline bất biến.

## 7. Giới hạn và an toàn

- Chạy trên stack `hrbench` cô lập, không trỏ test tới production.
- Benchmark Approval thay đổi trạng thái đơn LOADTEST; seed lại trước từng lần chạy để các lượt bắt đầu từ cùng trạng thái Pending.
- Approval hiện là burst concurrency với iteration cố định, không phải sustained load 5 phút. Muốn kiểm tra tải duy trì cần tạo scenario riêng dùng executor theo thời gian và chuẩn bị dữ liệu Pending đủ lớn/được làm mới an toàn.
- Không kết luận benchmark đạt chỉ từ HTTP success; đối chiếu cả HR-call count, HR latency, request p95 và error rate.
- Không commit file `.env`, token, mật khẩu, hoặc log có dữ liệu nhạy cảm.


## 8. Kết quả benchmark thực tế — 12/11/2026

### 8.1. Metadata và cấu hình lần chạy

| Thuộc tính | Giá trị được ghi nhận |
|---|---|
| Ngày thực hiện | 12/11/2026 |
| Timezone | Asia/Ho_Chi_Minh (UTC+07:00) |
| Branch | `feature/dynamic-authorization-v1` |
| Commit SHA tham chiếu trước benchmark | `23cfcb8ecea6302df6bf3c9fcd6232ee70de81b9` |
| Compose project | `hrbench` |
| Compose files | `docker-compose.yml` + `docker-compose.loadtest.yml` |
| k6 profile | `loadtest` |
| HR / Leave | `localhost:18081` / `localhost:18082` |
| SQL Server / Redis | `localhost:11433` / `localhost:16379` |
| Năm dữ liệu | 2026 |
| Seed | 1.000 nhân viên, 5.000 đơn nghỉ |
| Trạng thái đơn sau seed | 1.250 Pending, 1.250 Approved, 1.250 Rejected, 1.250 Cancelled |
| HR-call metrics | Bật qua `LoadTest__EnableHrCallMetrics=true` trong Leave Service |
| Redis | Container hoạt động healthy trong stack benchmark; không thực hiện bài A/B Redis bật/tắt trong nhóm kết quả này |

**Lưu ý về revision và timestamp:** SHA ở bảng là HEAD của branch được xác minh trước khi ghi nhận benchmark trong phiên làm việc này. Các output k6 đã gửi không tự chứa Git SHA; cần chạy `git rev-parse HEAD` tại checkout đã thực hiện benchmark để xác nhận chính xác SHA gắn với những lần chạy. Thời gian từng lần chạy không được lưu trong summary; chỉ ngày thực hiện được xác nhận. Không suy diễn giờ bắt đầu/kết thúc từ các mốc Docker Stats.

### 8.2. Workforce reads — tải liên tục 5 phút

Workforce dùng executor `constant-vus` với duration 5 phút. RPS trong bảng là trường `rps` do `workforce.js` tính bằng số request chia cho duration cấu hình 300 giây. Trong raw JSON của lần 50 VU, k6 ghi nhận testRunDuration khoảng 302,15 giây và counter rate khoảng 42,68 req/s; đây là cách tính theo thời gian thực tế của k6, khác với trường RPS theo 300 giây cấu hình.

| Chỉ số | 50 VU | 100 VU |
|---|---:|---:|
| Workload requests | 12.897 | 13.607 |
| RPS theo script | 42,99 | 45,36 |
| p50 (ms) | 1.102,77 | 2.212,75 |
| p95 (ms) | 2.236,73 | 4.513,24 |
| HTTP errors | 0 | 0 |
| Error rate | 0% | 0% |
| HR direct requests | 3.222 | 3.397 |
| HR direct avg (ms) | 47,76 | 67,73 |
| HR direct p95 (ms) | 97,28 | 133,94 |
| Leave API requests | 9.675 | 10.210 |
| Leave → HR calls | 3.221 | 3.391 |
| Leave → HR calls / Leave request | 0,3329 | 0,3321 |
| Leave → HR calls / second | 10,74 | 11,30 |
| Leave → HR avg (ms) | 120,40 | 219,20 |
| Leave → HR p95 (ms) | 238,09 | 470,17 |

**Nhận xét Workforce:**

- Tăng VU từ 50 lên 100 làm số request và throughput chỉ tăng khoảng 5,5%, trong khi p50 tăng khoảng 100,5% và p95 tăng khoảng 101,8%.
- Error rate vẫn bằng 0% ở cả hai mức tải.
- Tỷ lệ Leave → HR calls trên mỗi Leave API request gần như không đổi (xấp xỉ 0,333). Chưa thấy dấu hiệu số lần lookup HR tăng bất thường theo VU.
- Độ trễ tăng mạnh trong khi throughput tăng ít cho thấy workload đang có dấu hiệu chạm giới hạn xử lý ở mức 50–100 VU. Số liệu hiện tại chưa đủ để quy nguyên nhân cụ thể cho SQL Server, HR Service, Leave Service, network hoặc connection pool.

### 8.3. Approval decisions — burst concurrency

Approval dùng `per-vu-iterations`, `ITERATIONS_PER_VU=10`. Mỗi lần chạy chỉ thực hiện 10 quyết định trên mỗi VU. Bốn lần chạy đều hoàn thành dưới khoảng 2 giây; `DURATION=5m` chỉ là thời hạn tối đa, không phải thời gian duy trì tải.

| Lần chạy | VU | Planned | Completed | p50 (ms) | p95 (ms) | Approval errors | Leave → HR calls | HR avg (ms) | HR p95 (ms) | Thời gian scenario |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Lần đầu | 50 | 500 | 500 | 27,62 | 833,40 | 0 | 500 | 12,66 | 62,76 | ~1,6 s |
| Lần đầu | 100 | 1.000 | 1.000 | 57,33 | 167,79 | 0 | 1.000 | 8,53 | 20,58 | ~1,4 s |
| Repeat | 50 | 500 | 500 | 20,65 | 89,97 | 0 | 500 | 4,31 | 9,82 | ~0,9 s |
| Repeat | 100 | 1.000 | 1.000 | 58,51 | 138,63 | 0 | 1.000 | 14,95 | 49,38 | ~1,4 s |

Error rate và HTTP error đều bằng 0 trong cả bốn lần chạy. Mỗi approval phát sinh đúng một Leave → HR call theo metric được trả về từ response header.

**Nhận xét Approval:**

- Cả bốn lượt đều hoàn thành đủ số approval dự kiến: 500 ở 50 VU và 1.000 ở 100 VU.
- So sánh hai lượt repeat: khi tăng VU gấp đôi, p50 tăng từ 20,65 lên 58,51 ms; p95 tăng từ 89,97 lên 138,63 ms. HR lookup avg tăng từ 4,31 lên 14,95 ms và HR lookup p95 tăng từ 9,82 lên 49,38 ms.
- Lượt 50 VU đầu có p95 833,40 ms, cao hơn đáng kể so với repeat 89,97 ms. Lượt 100 VU đầu có p95 167,79 ms, so với repeat 138,63 ms. Điều này cho thấy biến động giữa các lần chạy; không dùng một lần chạy duy nhất để kết luận xu hướng ổn định.
- Chưa đủ cơ sở để kết luận 100 VU có hiệu năng tốt hơn 50 VU. Cần tiếp tục lặp lại cùng điều kiện và thu thập trace/metrics chi tiết nếu cần xác định điểm nghẽn.

Một lần repeat 50 VU ban đầu bị dừng ở `setup()` vì chỉ còn 250 đơn LOADTEST Pending sau lượt 100 VU trước đó, trong khi cần 500. Lần setup lỗi không chạy workload và không được tính vào bảng. Sau khi chạy lại seed, repeat 50 VU hoàn thành đủ 500 approvals.

### 8.4. Docker Stats và giới hạn đo tài nguyên

File Docker Stats được thu thập theo chu kỳ dự kiến 5 giây, thực tế các timestamp cách nhau khoảng 7 giây, trong khoảng 11:44:24–11:51:21 ngày 12/11/2026. Các mẫu ghi nhận mức CPU cao nhất trong cửa sổ lấy mẫu:

| Container | CPU cao nhất trong các mẫu | RAM quan sát được |
|---|---:|---:|
| HR Service | 2,73% | khoảng 391–426 MiB |
| Leave Service | 3,01% | khoảng 1,097–1,122 GiB |
| SQL Server | 4,93% | khoảng 1,196–1,213 GiB |
| Redis | 2,73% | khoảng 5,9–8,4 MiB |

Đây là snapshot lấy mẫu, không phải peak CPU/RAM chính xác. Đặc biệt, Approval scenario chỉ kéo dài khoảng 1–2 giây trong khi chu kỳ lấy mẫu khoảng 7 giây; do đó có thể bỏ lỡ hoàn toàn thời điểm xử lý đồng thời cao nhất. Không dùng bảng này để kết luận CPU không phải nút thắt.

### 8.5. Kết luận và giới hạn nghiệm thu

- **Tính đúng đắn:** Workforce và cả bốn lượt Approval đều có 0 HTTP/approval errors theo summary được cung cấp.
- **Workforce:** tăng từ 50 lên 100 VU làm throughput tăng ít, p95 tăng khoảng gấp đôi. Đây là tín hiệu cần điều tra trước khi tăng tải cao hơn.
- **Approval:** kiểm tra burst concurrency thành công với 50 và 100 VU; một HR lookup được ghi nhận cho mỗi approval. p95 và HR lookup latency có biến động giữa các lượt.
- **Chưa nghiệm thu sustained Approval load:** kịch bản hiện tại không duy trì 5 phút. Nếu yêu cầu kiểm tra Approval tải liên tục, cần thiết kế scenario theo thời lượng thực (ví dụ `constant-vus` hoặc `constant-arrival-rate`) và chuẩn bị đủ đơn Pending mới cho toàn bộ số quyết định.
- **Chưa xác định bottleneck:** cần đo tài nguyên liên tục hoặc dùng tracing/SQL metrics theo đúng thời gian scenario, rồi mới quyết định tối ưu query, connection pool hay HTTP HR lookup.
- **Không thực hiện Redis on/off comparison:** dữ liệu ở đây chỉ phản ánh cấu hình Redis đang hoạt động; không được diễn giải thành bằng chứng về mức cải thiện nhờ Redis.

Raw report theo đường dẫn cấu hình:

- `workforce-50.json`
- `workforce-100.json`
- `approval-50.json`
- `approval-100.json`
- `approval-50-repeat.json`
- `approval-100-repeat.json`



## 9. Xác minh hợp đồng phân trang và benchmark đúng revision

### 9.1. Rebuild service từ HEAD hiện tại

Không dùng lại container cũ khi benchmark thay đổi query hoặc pagination contract.

```powershell
git pull origin feature/dynamic-authorization-v1
$gitSha = (git rev-parse HEAD).Trim()

$compose = @(
    "-p", "hrbench",
    "-f", "docker-compose.yml",
    "-f", "docker-compose.loadtest.yml"
)

docker compose @compose build hr-service leave-service
if ($LASTEXITCODE -ne 0) { throw "Build HR/Leave failed." }

docker compose @compose up -d --force-recreate hr-service leave-service
if ($LASTEXITCODE -ne 0) { throw "Recreate HR/Leave failed." }

Invoke-RestMethod http://localhost:18081/health
Invoke-RestMethod http://localhost:18082/health

$hrImage = (docker compose @compose images -q hr-service | Select-Object -First 1).Trim()
$leaveImage = (docker compose @compose images -q leave-service | Select-Object -First 1).Trim()
$imageTag = "hr=$hrImage;leave=$leaveImage"

Write-Host "Git SHA: $gitSha"
Write-Host "Image tag: $imageTag"
```

### 9.2. Chạy workload có phân trang 10/50/100

Mỗi lần chạy giữ nguyên 100 VU, 5 phút và cùng database snapshot. Workforce chỉ đọc dữ liệu; không chạy Approval hoặc seed lại trong lúc đang so sánh các page size.

```powershell
foreach ($size in @(10, 50, 100)) {
    foreach ($run in @(1, 2, 3)) {
        $report = "/reports/workforce-100vu-page$size-run$run.json"

        docker compose @compose --profile loadtest run --rm `
            -e VUS=100 `
            -e DURATION=5m `
            -e PAGE_SIZE=$size `
            -e GIT_SHA=$gitSha `
            -e IMAGE_TAG=$imageTag `
            -e REPORT_JSON=$report `
            k6 run /scripts/workforce.js

        if ($LASTEXITCODE -ne 0) {
            throw "Workforce failed: PAGE_SIZE=$size, run=$run"
        }
    }
}
```

K6 setup kiểm tra trước mỗi lượt rằng bỏ page/pageSize ở Employees, Leave Requests, Pending Approval và Manager Team phải trả HTTP 400. Sau đó mới warm-up endpoint với page size hợp lệ. Nếu contract check thất bại, script dừng ở setup; không được coi lượt đó là benchmark thành công.

Raw JSON nằm trong `docs/testing/evidence/dynamic-authorization/`. Kiểm tra trường `benchmark_metadata` trong từng JSON, đặc biệt `git_sha`, `image_tag`, `page_size`, `vus`, `duration` và `generated_at_utc`, trước khi đưa kết quả vào báo cáo.

### 9.3. Chạy API contract tests

HR API có test xác nhận Employees list từ chối thiếu/sai page và pageSize. Leave API có TestServer coverage cho Employees tương ứng ở HR và cả ba list endpoint Leave: danh sách toàn bộ, danh sách mine và pending approval.

```powershell
dotnet test .\src\hr-service\HrService.slnx
dotnet test .\src\leave-service\LeaveService.slnx
```

Để nghiệm thu cả integration với SQL Server/Redis, phải cấu hình `HR_TEST_SQL_CONNECTION_STRING` và `HR_TEST_REDIS_CONNECTION_STRING` như Mục 2. Điều kiện cuối cùng vẫn là `failed = 0`, `skipped = 0` ở cả hai solution. TestServer contract tests không thay thế SQL/Redis integration tests.
