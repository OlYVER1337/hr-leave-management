# Approval benchmark — cấu hình dùng chung và tải theo arrival rate

Tài liệu này mô tả hai workload Approval độc lập:

- `constant-vus`: giữ số VU cố định, throughput phụ thuộc thời gian phản hồi. Dùng để quan sát concurrency và throughput đạt được.
- `constant-arrival-rate`: k6 lên lịch bắt đầu iteration theo tốc độ cố định, độc lập với thời gian phản hồi. Dùng để so sánh cùng tốc độ đầu vào khi thay đổi kích thước VU pool.

Không chạy đồng thời hai benchmark trên cùng stack/dữ liệu.

## 1. Một nguồn cấu hình

Nguồn cấu hình duy nhất là:

`scripts/load-test/approval-benchmark.config.json`

File này chứa các tham số seed, duration và hai workload. Quota fixture 12 ngày là điều kiện domain cố định trong SQL seed, không phải một biến môi trường cần truyền riêng. Script PowerShell `run-approval-benchmark.ps1` đọc file rồi truyền cùng giá trị cho SQLCMD seed, SQLCMD verifier và k6. Các script k6 cũng đọc trực tiếp JSON thông qua `approval-benchmark-common.js`; không còn phải khai báo lại các biến `SEED_*` ở lệnh chạy k6.

Cấu hình mặc định:

| Tham số | Giá trị |
|---|---:|
| Nhân viên load-test | 5.000 |
| Đơn mỗi nhân viên | 20 |
| Manager | 25 |
| Pending percent | 50% |
| Quota fixture mỗi nhân viên (cố định trong seed) | 12 ngày |
| Tổng đơn | 100.000 |
| Pending sau seed | 50.000 |
| Pending tối thiểu mỗi Manager | 2.000 |
| Duration workload | 300 giây |

Khi dùng Pending 50%, seed tạo 10 Pending và 2 Approved trên mỗi nhân viên; `Used + Pending = 12`, không vượt quota 12 ngày. SQL seed kiểm tra quota, ngày nghỉ không trùng nhau và lịch làm việc/ngày lễ trước khi commit.

Không tự chỉnh riêng tham số SQL hoặc k6. Muốn thay đổi seed hoặc workload, sửa JSON duy nhất rồi chạy lại script điều phối.

## 2. Chuẩn bị stack benchmark

PowerShell tại thư mục gốc repository:

```powershell
$compose = @(
  "-p", "hrbench",
  "-f", "docker-compose.yml",
  "-f", "docker-compose.loadtest.yml"
)

docker compose @compose up --build -d
docker compose @compose ps

Invoke-RestMethod http://localhost:18081/health
Invoke-RestMethod http://localhost:18082/health
```

Không chạy trên production. Stack `hrbench` dùng port riêng: HR 18081, Leave 18082, SQL Server 11433 và Redis 16379.

## 3. Chạy constant-vus

Script tự seed, kiểm tra Pending theo từng Manager rồi mới khởi chạy k6:

```powershell
.scriptsload-testun-approval-benchmark.ps1 -Mode RunConstantVus -Vus 50
```

So sánh mức 100 VU:

```powershell
.scriptsload-testun-approval-benchmark.ps1 -Mode RunConstantVus -Vus 100
```

Với `maxApprovalsPerVu=400`, 100 VU cần tối đa 40.000 ID Pending. Do script phân VU round-robin cho 25 Manager, mỗi Manager cần tối đa 1.600 ID. Seed mặc định có tối thiểu 2.000 Pending mỗi Manager.

`constant-vus` giữ số VU cố định và mỗi VU nghỉ theo `thinkTimeSeconds`; đây không phải tốc độ request cố định.

## 4. Chạy constant-arrival-rate

Cấu hình mặc định là 50 iteration bắt đầu mỗi giây trong 300 giây:

```text
Planned iterations = 50 x 300 = 15.000
Pending ID tối đa mỗi Manager = ceil(15.000 / 25) = 600
```

So sánh cùng rate 50 iteration/giây với hai VU pool:

```powershell
.scriptsload-testun-approval-benchmark.ps1 -Mode RunConstantArrivalRate -PreAllocatedVus 50 -MaxVus 50

.scriptsload-testun-approval-benchmark.ps1 -Mode RunConstantArrivalRate -PreAllocatedVus 100 -MaxVus 100
```

