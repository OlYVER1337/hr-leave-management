# Báo cáo Redis cache và kiểm thử tải

## 1. Mục tiêu

Redis được sử dụng để giảm thời gian đọc các nhóm dữ liệu danh mục thường xuyên
được truy vấn. Redis chỉ là lớp tăng tốc; SQL Server vẫn là nguồn dữ liệu chính
thức. Các luồng tạo và xử lý đơn nghỉ không được dựa riêng vào dữ liệu cache để
tính ngày nghỉ, kiểm tra số dư hoặc ra quyết định phê duyệt.

## 2. Thiết kế cache và tính đúng dữ liệu

HR Service lưu cache ba nhóm dữ liệu danh mục sau:

| Nhóm dữ liệu | Redis key | TTL mặc định | Cơ chế invalidation |
|---|---|---:|---|
| Loại nghỉ phép | `hr:catalog:leave-types` | 1.800 giây (30 phút) | Xóa cache khi tạo/cập nhật loại nghỉ; cập nhật loại nghỉ cũng xóa cache chính sách |
| Chính sách nghỉ phép | `hr:catalog:leave-policies` | 1.800 giây (30 phút) | Xóa cache khi tạo/cập nhật chính sách hoặc cập nhật loại nghỉ liên quan |
| Ngày lễ | `hr:catalog:holidays` | 1.800 giây (30 phút) | Xóa cache khi tạo/cập nhật/xóa ngày lễ |

`Cache:ExpirationSeconds` cấu hình TTL chung, trong khoảng từ 1 đến 3.600 giây.
Giá trị mặc định là 1.800 giây (30 phút).
Khi cache miss, dịch vụ đọc SQL Server, trả kết quả và thử ghi dữ liệu vào Redis.
Lỗi đọc, ghi hoặc xóa cache được ghi log; khi Redis lỗi, thao tác đọc quay về
SQL và các thao tác ghi dữ liệu vẫn tiếp tục qua SQL. Nếu không thể invalidation
trong lúc Redis gián đoạn, dữ liệu đọc thông thường có thể cũ cho đến khi TTL hết.

Luồng nghiệp vụ nghỉ phép cần dữ liệu loại nghỉ, chính sách và ngày lễ hiện hành
để tính ngày làm việc và phân bổ số dư. Vì vậy Leave Service gọi các endpoint HR
với `authoritative=true`; HR Service bỏ qua Redis và đọc trực tiếp SQL Server.
Việc kiểm tra số dư, chuyển trạng thái và ghi lịch sử đơn nghỉ được thực hiện
trong transaction trên SQL của Leave Service. Quyền phê duyệt được xác minh bằng
cấp quản lý hiện hành từ HR, không dựa trên thông tin phê duyệt cache.

Kiểm thử tích hợp SQL bao gồm tạo các đơn nghỉ trùng ngày đồng thời trên cùng
số dư, hai quyết định phê duyệt/từ chối cạnh tranh và thay đổi người phê duyệt.
Cả sáu kiểm thử persistence/concurrency của Leave Service đều pass với SQL Server.
Kiểm thử tích hợp SQL/Redis của HR Service cũng pass. Unit test xác nhận Redis
timeout vẫn fallback về SQL và truy vấn authoritative không đọc cache.
Leave Service thêm `authoritative=true` khi gọi HR để lấy loại nghỉ, chính sách
theo năm và ngày lễ; kiểm thử client xác nhận chính xác query string này được gửi.

Đã kiểm tra outage trên Compose project cô lập `hrfault`: khi dừng Redis, ba
endpoint danh mục vẫn trả HTTP 200 nhờ fallback SQL; lượt smoke test 10 giây có
7 request, 0 lỗi HTTP, p50 3.419 giây và p95 3.972 giây. Độ trễ cao hơn do Redis
không kết nối được, nên đây là kiểm tra chịu lỗi, không phải số liệu benchmark.
Khi dừng HR Service, tạo đơn nghỉ trả HTTP 503 với ProblemDetails
`Dependency unavailable`; truy vấn danh sách đơn của người gửi xác nhận đơn thử
không được ghi vào Leave DB. HR Service được khởi động lại và trở về trạng thái
healthy sau kiểm tra.

