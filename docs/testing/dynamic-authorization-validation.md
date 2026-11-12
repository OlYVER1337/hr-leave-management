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

Ghi Workforce và Approval thành các nhóm riêng, không gộp RPS của hai workload vì cách sinh tải và số lần HR lookup khác nhau.

| Workload | VU | Planned | Actual | Direct HR calls | Direct HR total (ms) | Leave → HR calls | Leave → HR total (ms) | Leave → HR avg/p95 (ms) | Request p95 (ms) | Error rate |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Workforce | 50 | 5 phút | | | | | | | | |
| Workforce | 100 | 5 phút | | | | | | | | |
| Approval | 50 | 500 approvals | | 0 | 0 | | | | | |
| Approval | 100 | 1.000 approvals | | 0 | 0 | | | | | |

Raw k6 summaries được cấu hình ghi vào `docs/testing/evidence/dynamic-authorization/` qua `REPORT_JSON`. Kiểm tra các file thực sự tồn tại trước khi commit. Không sử dụng kết quả benchmark lịch sử trước Dynamic RBAC làm kết quả của phiên bản này. Ghi rõ commit SHA, thời điểm, cấu hình cache, số nhân viên/đơn và kết quả lỗi cùng với bảng.

Lưu ý: `workforce-debug.json` là kết quả chạy thử để chẩn đoán, không phải workload 50 VU chính thức. Metrics trong file ghi nhận `vus = 5` và thời gian chạy khoảng 60,85 giây; không dùng file này để điền dòng Workforce 50 VU trong bảng nghiệm thu.

## 7. Giới hạn và an toàn

- Chạy trên stack `hrbench` cô lập, không trỏ test tới production.
- Benchmark Approval thay đổi trạng thái đơn LOADTEST; seed lại trước từng lần chạy để các lượt bắt đầu từ cùng trạng thái Pending.
- Approval hiện là burst concurrency với iteration cố định, không phải sustained load 5 phút. Muốn kiểm tra tải duy trì cần tạo scenario riêng dùng executor theo thời gian và chuẩn bị dữ liệu Pending đủ lớn/được làm mới an toàn.
- Không kết luận benchmark đạt chỉ từ HTTP success; đối chiếu cả HR-call count, HR latency, request p95 và error rate.
- Không commit file `.env`, token, mật khẩu, hoặc log có dữ liệu nhạy cảm.
