# Xác minh Dynamic RBAC và benchmark HR lookup

Tài liệu này dùng cho branch `feature/dynamic-authorization-v1`. Các integration test tạo/xóa database kiểm thử ngẫu nhiên; chỉ chạy với SQL Server và Redis riêng cho môi trường test/benchmark.

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

Script lấy `SA_PASSWORD` trực tiếp từ file `.env`, tự tạo connection string cho test và kiểm tra summary của từng solution. Script trả lỗi nếu có test fail, không tìm thấy summary hoặc `skipped > 0`.

```powershell
.\scripts\test\run-all-tests.ps1 -SqlServer "localhost,11433" -RedisServer "localhost:16379"
```

Điều kiện nghiệm thu: HR Service và Leave Service đều có `failed = 0`, `skipped = 0`. Giữ nguyên toàn bộ log Test summary để lưu bằng chứng.

## 3. Seed dữ liệu tải

Seed tạo/cập nhật bộ dữ liệu 1.000 nhân viên load-test và khoảng 5.000 đơn nghỉ, trong đó khoảng 25% đang Pending. Nó chỉ xóa đơn và số dư thuộc nhóm nhân viên mã `LT*` / lý do `LOADTEST:*` đã được chuẩn bị cho benchmark.

```powershell
docker compose @compose cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql

docker compose @compose exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d master -i /tmp/seed-scale-data.sql'
```

Lệnh SQL lấy mật khẩu từ biến môi trường trong container SQL Server; không cần nhập hoặc in mật khẩu trong terminal.

## 4. Workforce reads — 50 và 100 VU

Workload phân phối request đến HR Employees, Leave Requests, Leave Calendar và Manager Approval Inbox. Metric tách biệt request trực tiếp vào HR và các HTTP call nội bộ từ Leave Service sang HR.

```powershell
docker compose @compose run --rm -e VUS=50 -e DURATION=5m -e REPORT_JSON=/reports/workforce-50.json k6 run /scripts/workforce.js

docker compose @compose run --rm -e VUS=100 -e DURATION=5m -e REPORT_JSON=/reports/workforce-100.json k6 run /scripts/workforce.js
```

Đọc các trường trong JSON summary:

- `hr_direct_requests`, `hr_direct_total_time_ms`, `hr_direct_avg_ms`, `hr_direct_p95_ms`: số request trực tiếp tới HR API, tổng thời gian, trung bình và p95.
- `leave_to_hr_calls`, `leave_to_hr_calls_per_second`: tổng outbound HR calls phát sinh bên trong Leave Service.
- `leave_to_hr_total_time_ms`, `leave_to_hr_avg_ms`, `leave_to_hr_p95_ms`: tổng thời gian tích lũy và phân vị thời gian từng HTTP call Leave → HR.
- `p95_ms`, `error_rate`: độ trễ và tỷ lệ lỗi của workload tổng.

Các số liệu outbound HR được đọc từ response headers chỉ bật trong Compose benchmark. Header không được bật ở cấu hình thông thường.

## 5. Approval decisions — 50 và 100 VU

Workload này đo riêng lookup HR tại thời điểm duyệt. Mỗi VU duyệt 10 đơn LOADTEST Pending bằng request ID riêng; 50 VU tạo 500 lần approve và 100 VU tạo 1.000 lần approve.

Chạy 50 VU:

```powershell
docker compose @compose run --rm -e VUS=50 -e ITERATIONS_PER_VU=10 -e DURATION=5m -e REPORT_JSON=/reports/approval-50.json k6 run /scripts/approval.js
```

Phải seed lại để khôi phục trạng thái Pending trước lượt 100 VU:

```powershell
docker compose @compose cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql

docker compose @compose exec -T sqlserver /bin/bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -d master -i /tmp/seed-scale-data.sql'

docker compose @compose run --rm -e VUS=100 -e ITERATIONS_PER_VU=10 -e DURATION=5m -e REPORT_JSON=/reports/approval-100.json k6 run /scripts/approval.js
```

Đọc `approval_attempts`, `completed_approvals`, `approval_errors`, `p50_ms`, `p95_ms`, `leave_to_hr_calls`, `leave_to_hr_total_time_ms`, `leave_to_hr_avg_ms` và `leave_to_hr_p95_ms`. P50/p95 chỉ tính thời gian approve trong workload, không tính login và lần đọc inbox ở setup. Với mỗi lần approve Team, kỳ vọng một HR lookup để xác nhận ManagerId hiện tại; Scope All không thực hiện lookup này.

## 6. Cách ghi nhận kết quả

Ghi bốn workload thành bốn dòng riêng, không gộp workforce và approval thành một RPS vì thao tác và số lần HR lookup khác nhau.

| Workload | VU | Planned | Actual requests/approvals | HR calls | HR total time (ms) | HR avg (ms) | HR p95 (ms) | Error rate |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Workforce | 50 | 5 phút | | | | | | |
| Workforce | 100 | 5 phút | | | | | | |
| Approval | 50 | 500 approvals | | | | | | |
| Approval | 100 | 1.000 approvals | | | | | | |

Raw k6 summaries được lưu trong `docs/testing/evidence/dynamic-authorization/` với bốn file `workforce-50.json`, `workforce-100.json`, `approval-50.json` và `approval-100.json`. Không sử dụng kết quả benchmark lịch sử trước Dynamic RBAC làm kết quả của phiên bản này. Ghi rõ commit SHA, thời điểm, cấu hình cache, số nhân viên/đơn và kết quả lỗi cùng với bảng.

## 7. Giới hạn và an toàn

- Chạy trên stack `hrbench` cô lập, không trỏ test tới production.
- Benchmark approval thay đổi trạng thái đơn LOADTEST; seed lại giữa hai mức VU để kết quả có điểm xuất phát tương đương.
- Không commit file `.env`, token, mật khẩu, hoặc log có dữ liệu nhạy cảm.
- Không kết luận benchmark đạt chỉ từ HTTP success; đối chiếu cả HR-call count, HR latency, request p95 và error rate.