## 3. Cách chạy kiểm thử tải

Benchmark chạy trong Compose project riêng tên `hrbench`, với volume và cổng
riêng cho SQL/Redis. Cấu hình này không tạo lại hay thay đổi các container thông
thường của dự án (`hr-leave`). Benchmark sử dụng thông tin đăng nhập HR từ file
`.env` cục bộ; không chia sẻ hoặc đưa thông tin này vào báo cáo. Trong `.env`,
bật seed HR và manager ban đầu, hoặc chuẩn bị sẵn một Manager đang hoạt động cùng
loại nghỉ `ANNUAL` đang hoạt động.

Chạy các lệnh sau từ thư mục gốc repository trong PowerShell:

```powershell
docker compose -p hrbench -f docker-compose.yml -f docker-compose.loadtest.yml up -d --build
docker compose -p hrbench -f docker-compose.yml -f docker-compose.loadtest.yml ps
docker compose -p hrbench -f docker-compose.yml -f docker-compose.loadtest.yml cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql
docker compose -p hrbench -f docker-compose.yml -f docker-compose.loadtest.yml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $env:SA_PASSWORD -C -d master -i /tmp/seed-scale-data.sql
```

Mặc định script tạo dữ liệu 1.000 nhân viên và 5.000 đơn nghỉ, phân bổ vào các
trạng thái chờ duyệt/đã duyệt/từ chối/đã hủy, đồng thời tạo số dư nghỉ phép,
phân bổ theo năm và lịch sử tương ứng. Có thể chỉnh hai giá trị `:setvar` ở đầu
file `scripts/load-test/seed-scale-data.sql` để chọn từ 1.000 đến 5.000 nhân viên
và từ 1 đến 20 đơn mỗi nhân viên. Chạy lại script trên cùng tập nhân viên sẽ chỉ
làm mới các đơn và số dư phục vụ kiểm thử của tài khoản
`loadtest.user.*@example.invalid`.

### Seed 1.000 nhân viên: quy trình đầy đủ

Script seed **không tự chạy** khi chỉ chạy `docker compose ... up`. Sau khi stack
`hrbench` đã khởi động và database/service ở trạng thái sẵn sàng, cần thực hiện
đúng hai bước: copy file SQL vào container SQL Server, sau đó chạy `sqlcmd`.

Mặc định trong `scripts/load-test/seed-scale-data.sql`:

```sql
:setvar EmployeeCount "1000"
:setvar RequestsPerEmployee "5"
```

Kết quả mong đợi là **1.000 load-test employees** và **5.000 leave requests**.
Có thể kiểm tra ngay sau khi seed:

```powershell
docker compose -p hrbench -f docker-compose.yml -f docker-compose.loadtest.yml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $env:SA_PASSWORD -C -d HR_DB -Q "SELECT COUNT(*) AS EmployeeCount FROM dbo.Employees WHERE Email LIKE 'loadtest.user.%@example.invalid';"

docker compose -p hrbench -f docker-compose.yml -f docker-compose.loadtest.yml exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $env:SA_PASSWORD -C -d Leave_DB -Q "SELECT COUNT(*) AS LoadTestRequests FROM dbo.LeaveRequests WHERE Reason LIKE 'LOADTEST:%';"
```

Hai kết quả mong đợi lần lượt là **1000** và **5000**. Nếu chưa có Manager đang
hoạt động hoặc chưa có leave type `ANNUAL` đang hoạt động, script sẽ dừng với lỗi
thay vì tạo dữ liệu không đầy đủ.

