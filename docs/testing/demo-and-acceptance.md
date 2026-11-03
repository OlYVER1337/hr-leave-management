# Kịch bản demo, kết quả kiểm thử và checklist nghiệm thu

## 1. Kết quả kiểm thử hồi quy và tích hợp

Backend được chạy lại ngày **03/11/2026** sau khi cập nhật Microsoft.Data.SqlClient
6.1.8 và thêm kiểm tra TTL mặc định/thu hồi refresh token khi deactivate. Web test
và production build được chạy lại ngày **03/11/2026** sau khi thêm kiểm thử cho
Calendar và Departments.

| Bộ kiểm thử | Kết quả gần nhất | Tích hợp thực |
|---|---:|---|
| HR Service (`HrService.slnx`) | 53/53 pass, 0 fail, 0 skip (03/11) | SQL Server và Redis |
| Leave Service (`LeaveService.slnx`) | 25/25 pass, 0 fail, 0 skip (03/11) | SQL Server |
| Web (Vitest) | 11/11 file, 31/31 test pass (03/11) | React Testing Library |
| Web production build | Thành công (03/11) | TypeScript check và Vite build |

Lượt backend mới nhất có **78 test pass**, không fail hoặc skip. Hai test suite
tạo database tích hợp với tên ngẫu nhiên rồi xóa database đó khi hoàn tất; HR
integration test dùng Redis database 15. Chỉ chạy các test này trên SQL Server
và Redis dành riêng cho kiểm thử, không dùng dịch vụ production.

### Lệnh kiểm thử backend

PowerShell, với biến kết nối lấy từ môi trường kiểm thử riêng:

```powershell
$env:HR_TEST_SQL_CONNECTION_STRING = "Server=127.0.0.1,11433;Database=master;User Id=sa;Password=<secret>;Encrypt=False;TrustServerCertificate=True"
$env:HR_TEST_REDIS_CONNECTION_STRING = "127.0.0.1:16379,abortConnect=false"

dotnet test src\hr-service\HrService.slnx --no-restore `
  --logger "trx;LogFileName=hr-service.trx" `
  --results-directory docs\testing\evidence\dotnet\hr

dotnet test src\leave-service\LeaveService.slnx --no-restore `
  --logger "trx;LogFileName=leave-service.trx" `
  --results-directory docs\testing\evidence\dotnet\leave
```

Không ghi mật khẩu thật vào lệnh lưu trong tài liệu hoặc commit. Khi chạy thực tế,
nạp secret từ `.env` cục bộ hoặc secret store đã được bảo vệ. Cổng `11433` và
`16379` trong ví dụ là cổng của stack benchmark cô lập; thay bằng cổng test riêng
nếu dùng môi trường khác.

### Lệnh kiểm thử web

Web test và build được chạy trong Node 20 container vì host không có `npm` trong
`PATH`. Dependency được cài theo lockfile trong container tạm; không sửa
`package.json` hoặc `package-lock.json`.

```powershell
$web = (Get-Location).Path + "\src\web"
docker run --rm --volume "${web}:/src:ro" node:20-alpine sh -c `
  "mkdir -p /app && cp /src/package.json /src/package-lock.json /src/index.html /src/vite.config.ts /src/tsconfig.json /app/ && cp -R /src/src /app/src && cd /app && npm ci --no-audit --no-fund && npm test -- --run && npm run build"
```

### Kết quả có thể kiểm tra lại

- [HR Service TRX](./evidence/dotnet/hr/hr-service.trx)
- [Leave Service TRX](./evidence/dotnet/leave/leave-service.trx)
- [K6 JSON, SQL-authoritative 100 VU](./evidence/diagnostics-sql-reads-100vu.json)
- [Container CPU/memory mẫu mỗi khoảng 5–7 giây](./evidence/diagnostics-containers.csv)
- [SQL cached-plan và runtime summary](./evidence/sql-query-plan-summary.txt)
- [Workforce load summary, gồm lỗi phát hiện ở 100 VU](./evidence/workforce-load-summary.json)
- [Redis ON/OFF benchmark ban đầu](../performance/redis-performance.md)

### Cảnh báo trong lúc kiểm thử

- Advisory moderate/high của `System.Security.Cryptography.Pkcs 9.0.11` đã được
  xử lý: package này được kéo vào bởi `Microsoft.Data.SqlClient 6.1.7`; hai
  Infrastructure project hiện pin `Microsoft.Data.SqlClient 6.1.8`, dùng
  `Pkcs 9.0.21` đã vá. Quét `--vulnerable --include-transitive` cho cả hai
  solution ngày 03/11 không còn package dễ tổn thương.
- Vite build thành công nhưng cảnh báo chunk JavaScript sau minify khoảng
  `775.45 kB` (gzip khoảng `221.68 kB`), dynamic import không tách được `apiClient`, và Recharts 2.x đã
  hết active support. Đây chưa phải lỗi chức năng; nên xử lý trong đợt tối ưu UI.

### Kiểm tra luồng demo API thật (03/11/2026)

- Compose project cô lập `hrbench` chạy healthy; Web trả HTTP 200, HR API ở
  `http://localhost:18081`, Leave API ở `http://localhost:18082`.