Mỗi lệnh sẽ seed lại cùng dataset từ JSON, xác minh đủ Pending cho từng Manager rồi chạy workload. Chạy tuần tự, không chạy song song.

Ở đây 50/100 là kích thước VU pool được pre-allocate và giới hạn tối đa, không khẳng định tất cả VU đều hoạt động đồng thời. Cả hai lượt dùng cùng arrival rate, duration, seed, API workload và cấu hình service; khác biệt là kích thước VU pool. Điều này cho biết pool nhỏ có giữ được target rate không và việc cấp thêm VU có làm giảm `dropped_iterations` hay không.

Executor `constant-arrival-rate` đã tự điều tiết thời điểm bắt đầu iteration, vì vậy script **không gọi `sleep()`** sau request. Tài liệu k6 cũng lưu ý arrival-rate executor bắt đầu iteration theo rate độc lập với thời gian phản hồi; iteration sẽ bị drop nếu không còn VU rảnh để nhận iteration mới.

## 5. Kiểm tra capacity riêng

Có thể seed hoặc verify mà không chạy benchmark:

```powershell
.scriptsload-testun-approval-benchmark.ps1 -Mode Seed

.scriptsload-testun-approval-benchmark.ps1 -Mode VerifyConstantVus -Vus 100

.scriptsscriptsload-testun-approval-benchmark.ps1 -Mode VerifyConstantArrivalRate -PreAllocatedVus 50 -MaxVus 50
```

Verifier nhận `ExpectedPendingPerManager` đã được tính từ chính JSON:
- constant-vus: VU được phân bổ cho từng Manager nhân với số approval ID tối đa mỗi VU.
- constant-arrival-rate: `ceil(ratePerSecond x durationSeconds / managerCount)`.

K6 cũng tự tải đúng số ID duy nhất theo phân bổ đó trước khi scenario bắt đầu. Nếu thiếu Pending hoặc thiếu ID duy nhất, setup dừng; không khởi chạy workload một phần.

## 6. Chỉ số và tiêu chí

Mỗi run lưu raw k6 JSON và summary JSON trong `docs/testing/evidence/dynamic-authorization/`, có timestamp riêng, Git SHA và image ID của hai service nếu container đã chạy.

Các trường cần đối chiếu:

| Trường | Ý nghĩa |
|---|---|
| `target_approval_rate_per_second` | Arrival rate yêu cầu |
| `planned_iterations` | `rate x durationSeconds` |
| `completed_iterations` | Số lần k6 thực thi hàm `default` theo built-in metric `iterations` |
| `out_of_plan_iterations` | Số lần gọi `default` có index vượt phạm vi `rate x duration`; script bỏ qua, không gửi approval và không tái sử dụng ID |
| `approval_http_attempts` | Số request approve thực sự gửi |
| `completed_approvals` | Số approve HTTP 200 |
| `dropped_iterations` | Iteration không được bắt đầu đúng lịch do không có VU rảnh |
| `dropped_iteration_rate` | Dropped / planned |
| `p50_ms`, `p95_ms` | Latency của request approve |
| `approval_errors`, `error_rate` | Lỗi của endpoint approve |
| `leave_to_hr_calls`, `leave_to_hr_avg_ms`, `leave_to_hr_p95_ms` | Outbound HR call được service trả về qua header benchmark |

Workload constant-arrival-rate đặt threshold `dropped_iterations: count==0` và `approval_http_errors: count==0`. Có thể so sánh các lượt cùng rate ngay cả khi một pool không đáp ứng được target: lượt có dropped iteration phải được ghi nhận là không đạt target, không được diễn giải throughput thấp thành rate đã được duy trì.

Không chỉ nhìn vào dropped iteration: cần xem HTTP errors, completed approvals, p95, outbound HR metrics và resource metrics của máy chạy. Không kết luận production capacity từ benchmark local.

## 7. Các tham số đang dùng và điều kiện mở rộng