Mỗi lượt kiểm thử kéo dài 5 phút và gửi các yêu cầu đọc có xác thực tới endpoint
loại nghỉ, chính sách năm và ngày lễ. Script k6 đã khai báo scenario, do đó cần
truyền số VU và thời lượng qua biến môi trường `VUS` và `DURATION`; các tùy chọn
CLI `--vus`/`--duration` không ghi đè scenario này. Sau khi đổi cấu hình cache,
chờ `hr-service` chuyển sang trạng thái healthy trước khi chạy tiếp.

```powershell
$compose = @('compose', '-p', 'hrbench', '-f', 'docker-compose.yml', '-f', 'docker-compose.loadtest.yml')
docker compose @compose run --rm -e VUS=50 -e DURATION=5m k6 run /scripts/catalog.js
docker compose @compose run --rm -e VUS=100 -e DURATION=5m k6 run /scripts/catalog.js

$env:CACHE_ENABLED = 'false'
docker compose @compose up -d --force-recreate hr-service
docker compose @compose run --rm -e VUS=50 -e DURATION=5m k6 run /scripts/catalog.js
docker compose @compose run --rm -e VUS=100 -e DURATION=5m k6 run /scripts/catalog.js
```

### Workload danh sách nhân viên và đơn nghỉ

`scripts/load-test/workforce.js` đo các API đọc trên dataset đã seed: danh sách
nhân viên, tất cả đơn nghỉ và lịch nghỉ bằng tài khoản HR, cùng hộp thư phê duyệt
bằng tài khoản Manager. Workload chỉ đọc dữ liệu, không tạo hoặc thay đổi đơn.
Seed từ 1.000 nhân viên và 1–20 đơn mỗi nhân viên bằng `seed-scale-data.sql` trước
khi chạy; kiểm tra có HR/Manager hoạt động và cấu hình email/mật khẩu riêng trong
`.env`. Kết quả workload này cần được báo cáo riêng, không gộp vào số liệu
catalog bên dưới.

```powershell
docker compose @compose run --rm -e VUS=50 -e DURATION=5m k6 run /scripts/workforce.js
docker compose @compose run --rm -e VUS=100 -e DURATION=5m k6 run /scripts/workforce.js
```

Chạy lại hai mức trên khi `CACHE_ENABLED=false` nếu cần so sánh ON/OFF. Dùng
Compose project `hrbench` cô lập như các lệnh bên trên. Đã chạy workforce
benchmark Redis ON ngày 03/11/2026 trên 1.000 nhân viên và 5.000 đơn nghỉ:

| VU | Thời lượng | Requests | RPS | p50 (ms) | p95 (ms) | HTTP lỗi |
|---:|---:|---:|---:|---:|---:|---:|
| 50 | 5 phút | 6.919 | 22,84 | 2.014 | 4.701 | 0% |
| 100 — lượt đầu | 5 phút | 6.837 | 20,08 | 4.213 | 11.055 | 0,31% (21 HTTP 500) |
| 100 — chạy lại, có giám sát | 5 phút | 4.480 | — | — | 17.225 | 0% |
| 100 — chạy xác nhận | 5 phút | 5.876 | 19,18 | 4.615 | 13.470 | 0% |

Lượt đầu ghi nhận HTTP 500 ở ba endpoint đọc của Leave Service; response chỉ có
ProblemDetails tổng quát và log không cho biết nguyên nhân cụ thể. Hai lượt chạy
lại 100 VU/5 phút đều không có HTTP error; lượt đầu chạy kèm lấy mẫu SQL DMV và
theo dõi log trực tiếp, còn lượt xác nhận không chạy các phép giám sát đồng thời.
Do lỗi lịch sử chưa tái hiện, chưa thể xác nhận nguyên nhân hay coi đây là một
code fix. Giữ việc truy nguyên ở trạng thái mở; nếu lỗi xuất hiện lại, cần lưu
exception/stack trace và endpoint cụ thể trước khi sửa. Script đặt ngưỡng
`http_req_failed < 0,1%` để tỷ lệ lỗi tương tự lượt đầu làm benchmark thất bại.
Đây là workload đọc đồng đều bốn route, không phải mô phỏng tần suất người dùng
production; percentile chịu ảnh hưởng lớn từ các response danh sách lớn.