- Database benchmark có 1.003 nhân viên và 5.000 đơn load-test. Tài khoản HR,
  Manager và Employee lấy từ cấu hình cục bộ đều đăng nhập được; không ghi
  thông tin đăng nhập vào tài liệu.
- Employee gửi đơn Annual một ngày làm việc (**09/11/2026**); Manager nhìn thấy
  đơn trong approval inbox và duyệt thành công. Employee đọc lại được trạng thái
  `Approved`; số dư sau duyệt là `used=1`, `pending=0`, `available=11`.
- Đơn demo được giữ trong volume riêng `hrbench` để làm dữ liệu trình diễn và
  vẫn còn sau khi build lại hai API. Không reset volume và không tác động stack
  `hr-leave` chính.
- Web container của `hrbench` được build lại riêng ngày 03/11; chỉ container
  Web được thay, không restart API, reset volume hay tác động stack `hr-leave`.
  HTTP trả 200 và browser mở được trang đăng nhập; chưa đăng nhập trong browser.
- Đây là kiểm tra API end-to-end và HTTP smoke test; nghiệm thu giao diện có
  xác thực cùng các trạng thái trực quan vẫn cần người trình diễn/nghiệm thu xác nhận.

## 2. Chẩn đoán CPU, SQL và query plan

### Phương pháp và môi trường

- Stack benchmark cô lập: Compose project `hrbench`; không thay đổi hay dừng stack
  `hr-leave` chính.
- Host: Windows/Docker Desktop, **16 logical CPU**, **15.73 GiB RAM**; Docker
  container stats báo giới hạn tổng khả dụng khoảng **7.62 GiB**.
- SQL Server image `2022-latest`, Redis `7-alpine`.
- Dataset sau seed: **1,000 nhân viên load test** và **5,000 đơn load test**
  (HR database có thêm 3 tài khoản demo; Leave database có 5,000 đơn trong bộ tải).
- Kịch bản: k6, **100 VU trong 2 phút**, ba catalog endpoint, query
  `authoritative=true`. Cấu hình service đang tắt cache (`Cache__Enabled=false`),
  đồng thời query authoritative chủ động bỏ cache để chắc chắn truy vấn SQL được
  thực thi.
- CPU và memory lấy từ `docker stats --no-stream`; 18 mẫu trong khoảng
  `17:12:19`–`17:14:18` giờ Việt Nam. SQL runtime lấy từ
  `sys.dm_exec_query_stats` và plan cache sau workload.

Đây là workload chẩn đoán bổ sung, không thay thế bốn lượt benchmark 5 phút
Redis ON/OFF trong [báo cáo hiệu năng Redis](../performance/redis-performance.md).

### Kết quả tải trong lượt chẩn đoán

| VU | Thời lượng | Requests | RPS | p50 | p95 | HTTP error rate |
|---:|---:|---:|---:|---:|---:|---:|
| 100 | 2 phút | 112,341 | 932.56 | 4.650 ms | 12.755 ms | 0% |

### Tài nguyên container khi chạy tải

CPU là phần trăm tương đương số core của container (100% xấp xỉ một logical
core), không phải tỷ lệ CPU toàn máy. Memory là mức quan sát trong cùng đợt tải.

