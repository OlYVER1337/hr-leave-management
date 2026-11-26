# Checklist bàn giao — cập nhật 20/11/2026

## 1. Revision và trạng thái repository

| Thuộc tính | Giá trị |
|---|---|
| Repository | `OlYVER1337/hr-leave-management` |
| Branch tích hợp | `master` |
| Merge Pull Request | [#6 — Merge dynamic authorization v1 vào master](https://github.com/OlYVER1337/hr-leave-management/pull/6) |
| Merge commit | `81b987043ce4663ad11800bb331ffdbc013bd1d2` |
| Feature branch | `feature/dynamic-authorization-v1` |
| Ngày cập nhật | 20/11/2026 |
| Trạng thái triển khai | Đã merge vào master; chưa có bằng chứng deploy production |

PR #6 được merge bằng merge commit, giữ lịch sử 558 commits của feature branch. Merge commit đã được xác minh là HEAD của `master` ngay sau khi merge.

## 2. Trạng thái từng hạng mục

| # | Hạng mục | Trạng thái | Bằng chứng / giới hạn |
|---:|---|---|---|
| 1 | Source code và Git | Đã merge | PR #6, merge commit `81b98704`; không đồng nghĩa đã deploy production. |
| 2 | Web UI và workflow | Đã có | Login, Dashboard, Leave Requests, Request Leave, Approvals, Calendar, Manager Team, HR Employees/Departments/Policies và Profile. |
| 3 | Dynamic Authorization V1 | Đã triển khai backend | HR quản lý Roles, Permissions, UserRoles, Scope rules và audit. JWT có `authz_v=1` và `perm`; HR/Leave API authorize theo permission/scope. |
| 4 | Frontend authorization | Còn giới hạn | React vẫn dùng Role legacy HR/Manager/Employee cho menu/route guard; chưa có permission-driven UI hoặc màn hình quản trị Role/Permission catalog. Backend vẫn là lớp enforcement cuối. |
| 5 | Service boundary | Đã triển khai | HR sở hữu HR_DB; Leave sở hữu Leave_DB; Leave chỉ gọi HR qua HTTP; Nginx route API, không có API Gateway riêng. |
| 6 | Database và migrations | Đã có | HR migrations gồm Dynamic Authorization và Leave Policy versions. Leave migrations gồm balance constraints và policy snapshots. EF Core migrations/model là nguồn quyết định schema. |
| 7 | API reference | Đã cập nhật theo API hiện có | Có permission/scope, pagination, current Team lookup và UserRole assignment. API chi tiết đơn nghỉ trả kèm History; không có endpoint history riêng. Export và notifications chưa có. |
| 8 | Redis | Đã có | Cache-aside best-effort cho HR catalog; SQL là nguồn dữ liệu chính. Redis lỗi phải fallback SQL, không phải nguồn dữ liệu nghiệp vụ. |
| 9 | Backend tests | Có kết quả local đã báo cáo | HR 155/155 pass; Leave 123/123 pass; 0 failed/0 skipped ở lượt chạy được báo cáo. SHA của checkout lúc chạy chưa được ghi lại. |
| 10 | Web tests/build | Có kết quả local đã báo cáo | 79 tests pass; TypeScript/production build thành công trong lượt chạy đã ghi nhận. Chưa có SHA chính xác gắn với kết quả. |
| 11 | Seed dữ liệu benchmark | Đã xác minh ngày 20/11 | Seed 1.000 nhân viên, 5.000 đơn; 1.250 đơn mỗi trạng thái Pending/Approved/Rejected/Cancelled; năm dữ liệu 2026. `sqlcmd -b` trả exit code 0. |
| 12 | Approval benchmark | Một lượt mới 100 VU thành công | 1.000/1.000 approval completed, 0 approval errors, 0% error rate; P50 61,64 ms, P95 193,42 ms. Đây là burst ngắn, không phải sustained load. |
| 13 | GitHub Actions | Không triển khai CI/CD theo quyết định dự án | Workflow Render architecture diagrams #5 thành công ngày 17/11; không có workflow tự động chạy backend/Web tests. CI/CD không phải điều kiện bàn giao theo yêu cầu hiện tại. |
| 14 | Production readiness | Chưa nghiệm thu | Chưa có bằng chứng TLS, secrets manager, backup/restore, production monitoring, clean-environment deployment và rollback rehearsal. |

**Cách diễn giải test:** số HR/Leave/Web ở trên là kết quả chạy local đã được báo cáo trong quá trình kiểm thử. Vì output không ghi `git rev-parse HEAD`, không khẳng định chúng được chạy chính xác trên merge commit `81b98704`. Không có CI/CD theo lựa chọn của dự án; việc này không phủ nhận các lượt test thủ công.

## 3. Bằng chứng seed và Approval benchmark ngày 20/11/2026

### Seed scale data

| Hạng mục | Kết quả |
|---|---:|
| Employees | 1.000 |
| Leave requests | 5.000 |
| Pending | 1.250 |
| Approved | 1.250 |
| Rejected | 1.250 |
| Cancelled | 1.250 |
| Leave year | 2026 |
| SQL command | Thành công, exit code 0 |

Seed script đã dùng cột SQL `PolicyDays` thay cho thuộc tính C# `Entitled` vốn được đánh dấu `[NotMapped]`.

### Approval 100 VU

| Chỉ số | Kết quả |
|---|---:|
| VU | 100 |
| Iterations/VU | 10 |
| Planned approvals | 1.000 |
| Approval attempts | 1.000 |
| Completed approvals | 1.000 |
| Approval errors | 0 |
| Error rate | 0% |
| P50 | 61,64 ms |
| P95 | 193,42 ms |
| Leave → HR calls | 2.000 |
| Leave → HR total time | 14.987 ms |
| Leave → HR average | 7,49 ms |
| Leave → HR P95 | 17,03 ms |

Scenario hoàn tất khoảng 1,4 giây; tổng thời gian lệnh k6 khoảng 3,7 giây. `DURATION=5m` trong script là `maxDuration`, không phải thời gian duy trì tải. SHA/image ID của checkout/container tại thời điểm chạy chưa được lưu kèm output. Raw report có tên yêu cầu `approval-100vu-after-seed.json` chưa được xác minh là đã commit lên GitHub; cần kiểm tra file local và push nếu file tồn tại.

## 4. Những điểm còn cần xác minh trước sign-off

1. Chạy lại HR, Leave, Web tests trên merge commit hiện tại nếu cần bằng chứng chính xác theo revision; lưu SHA và TRX/log tương ứng.
2. Kiểm tra raw report `approval-100vu-after-seed.json` tại `docs/testing/evidence/dynamic-authorization/`; commit nếu có. Không tạo metadata SHA/image bằng suy đoán.
3. Lặp lại Approval 100 VU để đánh giá biến động P95; lượt mới hiện có P95 193,42 ms và mới chỉ một lần.
4. Nếu cần đánh giá tải duy trì, tạo scenario Approval sustained-load riêng. Script hiện tại là `per-vu-iterations` burst.
5. Rà soát business timezone UTC+07 ở các màn hình còn lại và active-state behavior của employee lookup.
6. Rà soát clean clone, secrets, TLS, backup/restore, logging/monitoring và migration/rollback trước production.

## 5. Rủi ro và giới hạn đã biết

- Dynamic RBAC backend chưa đi cùng permission-driven React UI.
- Role/Permission catalog CRUD chưa có; UserRole assignment API đã có nhưng chưa có UI tương ứng.
- Department scope có trong schema/rule catalog nhưng chưa được thực thi thành data filter.
- Leave Balance lưu `PolicyDays`, `Used`, `Pending`; `Entitled = max(PolicyDays, Used + Pending)`. Self GET/POST sync và các luồng create/decision refresh policy hiện hành; chưa có bulk reconciliation cho toàn bộ nhân viên.
- Request giữ `PolicyVersionId` và `PolicyDaysAtCreation` theo từng yearly allocation. Policy version được chọn theo ngày tạo đơn của business timezone UTC+07.
- `GET /api/v1/leave-requests/{id}` trả chi tiết đơn cùng danh sách `history`; quyền Self/Team/All được kiểm tra trên đơn trước khi trả lịch sử. Không có endpoint `/history` riêng.
- `/health` chưa tách readiness/liveness có dependency status; HR client chưa có circuit breaker.
- Access JWT đã phát không bị thu hồi tức thời khi UserRole bị revoke hoặc nhân viên deactivate; quyền mới có hiệu lực khi token mới được phát hành/refresh.
- Approval benchmark ngày 20/11 là burst ngắn; không đại diện cho tải liên tục 5 phút hoặc production capacity.

## 6. Tài liệu liên quan

- [Docs index](README.md)
- [System design](architecture/SYSTEM_DESIGN.md)
- [Database design](architecture/DATABASE_DESIGN.md)
- [Dynamic Authorization design](architecture/AUTHORIZATION_DESIGN.md)
- [API reference](API_REFERENCE.md)
- [Backlog](planning/BACKLOG.md)
- [Validation và lệnh benchmark](testing/dynamic-authorization-validation.md)
- [Benchmark results](testing/dynamic-authorization-benchmark-results.md)