Kết quả smoke-test 1 VU/10 giây là 45 request, 0 lỗi; không dùng smoke-test làm
thay thế cho các lượt 5 phút. Tóm tắt có thể tái kiểm tra tại
[workforce load summary](../testing/evidence/workforce-load-summary.json).

### Đo số lần gọi và thời gian HR Service

Trong Compose override cho load test, Leave Service bật
`LoadTest__EnableHrCallMetrics=true`. HrServiceClient đo thời gian mỗi HTTP call
đến HR Service; middleware trả các giá trị theo từng request của Leave API qua
`X-HR-Call-Count` và `X-HR-Call-Durations-Ms`. Cấu hình thường không bật
telemetry này và không thêm các response header trên.

`workforce.js` tổng hợp riêng:
- `hr_direct_requests`, `hr_direct_avg_ms`, `hr_direct_p95_ms`: các request
  trực tiếp vào HR API, ví dụ GET employees.
- `leave_to_hr_calls`, `leave_to_hr_total_time_ms`, `leave_to_hr_avg_ms`,
  `leave_to_hr_p95_ms`: số lượng và thời gian các HTTP call nội bộ từ Leave
  Service sang HR Service. Route manager_approvals sẽ gọi HR để lấy Team hiện tại.

Để đo HR lookup tại thời điểm approve, dùng thêm
`scripts/load-test/approval.js`. Script lấy danh sách đơn LOADTEST đang Pending
trong setup, sau đó mỗi VU duyệt các request ID riêng biệt. Mỗi lần approve của
Manager phải gọi HR Service để xác minh ManagerId hiện tại. Mặc định mỗi VU duyệt
10 đơn; với 50 VU là 500 quyết định và 100 VU là 1.000 quyết định. Script không
lặp lại một request đã duyệt. Hãy chạy lại seed SQL trước mỗi lượt approval để
khôi phục các đơn Pending; 1.000 nhân viên × 5 request tạo khoảng 1.250 đơn
Pending theo fixture hiện tại.

Chạy workforce reads 50 và 100 VU:

```powershell
docker compose @compose run --rm -e VUS=50 -e DURATION=5m k6 run /scripts/workforce.js
docker compose @compose run --rm -e VUS=100 -e DURATION=5m k6 run /scripts/workforce.js
```

Chạy decision/approval 50 VU; trước lượt 100 VU, seed lại dữ liệu để khôi phục
trạng thái Pending:

```powershell
docker compose @compose run --rm -e VUS=50 -e ITERATIONS_PER_VU=10 -e DURATION=5m k6 run /scripts/approval.js

docker compose @compose cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql
docker compose @compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $env:SA_PASSWORD -C -d master -i /tmp/seed-scale-data.sql

docker compose @compose run --rm -e VUS=100 -e ITERATIONS_PER_VU=10 -e DURATION=5m k6 run /scripts/approval.js
```

Cả hai script in JSON summary có số HR call, tổng thời gian, trung bình và p95.
Khi ghi báo cáo 50/100 VU, giữ kết quả workforce và approval thành hai workload
riêng: workload đầu đo HR team-scope lookup cho hộp chờ qua
`GET /api/v1/leave-scope/managers/{managerId}/employee-ids`, workload sau đo lookup
nhân viên khi quyết định (`GET /api/v1/lookup/employees/{employeeId}`). Không cộng
hai tập số liệu thành một RPS chung vì số operation
và hành vi DB khác nhau.

Sau khi đổi `CACHE_ENABLED`, cần đợi `hr-service` healthy. Khôi phục cache ON cho
stack benchmark rồi chỉ xóa Compose project `hrbench` cùng các volume của project:

```powershell
$env:CACHE_ENABLED = 'true'
docker compose @compose up -d --force-recreate hr-service
docker compose @compose down -v
```

Lệnh `down -v` chỉ được dùng với đúng tên project benchmark như trên; không chạy
lệnh này với Compose project thông thường vì nó xóa volume SQL và Redis của project.

## 4. Môi trường và kết quả đo

Cả bốn lượt được chạy trên stack `hrbench` cô lập, Windows với Docker Desktop.
Máy có 16 CPU và 8.182.980.608 byte bộ nhớ (khoảng 7,62 GiB). Các container sử
dụng SQL Server `2022-latest` và Redis `7-alpine`. Dữ liệu gồm 1.000 nhân viên
và 5.000 đơn nghỉ. Ba key danh mục được làm nóng trong bước setup trước mỗi lượt.
Mỗi lượt kéo dài 5 phút; access token của riêng stack benchmark có thời hạn
30 phút để không hết hạn giữa lượt đo. Benchmark không đặt giới hạn CPU/RAM riêng
cho từng container.

| Cache | VU | Số request | RPS | p50 (ms) | p95 (ms) | Tỷ lệ lỗi HTTP |
|---|---:|---:|---:|---:|---:|---:|
| Bật | 50 | 144.408 | 481,12 | 2,326 | 5,885 | 0% |
| Tắt | 50 | 141.894 | 471,97 | 3,823 | 10,663 | 0% |
| Bật | 100 | 287.490 | 957,62 | 2,556 | 7,857 | 0% |
| Tắt | 100 | 281.622 | 938,09 | 4,217 | 13,913 | 0% |

Trong môi trường và bộ dữ liệu này, bật Redis tăng throughput khoảng 2%, giảm
p50 khoảng 39% và p95 khoảng 44% ở cả hai mức tải. RPS chỉ tăng nhẹ trong khi
latency giảm đáng kể, cho thấy throughput còn chịu ảnh hưởng của các chi phí
khác trong vòng đời request, gồm nhịp gửi request cố định của k6, xử lý của ứng
dụng và overhead mạng/container. Khi tắt Redis, SQL phục vụ cả ba nhóm danh mục;
độ trễ cao hơn nhưng workload đọc đã thử vẫn hoàn tất mà không có lỗi HTTP.

## 5. Nhận xét và hướng tối ưu

Số liệu trên chỉ phản ánh môi trường benchmark cụ thể, không phải cam kết hiệu
năng cho mọi máy. Kết quả một node bao gồm chi phí mạng/container, xử lý API và
truy vấn SQL, vì vậy không thể xem chênh lệch này là phép đo riêng cho Redis.

Khi tắt cache, thời gian truy vấn SQL và áp lực lên connection pool có thể trở
thành bottleneck. Khi tăng tải, CPU/serialization của API và kết nối Redis/mạng
dùng chung cũng cần được đo. Đây là các giả thuyết cần xác minh trước khi tối ưu:
sử dụng execution plan của SQL Server, `dotnet-counters` và metrics CPU/bộ nhớ
của container trong cùng workload. Chỉ sau khi có số đo mới cân nhắc thay đổi
index, connection pooling hoặc chiến lược cache. Các thao tác cập nhật số dư và
quyết định phê duyệt phải tiếp tục dựa trên transaction SQL, không đánh đổi tính
đúng đắn để tăng cache hit.


## 6. Workforce paged-v2 bổ sung — 16/11/2026

Workforce benchmark mới nhất được thực hiện với kịch bản paged-v2, page size 50, 5 phút/lượt, trên sáu lần chạy (50 và 100 VU, mỗi mức ba lần). Kết quả:

| VU | RPS trung bình | P50 trung bình | P95 trung bình | HTTP errors |
|---:|---:|---:|---:|---:|
| 50 | 454,07 | 9,37 ms | 17,85 ms | 0 |
| 100 | 764,14 | 23,66 ms | 73,28 ms | 0 |