| Container | CPU trung bình | CPU đỉnh | RAM trung bình | RAM đỉnh |
|---|---:|---:|---:|---:|
| HR Service | 119.07% | 154.47% | 162.4 MiB | 176.3 MiB |
| SQL Server | 62.26% | 81.68% | 1,387.3 MiB | 1,392.6 MiB |
| Redis | 1.68% | 7.21% | 6.9 MiB | 11.1 MiB |
| Leave Service | 0.30% | 4.99% | 79.4 MiB | 80.7 MiB |
| Web/Nginx | 0% | 0% | 13.9 MiB | 13.9 MiB |

### SQL runtime và plan quan sát được

Các execution count dưới đây khớp xấp xỉ một lần chạy cho mỗi endpoint trên
phần ba workload và có `last_execution_time` trùng với lượt k6. CPU/elapsed là
trung bình mỗi statement theo DMV; logical reads là số trang đọc trung bình.

| Query catalog | Executions | CPU trung bình | Elapsed trung bình | Logical reads trung bình | Operator chính |
|---|---:|---:|---:|---:|---|
| Loại nghỉ, lọc active và sắp theo tên | 37,216 | 0.25 ms | 0.26 ms | 2 | Clustered Index Scan + Sort |
| Chính sách, join loại nghỉ và lọc năm | 37,449 | 0.19 ms | 0.19 ms | 6 | Clustered Index Scan/Seek + Nested Loops + Sort |
| Ngày lễ theo khoảng ngày | 37,449 | 0.06 ms | 0.06 ms | 0 | Index Seek + Nested Loops trên `IX_Holidays_HolidayDate` |

Các bảng catalog rất nhỏ so với bảng nhân viên/đơn nghỉ; workload này không truy
vấn các bảng 1,000 nhân viên/5,000 đơn. Hai plan có scan/sort là loại nghỉ và
chính sách, nhưng mỗi request chỉ đọc 2–6 trang và tốn dưới 0.3 ms CPU trung
bình; vì vậy **chưa có bằng chứng SQL catalog là bottleneck**. Query ngày lễ dùng
index seek theo ngày.

### Kết luận và hướng tối ưu

Trong workload SQL-authoritative này, HR Service là thành phần tiêu thụ CPU lớn
nhất (khoảng 1.19 core trung bình), SQL Server khoảng 0.62 core; p95 vẫn khoảng
12.8 ms và tỷ lệ lỗi bằng 0. Kết quả gợi ý chi phí xử lý request phía HR API,
bao gồm auth, mapping/JSON serialization và truyền payload, đáng được profile
trước. Đây là xác định **hot component theo container**, chưa phải khẳng định
hàm cụ thể gây CPU; cần `dotnet-counters`/trace ở workload tiếp theo để phân rã.

Chưa thêm index mới: với catalog hiện tại, các plan scan/sort có chi phí thấp và
không giải thích được phần CPU cao hơn ở API. Nếu kích thước catalog tăng, hãy
đo lại plan/row count rồi cân nhắc index phủ sắp xếp theo tên hoặc index theo
năm/chính sách; không tối ưu chỉ dựa vào tên operator. SQL Server giữ mức memory
khoảng 1.39 GiB trong lượt này, nên cũng cần tính memory cache/buffer pool khi
đặt giới hạn container.

## 3. Kịch bản demo

### Chuẩn bị trước demo

1. Tạo `.env` từ `.env.example`; dùng secret riêng cho môi trường demo. Không
   trình chiếu hoặc gửi file `.env`.
2. Bật `INITIAL_HR_ENABLED=true` và seed Manager/Employee demo theo cấu hình
   `DEMO_DATA_ENABLED=true`; chọn `VITE_DEMO_MODE=false` để trình diễn API thật.
3. Khởi động bằng `docker compose up --build -d`, chờ SQL, Redis, HR, Leave
   healthy; kiểm tra `/health` của hai backend.
4. Mở `http://localhost:3000`. Dùng thông tin đăng nhập lấy từ các biến
   `INITIAL_HR_EMAIL`, `INITIAL_HR_PASSWORD`, `DEMO_MANAGER_EMAIL`,
   `DEMO_MANAGER_PASSWORD`, `DEMO_EMPLOYEE_EMAIL` và `DEMO_EMPLOYEE_PASSWORD`.
   Không dùng tài khoản/mật khẩu mẫu ngoài môi trường demo.
5. Chuẩn bị một ngày làm việc tương lai không trùng ngày lễ và đảm bảo Employee
   còn đủ số dư phép để luồng duyệt thành công.

