# Kết quả benchmark Dynamic RBAC và HR lookup

## 1. Thông tin lần đo

| Thuộc tính | Giá trị |
|---|---|
| Ngày thực hiện | 12/11/2026 |
| Branch | `feature/dynamic-authorization-v1` |
| Compose project | `hrbench` |
| Compose files | `docker-compose.yml`, `docker-compose.loadtest.yml` |
| HR / Leave | ASP.NET Core trên .NET 10 |
| Database | SQL Server 2022 |
| Cache | Redis 7 |
| Load generator | Image `grafana/k6:latest` |
| HR metrics | Bật qua `LoadTest__EnableHrCallMetrics=true` trong Compose load-test |

Stack benchmark sử dụng cổng riêng: HR `18081`, Leave `18082`, SQL Server `11433`, Redis `16379`. Kết quả là số liệu thực thi trên máy local do người chạy benchmark cung cấp; không phải kết quả CI hoặc môi trường production.

Branch HEAD tại thời điểm cập nhật tài liệu là `23cfcb8ecea6302df6bf3c9fcd6232ee70de81b9`. SHA được checkout trên máy tại đúng thời điểm chạy benchmark chưa được ghi lại riêng; cần đối chiếu `git rev-parse HEAD` nếu sử dụng số liệu này trong biên bản nghiệm thu chính thức.

## 2. Dữ liệu và kiểm thử trước benchmark

Seed script `scripts/load-test/seed-scale-data.sql` tạo/cập nhật:

| Dữ liệu | Số lượng |
|---|---:|
| Load-test employees | 1.000 |
| Leave requests | 5.000 |
| Pending | 1.250 |
| Approved | 1.250 |
| Rejected | 1.250 |
| Cancelled | 1.250 |

Các đơn và nhân viên có dấu hiệu nhận diện kiểm thử `LOADTEST:` / `LT*`. Script seed gán nhân viên kiểm thử vào cùng một Manager đang hoạt động để tạo quan hệ Team có thể kiểm tra trong bài Approval. Seed lại sẽ xóa và tạo lại dữ liệu load-test cùng số dư tương ứng; không chạy trên database production.

Hai solution test đã được người chạy xác nhận trước đợt benchmark:

| Test suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| HR Service | 114 | 0 | 0 |
| Leave Service | 61 | 0 | 0 |
| **Tổng** | **175** | **0** | **0** |

## 3. Cách hiểu hai workload

### 3.1. Workforce reads

Script `scripts/load-test/workforce.js` dùng executor `constant-vus`, chạy liên tục theo `DURATION=5m` ở 50 hoặc 100 VU. Custom counter `workforce_requests` chỉ tính request đo trong workload; các request login và warm-up ở `setup()` không được cộng vào counter này.

RPS trong summary của script được tính bằng:

`workforce_requests / 300 giây`

Do đó RPS bên dưới là throughput theo thời lượng cấu hình 5 phút, không phải phép chia cho toàn bộ thời gian process kết thúc (bao gồm setup/graceful stop).

### 3.2. Approval decisions

Script `scripts/load-test/approval.js` dùng executor `per-vu-iterations`, mặc định `ITERATIONS_PER_VU=10`. Vì vậy:

- 50 VU tạo 500 approval attempts.
- 100 VU tạo 1.000 approval attempts.
- `DURATION=5m` được truyền vào `maxDuration`: đây là giới hạn tối đa để hoàn thành scenario, **không phải thời lượng tải duy trì 5 phút**.
- Các lần chạy thực tế hoàn thành trong khoảng 0,9–1,6 giây theo output k6.
- P50/p95 approval chỉ đo thời gian HTTP request approve; login và lần đọc pending inbox ở `setup()` không nằm trong các metric này.

Mỗi approval request kiểm tra trạng thái Team bằng cách gọi HR từ Leave Service. Vì approval làm thay đổi Pending thành Approved, phải seed lại trước **mỗi lần chạy Approval**, bao gồm cả các lần repeat.

## 4. Kết quả Workforce

### 4.1. Hiệu năng tổng thể

| Chỉ số | 50 VU | 100 VU |
|---|---:|---:|
| Thời lượng cấu hình | 5 phút | 5 phút |
| Workload requests | 12.897 | 13.607 |
| RPS theo script | 42,99 | 45,36 |
| P50 | 1.102,77 ms | 2.212,75 ms |
| P95 | 2.236,73 ms | 4.513,24 ms |
| HTTP errors | 0 | 0 |
| Error rate | 0% | 0% |