Đây là workload Workforce đọc dữ liệu có phân trang, **không phải phép đo Redis ON/OFF**. Sáu JSON mới có metadata page_size=50 và duration=5m nhưng git_sha/image_tag đều not-provided, nên chưa gắn được với một binary revision cố định.

Các bảng Catalog Redis ON/OFF ở mục 4 vẫn là benchmark riêng cho ba catalog endpoints. Không dùng kết quả Catalog để kết luận Redis làm tăng/giảm throughput của Workforce paged-v2. Cũng không lấy Workforce paged-v2 làm phép so sánh với các Workforce request không phân trang ở lượt cũ, vì API contract hiện bắt buộc page/pageSize.

Chi tiết từng lượt và phân tích p95 nằm trong [Dynamic Authorization benchmark report](../testing/dynamic-authorization-benchmark-results.md). Nếu cần đánh giá cache ảnh hưởng tới Workforce, phải chạy một cặp ON/OFF từ cùng Git SHA, cùng image, dataset, page size, VU, duration và resource limits.


## 7. Approval sustained load với k6 constant-vus

Kịch bản sustained được tách khỏi `approval.js` để giữ nguyên khả năng tái lập benchmark burst lịch sử. Script mới là `scripts/load-test/approval-sustained.js`, dùng executor `constant-vus`: số VU được giữ liên tục trong suốt `DURATION`, mỗi vòng approve một request ID duy nhất rồi nghỉ theo `THINK_TIME_SECONDS`.

### 7.1. Cấu hình baseline

| Tham số | Giá trị | Ý nghĩa |
|---|---:|---|
| VUS | 50 | 50 Manager VU hoạt động liên tục |
| DURATION | 5m | Thời lượng scenario |
| LOADTEST_MANAGER_COUNT | 25 | 25 Manager load-test, hai VU/Manager |
| Seed EmployeeCount | 4.000 | Nhân viên load-test |
| Seed RequestsPerEmployee | 20 | 20 request/nhân viên |
| Seed ManagerCount | 25 | Phân bổ đều nhân viên cho Manager |
| MAX_APPROVALS_PER_VU | 400 | Số ID Pending duy nhất dự phòng cho mỗi VU |
| THINK_TIME_SECONDS | 1 | Nghỉ sau mỗi quyết định để giới hạn tốc độ tiêu thụ dữ liệu |
| Inbox page size | 100 | Đọc đủ các trang Pending có Reason bắt đầu bằng `LOADTEST:` |

Với seed fixture hiện tại, mỗi nhân viên có 20 đơn, trong đó 5 đơn Pending. 4.000 nhân viên được chia đều cho 25 Manager (160 nhân viên/Manager), tạo 800 đơn Pending/Manager. Hai VU/Manager được cấp 400 ID riêng mỗi VU, tổng capacity 20.000 quyết định duy nhất. Kịch bản không duyệt lặp lại một ID; nếu thiếu dữ liệu hoặc VU dùng hết ID đã cấp, test fail thay vì tái sử dụng request.

Capacity 20.000 là giới hạn dữ liệu, không phải số lượng chắc chắn sẽ hoàn tất. Với 50 VU và think time 1 giây trong 5 phút, số approval thực tế phụ thuộc latency của hệ thống. Báo cáo phải dùng số đo k6 thực tế.

### 7.2. Chuẩn bị

Chạy trên stack benchmark cô lập `hrbench`, không dùng database nghiệp vụ. Trước tiên đồng bộ branch có script sustained và ghi SHA:

```powershell
git fetch origin
git switch benchmark/approval-constant-vus
git pull --ff-only
$env:GIT_SHA = (git rev-parse HEAD).Trim()
```

Khai báo Compose arguments:

```powershell
$compose = @(
  "-p", "hrbench",
  "-f", "docker-compose.yml",
  "-f", "docker-compose.loadtest.yml",
  "--profile", "loadtest"
)
```

Đảm bảo stack `hrbench` đã chạy healthy và credentials benchmark trong `.env` đã được nạp. Seed 4.000 nhân viên và 80.000 đơn:

```powershell
docker compose @compose cp .\scripts\load-test\seed-scale-data.sql sqlserver:/tmp/seed-scale-data.sql

docker compose @compose exec -T sqlserver `
  /opt/mssql-tools18/bin/sqlcmd `
  -S localhost -U sa -P $env:SA_PASSWORD -C -b -d master `
  -v EmployeeCount=4000 RequestsPerEmployee=20 ManagerCount=25 `
  -i /tmp/seed-scale-data.sql
```

### 7.3. Xác minh seed trước khi chạy

Copy và thực thi script kiểm tra:

```powershell
docker compose @compose cp .\scripts\load-test\verify-approval-seed.sql sqlserver:/tmp/verify-approval-seed.sql

docker compose @compose exec -T sqlserver `
  /opt/mssql-tools18/bin/sqlcmd `
  -S localhost -U sa -P $env:SA_PASSWORD -C -b -d master `
  -v ManagerCount=25 ExpectedPendingPerManager=800 `
  -i /tmp/verify-approval-seed.sql
```

Chỉ chạy k6 khi đủ 25 Manager và mỗi Manager có ít nhất 800 đơn Pending `LOADTEST:`. Script k6 cũng tự đăng nhập, đọc inbox qua pagination và kiểm tra đủ ID cho VU được giao trước khi bước vào scenario.

### 7.4. Ghi nhận image ID và chạy 50 VU / 5 phút

```powershell
$env:HR_IMAGE_ID = (docker inspect --format '{{.Image}}' hrbench-hr-service-1).Trim()
$env:LEAVE_IMAGE_ID = (docker inspect --format '{{.Image}}' hrbench-leave-service-1).Trim()

$env:REPORT_JSON = "/reports/approval-constant-vus-50-5m.json"

docker compose @compose run --rm `
  -e VUS=50 `
  -e DURATION=5m `
  -e LOADTEST_MANAGER_COUNT=25 `
  -e MAX_APPROVALS_PER_VU=400 `
  -e THINK_TIME_SECONDS=1 `
  -e SEED_EMPLOYEE_COUNT=4000 `
  -e REQUESTS_PER_EMPLOYEE=20 `
  -e GIT_SHA=$env:GIT_SHA `
  -e HR_IMAGE_ID=$env:HR_IMAGE_ID `
  -e LEAVE_IMAGE_ID=$env:LEAVE_IMAGE_ID `
  -e REPORT_JSON=$env:REPORT_JSON `
  k6 run /scripts/approval-sustained.js
```

Không chạy workload benchmark khác đồng thời. Theo dõi tài nguyên trong lúc chạy bằng `docker stats` ở một terminal riêng. Script xuất summary gồm số approval attempts/completed/errors, RPS, p50/p95, số và thời gian HTTP call nội bộ Leave → HR; raw k6 summary được ghi vào `docs/testing/evidence/dynamic-authorization/approval-constant-vus-50-5m.json` trên máy chạy do thư mục đó được mount vào `/reports`.

### 7.5. Sau lượt chạy

Xác minh raw report tồn tại và kiểm tra working tree:

```powershell
Test-Path .\docs\testing\evidence\dynamic-authorization\approval-constant-vus-50-5m.json
git status --short
```

Chỉ ghi kết quả vào benchmark report sau khi đối chiếu raw JSON, Git SHA và hai image ID. Ghi rõ đây là sustained constant-VU workload; không so trực tiếp RPS với benchmark per-vu-iterations burst vì executor và think time khác nhau. Reseed trước mỗi lượt lặp lại vì các request đã được approve sẽ không còn Pending.