### Kịch bản trình diễn 8–10 phút

1. **HR — cấu hình tổ chức:** đăng nhập HR; mở Phòng ban, tạo/chọn phòng ban;
   mở Nhân viên, kiểm tra nhân viên và gán phòng ban/quản lý trực tiếp.
2. **HR — chính sách:** mở Chính sách nghỉ phép; trình bày loại nghỉ, hạn mức
   theo năm và tab ngày lễ. Giải thích ngày lễ bị loại khi tính số ngày làm việc.
3. **Employee — tạo yêu cầu:** đăng nhập Employee; xem tổng quan và số dư; tạo
   đơn nghỉ hợp lệ với khoảng ngày làm việc và lý do; xác nhận đơn có trạng thái
   Chờ duyệt.
4. **Employee — kiểm tra quy tắc:** thử gửi lại khoảng ngày trùng với đơn đang
   Pending/Approved hoặc yêu cầu vượt số dư; backend phải từ chối và giao diện
   giữ thông báo lỗi. Dùng dữ liệu demo an toàn, không ảnh hưởng dữ liệu thật.
5. **Manager — phê duyệt:** đăng nhập Manager; mở Hộp thư phê duyệt; xác nhận
   chỉ thấy đơn thuộc phạm vi được giao; xem chi tiết trong hộp thoại rồi duyệt
   hoặc từ chối một đơn với ghi chú tùy chọn.
6. **Employee — xác nhận kết quả:** đăng nhập lại Employee; kiểm tra trạng thái
   và số dư. Đơn được duyệt chuyển số ngày từ đang chờ sang đã dùng; đơn bị từ
   chối trả lại phần giữ chỗ.
7. **Lịch và HR:** mở Lịch nghỉ của nhóm, chọn ngày vừa xử lý; đăng nhập HR để
   xem Tất cả đơn nghỉ và dashboard tổng quan.
8. **Giải thích kỹ thuật:** trình bày sơ đồ service/database, transaction và
   concurrency; sau đó trình bày Redis key/TTL/fallback và báo cáo ON/OFF cùng
   kết quả CPU/SQL trong tài liệu này.

Nút xuất báo cáo và chuông thông báo được disable, ghi rõ chưa khả dụng cho tới
khi có API tương ứng. HR không được tạo đơn nghỉ; xác nhận route `/request`
điều hướng về dashboard và CTA không xuất hiện với vai trò HR. Dùng đúng màn hình/API đã được xác nhận trong
[tài liệu UI](../README.md).

## 4. Đối chiếu bảng yêu cầu dự án

| Yêu cầu | Trạng thái hiện tại | Bằng chứng / việc còn lại |
|---|---|---|
| Hai microservice, database tách biệt, giao tiếp REST | Đạt | HR Service sở hữu `HR_DB`; Leave Service sở hữu `Leave_DB`; Leave gọi HR API |
| ASP.NET Core, React/TypeScript, EF Core, migrations, OpenAPI | Đạt | Hai solution .NET 10, Vite/React, EF migrations và OpenAPI ở Development |
| JWT, phân quyền và vòng đời token | Đạt một phần | Có role authorization, refresh rotation/revocation và test; JWT access đã cấp vẫn hợp lệ tới khi hết hạn (mặc định 1 phút) |
| Nghiệp vụ HR | Đạt | CRUD nhân viên/phòng ban, quản lý trực tiếp, danh mục nghỉ/ngày lễ và kiểm tra dữ liệu |
| Nghiệp vụ nghỉ phép | Đạt | Tạo/xem/hủy/duyệt/từ chối, số dư, phân bổ năm, ngày làm việc và history |
| Tính đồng thời | Đạt | SQL integration test cho tạo đơn cạnh tranh, trùng ngày và quyết định cạnh tranh |
| Redis catalog cache | Đạt | Ba nhóm cache, key riêng, TTL mặc định 30 phút, invalidation, SQL fallback và authoritative reads |
| Redis outage | Đạt | Ba catalog endpoint trả HTTP 200 khi Redis dừng; kết quả smoke được ghi ở mục 2 |
| Load test 50/100 VU, 5 phút, Redis ON/OFF | Catalog đạt; workforce 100 VU chạy lại không lỗi, nguyên nhân lịch sử chưa rõ | Workforce: lượt đầu 100 VU có 21 HTTP 500 (0,31%); hai lượt chạy lại có 0 lỗi. Chưa chạy Redis OFF cho workload này; không có code fix được xác nhận |
| Docker Compose, healthcheck, README | Đạt | Compose, Dockerfile, `.env.example`, volume và hướng dẫn chạy đã có |
| Design System và giao diện | Đã được người dùng nghiệm thu | Người dùng xác nhận UI đạt ngày 03/11/2026; Design System, wireframe và UI test được lưu trong repository. Chưa thực hiện audit accessibility độc lập |
| Bàn giao tài liệu | Đạt về tài liệu kỹ thuật | Có sơ đồ, ERD, sequence, Redis/performance, TRX, README và [hướng dẫn sử dụng/test/demo](../USER-GUIDE.md); còn kết quả workforce benchmark |