Khi tăng gấp đôi VU, throughput tăng khoảng 5,5%, trong khi p50 và p95 đều xấp xỉ gấp đôi. Đây là dấu hiệu khả năng xử lý không tăng tương ứng với concurrency ở mức tải đã thử. Kết quả chưa xác định được nút thắt nằm ở HR Service, Leave Service, SQL Server, payload hay tài nguyên máy.

### 4.2. HR lookup và phân bổ request

| Chỉ số | 50 VU | 100 VU |
|---|---:|---:|
| HR direct requests | 3.222 | 3.397 |
| HR direct tổng thời gian | 153.874,55 ms | 230.071,17 ms |
| HR direct avg | 47,76 ms | 67,73 ms |
| HR direct p95 | 97,28 ms | 133,94 ms |
| Leave API requests | 9.675 | 10.210 |
| Leave → HR calls | 3.221 | 3.391 |
| Leave → HR calls/giây | 10,74 | 11,30 |
| Leave → HR calls/Leave request | 0,3329 | 0,3321 |
| Leave → HR tổng thời gian | 387.812,57 ms | 743.290,80 ms |
| Leave → HR avg | 120,40 ms | 219,20 ms |
| Leave → HR p95 | 238,09 ms | 470,17 ms |

Tỷ lệ Leave → HR calls trên Leave request gần như giữ nguyên ở khoảng 0,333. Số liệu phù hợp với workload phân phối giữa các endpoint Leave, trong đó chỉ một phần request cần tra cứu Team từ HR. Không có dấu hiệu số lần gọi HR trên mỗi request tăng bất thường khi chuyển từ 50 lên 100 VU.

Ở JSON đầy đủ của Workforce 50 VU, k6 ghi nhận `data_received = 13.169.061.067 bytes` trên 12.903 HTTP requests, tương đương xấp xỉ 1,02 MB mỗi HTTP request nếu tính cả request setup. Cần kiểm tra kích thước response và cơ chế phân trang của các endpoint đọc dữ liệu trước khi kết luận đây là mức truyền tải mong muốn. Chưa có số liệu `data_received` tương ứng được cung cấp cho Workforce 100 VU.

### 4.3. Nhận xét Workforce

- Hai lần chạy đều có 0 HTTP errors.
- Throughput chỉ tăng 5,5% khi VU tăng 100%; p95 tăng từ 2,24 giây lên 4,51 giây.
- HR direct avg/p95 và Leave → HR avg/p95 đều tăng ở 100 VU.
- Tỷ lệ gọi HR của Leave Service ổn định, nên chưa có bằng chứng rằng số lượng lookup trên mỗi Leave request là nguyên nhân làm số lần gọi tăng bất thường.
- Cần đo tiếp CPU/SQL query/response size trong chính khoảng workload để xác định bottleneck. Chưa nên quy nguyên nhân cho Redis, SQL Server hoặc một service cụ thể.

## 5. Kết quả Approval

Bốn lần chạy được giữ riêng để thể hiện độ biến động giữa lần đầu và lần repeat. Mỗi lần có seed dữ liệu trước khi chạy.

| Lần chạy | VU | Planned / completed | P50 | P95 | Error rate | Leave → HR calls | HR avg | HR p95 | Thời gian scenario xấp xỉ |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Lần đầu | 50 | 500 / 500 | 27,62 ms | 833,40 ms | 0% | 500 | 12,66 ms | 62,76 ms | 1,6 giây |
| Lần đầu | 100 | 1.000 / 1.000 | 57,33 ms | 167,79 ms | 0% | 1.000 | 8,53 ms | 20,58 ms | 1,4 giây |
| Repeat | 50 | 500 / 500 | 20,65 ms | 89,97 ms | 0% | 500 | 4,31 ms | 9,82 ms | 0,9 giây |
| Repeat | 100 | 1.000 / 1.000 | 58,51 ms | 138,63 ms | 0% | 1.000 | 14,95 ms | 49,38 ms | 1,4 giây |

### 5.1. Nhận xét Approval