- Mặc định seed 50.000 Pending, tức 2.000 Pending mỗi Manager.
- Default arrival-rate 50/s x 300s cần 15.000 ID duy nhất (600 mỗi Manager).
- Nếu tăng rate hoặc thời lượng, script điều phối tự tính capacity và từ chối workload nếu seed hiện tại không đủ.
- Nếu tăng constant-vus hoặc `maxApprovalsPerVu`, capacity cũng được tính lại theo số VU thực chạy.
- Muốn tăng capacity, thay đổi `employeeCount` hoặc cấu hình seed hợp lệ trong JSON; giới hạn hiện tại của seed là 5.000 nhân viên.

Tham khảo:
- [k6 constant-arrival-rate executor](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/constant-arrival-rate/)
- [k6 dropped iterations](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/dropped-iterations/)
- [k6 arrival-rate VU allocation](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/arrival-rate-vu-allocation/)

## 8. Kết quả constant-arrival-rate thực tế — 26/11/2026

### 8.1. Điều kiện benchmark

Hai lượt chạy dùng cùng workload và cùng image HR/Leave:

| Thuộc tính | Giá trị |
|---|---|
| Git SHA lượt 50 VU | `8d7bb4772fe279ab60e1c49fcf2749e3a5526cd0` |
| Git SHA lượt 100 VU | `031dc613fb5fec6a4719c3b79f6ed8d23274010e` |
| HR image | `sha256:8eccc09571553866449c312854526453e167ee01f1009491318b22dda8de4d04` |
| Leave image | `sha256:fbb2ed6121920bbabc9923fdd3396c300331ebab098b91403c4c2a35dcb30911` |
| Target rate / duration | 50 iteration/s × 300 giây |
| Planned approvals | 15.000 |
| Seed | 5.000 nhân viên, 100.000 đơn, 50% Pending |
| Manager | 25 |
| Pending sau verifier | 50.000 tổng cộng; 2.000 mỗi Manager |

### 8.2. Kết quả so sánh

| Chỉ số | Pool 50/50 | Pool 100/100 |
|---|---:|---:|
| Approval attempts | 14.974 | 15.000 |
| Approval thành công | 14.974 | 15.000 |
| Throughput | 49,913/s | 50,000/s |
| Dropped iterations | 27 | 0 |
| Dropped rate | 0,18% | 0% |
| Approval errors | 0 | 0 |
| HTTP failed rate | 0% | 0% |
| Approval p50 | 11,97 ms | 12,08 ms |
| Approval p95 | 19,73 ms | 15,84 ms |
| Leave → HR calls | 29.948 | 30.000 |
| Leave → HR average | 2,484 ms | 2,194 ms |
| Leave → HR p95 | 3,34 ms | 3,43 ms |

### 8.3. Diễn giải và giới hạn

- Pool 50/50 không giữ đủ target rate: có 27 dropped iterations (0,18%), vì vậy lượt này **không đạt threshold** `dropped_iterations: count==0`. Không có approval HTTP error.
- Pool 100/100 hoàn tất đúng 15.000 approval với 0 dropped iteration và 0 approval error; đạt target 50 approval/s trong môi trường benchmark này.
- Ở lượt pool 100/100, k6 gọi hàm `default` thêm một lần với `scenario.iterationInTest=15000`. Guard của script bỏ qua lần gọi ngoài phạm vi, không gửi HTTP request và không tái sử dụng Pending ID. Vì vậy `k6_iterations=15001` và `out_of_plan_iterations=1`, trong khi `approval_http_attempts=completed_approvals=15000`. Đây không phải approval thừa.
- Đây là kết quả local với image ID nêu trên; không suy ra production capacity hoặc kết luận bottleneck của hệ thống. Chưa có CPU/RAM/SQL waits được thu đồng bộ để xác định nút thắt.
- Không cần chạy lại pool 100/100 chỉ để xử lý warning ngoài kế hoạch: warning đã được xử lý an toàn và không làm phát sinh request thứ 15.001.

Raw/summary report được tạo trong thư mục evidence ở máy benchmark với các prefix:
- `approval-arrival-rate-50rps-50-vu-pool-20261126-111807`
- `approval-arrival-rate-50rps-100-vu-pool-20261126-113347`

Các file report chỉ được đưa vào Git khi người vận hành add/commit chúng từ máy benchmark; bảng trên ghi lại các số liệu đã được cung cấp từ summary/raw output.