### Điểm cần lưu ý

- Deactivate nhân viên sẽ đánh dấu tài khoản không hoạt động và thu hồi toàn bộ
  refresh token. Vì access JWT được xác thực stateless ở cả hai service, token
  đã phát hành không bị thu hồi tức thời; nó còn hiệu lực đến `exp`. Cấu hình
  mặc định hiện là 1 phút; benchmark riêng có thể tăng thời hạn token để chạy
  đủ lượt. Muốn thu hồi access token ngay cần một thiết kế revocation chung và
  đánh đổi đặc tính không gọi HR ở mỗi request.
- TTL trong bảng yêu cầu người dùng gửi là 30 phút. Cấu hình mặc định trước đây
  là 5 phút; đã đổi default thành 30 phút và bổ sung test. Benchmark catalog cũ
  vẫn hợp lệ cho workload warm-cache, nhưng cần rebuild service để áp dụng
  default mới trong runtime.
- `workforce.js` chỉ đọc dữ liệu. Trên 1.000 nhân viên/5.000 đơn, lượt 50 VU
  trong 5 phút có 0 lỗi. Lượt 100 VU đầu tiên có 21 HTTP 500 (0,31%), p95
  11,05 giây; hai lượt chạy lại 100 VU/5 phút có 0 lỗi (p95 lần lượt 17,23 và
  13,47 giây). Lỗi lịch sử chưa tái hiện, nguyên nhân chưa xác định và không có
  thay đổi code nào được xác nhận là bản sửa. Redis OFF cho workforce chưa được
  đo. Chi tiết ở
  [workforce load summary](./evidence/workforce-load-summary.json).

## 5. Checklist nghiệm thu

`[x]` nghĩa là đã có bằng chứng test, kiểm tra trực tiếp hoặc người dùng xác
nhận nghiệm thu trong lượt kỹ thuật.
Các đường dẫn ở cột bằng chứng trỏ tới source, test hoặc tài liệu để đối chiếu.