- Cả bốn lần chạy đều hoàn thành đủ số approval dự kiến, không có approval error.
- Số HR calls bằng đúng số approval attempts trong cả bốn lần: 1 HR lookup cho mỗi approval trong workload Team.
- So sánh hai lần repeat: khi tăng từ 50 lên 100 VU, p50 tăng khoảng 183%, p95 tăng khoảng 54%; HR lookup p95 tăng từ 9,82 ms lên 49,38 ms.
- Lần đầu 50 VU có p95 833,40 ms, trong khi lần repeat là 89,97 ms. Chênh lệch lớn cho thấy kết quả Approval có biến động giữa các lần đo. Không lấy riêng lần đầu hoặc repeat để khẳng định hiệu năng ổn định, và không lấy trung bình hai lần này làm p95 đại diện.
- Cần ít nhất 3–5 lần lặp độc lập cho mỗi mức VU, mỗi lần seed lại cùng dữ liệu, nếu cần một kết luận thống kê vững hơn.

**Không so sánh trực tiếp RPS Workforce và Approval.** Workforce là sustained load 5 phút; Approval là burst concurrency với số iteration cố định và kết thúc sau khoảng 1–2 giây.

## 6. Quan sát CPU/RAM Docker

File Docker Stats được thu thập từ 11:44:24 đến 11:51:21 ngày 12/11/2026, gồm 60 mẫu cho mỗi container. Khoảng cách giữa các mẫu theo thời gian xấp xỉ 7 giây.

| Container | CPU trung bình của các mẫu | CPU cao nhất trong mẫu | RAM thấp nhất–cao nhất |
|---|---:|---:|---:|
| HR Service | 0,33% | 2,73% | 390,7–426,1 MiB |
| Leave Service | 0,44% | 3,01% | 1,097–1,122 GiB |
| SQL Server | 1,44% | 4,93% | 1,196–1,211 GiB |
| Redis | 0,88% | 2,73% | 5,9–8,4 MiB |

Đây là trung bình và cực đại của các **mẫu Docker Stats trong cả cửa sổ thu thập**, không phải phép đo peak CPU chính xác trong thời điểm xử lý approval. Scenario Approval chỉ kéo dài khoảng 1–2 giây, ngắn hơn khoảng lấy mẫu. Vì vậy không dùng bảng này để kết luận service không bị spike CPU trong workload hoặc xác định bottleneck.

## 7. Kết luận và việc còn lại

### Kết luận có thể rút ra

1. Dynamic RBAC và luồng approval Team hoạt động đúng trong các workload đã chạy: mọi request/approval được báo cáo đều thành công, error rate 0%.
2. Approval thực hiện một HR lookup cho mỗi quyết định Team trong test.
3. Workforce cho thấy dấu hiệu giảm khả năng mở rộng từ 50 lên 100 VU: throughput tăng ít trong khi p95 gần gấp đôi.
4. Approval repeat có p95 thấp hơn Workforce rất nhiều, nhưng nó là workload burst rất ngắn; không đại diện cho tải liên tục.
5. Số liệu hiện tại chưa đủ để xác định bottleneck hoặc kết luận năng lực production.

### Việc cần hoàn thiện nếu dùng làm bằng chứng nghiệm thu

- Ghi SHA chính xác trên máy chạy benchmark bằng `git rev-parse HEAD`.
- Lưu/commit các JSON k6 được tạo bởi `REPORT_JSON` trong `docs/testing/evidence/dynamic-authorization/`. Các file JSON thô chưa được xác nhận có trong branch remote khi tài liệu này được cập nhật; tài liệu này hiện ghi lại summary đã được cung cấp.
- Lặp lại Approval tối thiểu 3–5 lần cho mỗi mức VU nếu cần kết luận ổn định.
- Nếu muốn đánh giá Approval sustained load, cần tạo scenario riêng dùng executor theo thời gian (ví dụ `constant-vus` hoặc `ramping-vus`) và xử lý dữ liệu Pending đủ lớn/được seed luân phiên. Không chỉ đổi `DURATION` trong scenario `per-vu-iterations`.
- Thu thập metrics CPU, memory, SQL waits/query duration và response size đồng bộ với khoảng chạy workload.

## 8. Bằng chứng và nguồn số liệu

Summary metrics trong tài liệu được chép từ output k6 do người chạy cung cấp trong phiên kiểm thử ngày 12/11/2026. File Docker Stats được dùng cho mục 6. Các JSON k6 được cấu hình xuất qua `REPORT_JSON` vào thư mục host `docs/testing/evidence/dynamic-authorization/`; cần kiểm tra và đưa các file đó vào Git riêng nếu muốn repository chứa raw evidence.

Không lưu access token, mật khẩu SQL Server hoặc file `.env` vào tài liệu/bằng chứng.


## 9. Bổ sung báo cáo phân trang — 13/11/2026

### 9.1. Các file JSON mới được push

Commit `b26081799fa3860df923cf2ba53647ba79658a4c` bổ sung 9 file k6 JSON cho 3 nhóm, mỗi nhóm 3 lần chạy ở 100 VU. Các số liệu dưới đây là trung bình số học giữa 3 lần chạy; p50/p95 là trung bình percentile từng lần, không phải percentile tính trên dữ liệu gộp.

| Nhóm được đặt tên trong file | RPS trung bình | P50 trung bình | P95 trung bình | HTTP error rate |
|---|---:|---:|---:|---:|
| `nopage` | 58,35 | 1.265,56 ms | 4.363,29 ms | 0% |
| `paged` | 62,41 | 1.250,21 ms | 3.903,13 ms | 0% |
| `paged-new-query` | 150,66 | 285,03 ms | 1.851,75 ms | 0% |

Nhóm `paged-new-query` có throughput cao hơn khoảng 141,4% và p95 thấp hơn khoảng 52,6% so với nhóm `paged`. Đây là tín hiệu hiệu năng tích cực cần giữ lại để đối chiếu.

**Không xem các nhóm trên là benchmark nghiệm thu cho HEAD hiện tại.** Các JSON được push chưa chứa Git SHA, PAGE_SIZE và image ID của lần chạy. Ngoài ra, nhóm `nopage` ghi nhận các request không phân trang vẫn thành công (HTTP error rate 0%), trong khi controller ở source hiện tại yêu cầu page/pageSize và phải trả HTTP 400 nếu thiếu. Điều này cho thấy lần chạy `nopage` không dùng đúng API contract đang có trên branch hiện tại, hoặc container chưa được rebuild từ source tương ứng. Không dùng nhóm này làm baseline so sánh chính thức.

Nhóm `paged` và `paged-new-query` cũng chưa thể gán chắc chắn cho cùng một source revision/cùng page size. Vì vậy, chênh lệch 141,4% không chứng minh riêng thay đổi LINQ/query là nguyên nhân; cần chạy lại từ cùng HEAD và cùng image đã build.

### 9.2. Dữ liệu truyền tải

Ước tính từ `data_received / workforce_requests` trong raw JSON:

| Nhóm | Dữ liệu nhận trung bình / workload request |
|---|---:|
| `nopage` | khoảng 892 KB |
| `paged` | khoảng 277 KB |
| `paged-new-query` | khoảng 278 KB |

Hai nhóm phân trang có payload trung bình gần như tương đương. Điều này gợi ý phần tăng throughput của `paged-new-query` không đến chủ yếu từ việc giảm thêm kích thước response. Tuy nhiên, `data_received` trong raw k6 có cả request setup/warm-up, nên đây chỉ là ước tính trên toàn workload, không phải kích thước response riêng của endpoint.

### 9.3. Điều chỉnh để lần đo kế tiếp có thể tái lập

Script `scripts/load-test/workforce.js` đã được cập nhật:

- PAGE_SIZE luôn hợp lệ, mặc định 50 và chỉ nhận số nguyên từ 1 đến 100.
- Employees, Leave Requests và Pending Approval đều gửi `page/pageSize`.
- Setup kiểm tra các list API (bao gồm Manager Team) phải trả HTTP 400 khi bỏ page/pageSize, rồi mới warm-up bằng request phân trang hợp lệ.
- Raw JSON có `benchmark_metadata`: Git SHA, image tag/ID do người chạy truyền vào, page size, VU, duration, năm dữ liệu và timestamp UTC.

Lần benchmark tiếp theo cần build/recreate hai service từ cùng Git SHA trước khi chạy. So sánh page size 10/50/100 bằng cùng VU, duration và dữ liệu seed; không dùng lần đo không phân trang của binary cũ làm benchmark hiệu năng. Lệnh chạy có trong `dynamic-authorization-validation.md`.