| Xong | Hạng mục | Kỳ vọng khi nghiệm thu | Bằng chứng/tham chiếu |
|---|---|---|---|
| [x] | Dựng môi trường | Compose chạy Web, HR, Leave, SQL Server, Redis; healthcheck đạt | [README chạy hệ thống](../README.md), kiểm tra `hrbench` 03/11 |
| [x] | Tài khoản và phân quyền theo role | API/UI chặn đúng vai trò/phạm vi; refresh token bị thu hồi khi deactivate | HR API tests, `App.tsx`, `EmployeeServiceTests`; access JWT còn hiệu lực đến expiry |
| [x] | Quản trị nhân sự | HR tạo/sửa nhân viên, phòng ban và quản lý trực tiếp | `/employees`, `/departments`; HR tests |
| [x] | Chính sách nghỉ | HR quản lý loại chuẩn, hạn mức năm và ngày lễ | `/policies`; [Redis report](../performance/redis-performance.md) |
| [x] | Số dư cá nhân | Employee thấy hạn mức, đã dùng, đang chờ và khả dụng | Leave tests; đọc lại số dư sau demo API |
| [x] | Tạo đơn hợp lệ | Ngày kết thúc không trước ngày bắt đầu; chỉ tính ngày làm việc; tạo trạng thái Pending | `RequestLeavePage`; Leave tests; demo API |
| [x] | Đơn không hợp lệ | Đơn trùng ngày, ngày không hợp lệ, số dư thiếu và tổng ngày bằng 0 bị từ chối | Leave service tests |
| [x] | Hủy đơn | Employee chỉ hủy đơn Pending của mình; số dư và lịch sử cập nhật cùng giao dịch | Leave persistence integration tests |
| [x] | Phê duyệt/từ chối | Chỉ người duyệt hợp lệ xử lý; đơn chỉ chuyển trạng thái một lần; có history | Approvals page; Leave persistence tests; demo API approve |
| [x] | Concurrency | Hai yêu cầu vượt số dư hoặc hai quyết định cạnh tranh không làm sai số dư/trạng thái | [Leave TRX](./evidence/dotnet/leave/leave-service.trx) |
| [x] | Lịch và thống kê — automated | Đơn Pending/Approved và ngày lễ được hiển thị trên lịch/dashboard | `CalendarPage.test.tsx`, `DashboardPage.test.tsx` |
| [x] | Lịch và thống kê — visual sign-off | Người dùng xác nhận giao diện đạt nghiệm thu UI | Người dùng xác nhận ngày 03/11/2026; accessibility audit độc lập chưa thực hiện |
| [x] | Redis cache | Tối thiểu 2 nhóm dữ liệu có key/TTL/invalidation; cache miss đọc SQL | [Redis report](../performance/redis-performance.md) |
| [x] | Redis outage | Catalog vẫn đọc được từ SQL; nghiệp vụ không dựa cache cũ | [Redis report](../performance/redis-performance.md) |
| [x] | HR outage | Tác vụ cần HR trả lỗi dependency phù hợp và không lưu đơn dở dang | `HrServiceOutageTests`; [Redis report](../performance/redis-performance.md) |
| [x] | Regression backend | HR 53/53 và Leave 25/25 pass; không skip khi có test dependencies | Lượt chạy HR mới nhất; TRX lưu sẵn trong `evidence/dotnet/` |
| [x] | Regression frontend | 31 UI tests pass và production build thành công | Vitest/build lượt mới nhất, báo cáo mục 1 |
| [x] | Load test catalog Redis ON/OFF | 50 và 100 VU, 5 phút mỗi lượt; báo cáo RPS/p50/p95/error và cấu hình máy | [Redis performance report](../performance/redis-performance.md) |
| [x] | Load test workforce — Redis ON | 50/100 VU, 5 phút, dataset 1.000 nhân viên/5.000 đơn | 50 VU 0 lỗi; lượt đầu 100 VU 21 HTTP 500; hai lần chạy lại 100 VU 0 lỗi; [summary](./evidence/workforce-load-summary.json) |
| [ ] | Truy nguyên lỗi workforce lịch sử | Xác định exception/endpoint gây 500 và kiểm chứng bản sửa; nếu lỗi tái diễn, giữ log đủ chi tiết để tái hiện | Hai lượt chạy lại không tái hiện; nguyên nhân chưa rõ. Redis OFF cho workforce cũng chưa đo |
| [x] | Bottleneck | Đọc được CPU/container, SQL runtime và plan; không thêm index nếu chưa có bằng chứng | `evidence/diagnostics-*`, `evidence/sql-query-plan-summary.txt` |
| [x] | Tài liệu thiết kế và bàn giao | ERD, kiến trúc, API/OpenAPI, Design System, wireframe, README, hướng dẫn sử dụng | [Docs index](../README.md), [architecture diagrams](../architecture/README.md), [user guide](../USER-GUIDE.md) |
| [x] | Chạy demo an toàn | API mode thật; dùng dataset demo; không reset volume hoặc dùng secret production | Luồng API `hrbench` 03/11; checklist chuẩn bị demo phía trên |

## 6. Bảo toàn dữ liệu khi chạy lại

- Chỉ chạy seed/load test trên Compose project cô lập `hrbench` hoặc stack được
  tạo riêng cho kiểm thử.
- `seed-scale-data.sql` xóa và tạo lại các đơn có marker `LOADTEST:` cho nhóm
  `loadtest.user.*@example.invalid`; không chạy trên database nghiệp vụ.
- Không chạy `docker compose down -v` trên stack chính để dọn dữ liệu demo.
- Không đưa `.env`, mật khẩu, JWT secret, access token hoặc connection string
  chứa bí mật vào báo cáo, log hoặc Git.
