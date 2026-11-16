# Checklist bàn giao — cập nhật 16/11/2026

## 1. Revision và phạm vi

| Thuộc tính | Giá trị |
|---|---|
| Repository | OlYVER1337/hr-leave-management |
| Branch được rà soát | feature/dynamic-authorization-v1 |
| Baseline code đã đối chiếu | 2bac8327b0ec64b5d1a1cb5d4de81e00e2bd6c4c |
| HEAD sau các cập nhật tài liệu | aa7ef027ad6371fab6690b4d1fa8f6ec31fdcc9c |
| Ngày rà soát | 16/11/2026 |
| Phạm vi | Đối chiếu tài liệu với source, migrations, scripts và evidence trong branch |

Đây là trạng thái feature branch. Không đồng nghĩa branch đã merge/release lên master hoặc production.

## 2. Trạng thái từng hạng mục

| # | Hạng mục | Trạng thái hiện tại | Bằng chứng / việc còn lại |
|---:|---|---|---|
| 1 | Source code và Git | Có trên feature branch; chưa xác nhận release | HEAD và branch được kiểm tra. Cần review/merge theo quy trình nhóm; chưa có bằng chứng release production. |
| 2 | Web UI và workflow | Đã có; cần nghiệm thu lại trên HEAD hiện tại | Có Login, Dashboard, Leave Requests, Request Leave, Approvals, Calendar, Manager Team, HR Employees/Departments/Policies và Profile. Người dùng đã xác nhận UI đạt ngày 03/11/2026; đó là sign-off thủ công tại thời điểm đó, không tự động chứng minh mọi UI behavior của HEAD mới. |
| 3 | Dynamic Authorization V1 | Đã triển khai backend | HR migration, Roles/Permissions/UserRoles/Scope rules/Audit, JWT claims và permission/scope policies ở HR/Leave. React vẫn dùng role legacy cho menu/route; không có Role assignment UI hoặc Role/Permission catalog CRUD. |
| 4 | Kiến trúc và ownership | Được tài liệu hóa theo source | HR owns HR_DB; Leave owns Leave_DB; Leave chỉ gọi HR qua HTTP; không có API Gateway riêng. |
| 5 | Database design | Được tài liệu hóa theo migrations hiện tại | HR migrations gồm Initial schema, RefreshTokens, Dynamic Authorization; Leave migrations gồm Initial schema và approval/year allocations. EF Model/Migrations là nguồn quyết định schema thật. |
| 6 | API reference | Đã cập nhật theo controller hiện có | API permission/scope, pagination, current Team lookup, role assignment và giới hạn chưa có History/Export/Notification được ghi tại API_REFERENCE.md. |
| 7 | Redis | Đã có và có evidence kiểm tra lịch sử | Redis cache-aside cho leave types/policies/holidays, TTL mặc định 30 phút, invalidation và SQL fallback. Báo cáo ON/OFF ở redis-performance.md là workload Catalog; không coi là Redis comparison cho Workforce paged-v2. |
| 8 | Workforce benchmark paged-v2 | 3 lượt mỗi mức; 0 HTTP errors | 50 VU trung bình khoảng 453.56 RPS, p95 trung bình 17.85 ms; 100 VU trung bình khoảng 763.29 RPS, p95 trung bình 73.28 ms. Page size 50, duration 5m. Git SHA/image ID trong JSON là not-provided; chưa phải baseline gắn revision bất biến. |
| 9 | Approval benchmark | Có evidence burst cũ | approval.js dùng per-vu-iterations, không phải sustained load 5 phút; xem riêng kết quả lịch sử ở benchmark report. |
| 10 | Automated tests trên HEAD | Chưa xác nhận bằng evidence trong branch | TRX được lưu hiện tại có timestamp 05/11: HR run có 57 pass, 1 skipped; Leave 19 pass, 6 skipped. Không dùng chúng làm kết quả HEAD. Cần chạy lại full suite với SQL/Redis dependencies và skipped=0. |
| 11 | Deployment package | Có local/demo Compose; chưa nghiệm thu production | Dockerfiles, Nginx, Compose và .env.example có sẵn. Chưa có bằng chứng clean-environment deployment, TLS, secrets manager, backup/restore hoặc production monitoring. |

## 3. Bằng chứng benchmark hiện tại

Các file workforce-{50,100}vu-paged-v2-run{1,2,3}.json trong docs/testing/evidence/dynamic-authorization/ có metadata:
- duration = 5m
- page_size = 50
- workload = employee-and-leave-reads
- generated_at_utc
- git_sha = not-provided
- image_tag = not-provided

Các lần chạy đều kéo dài xấp xỉ 300 giây và có error rate 0. Mean RPS 50 VU khoảng 453.56; mean RPS 100 VU khoảng 763.29. P95 trung bình tăng từ khoảng 17.85 ms lên 73.28 ms. Không thể gắn chắc chắn các số liệu vào một source/image cụ thể khi metadata thiếu SHA/image ID. Xem dynamic-authorization-benchmark-results.md.

Một lỗi lịch sử ở workload Workforce cũ ghi nhận 21 HTTP 500 tại 100 VU ngày 03/11; hai lượt chạy xác nhận sau đó không tái hiện. Nguyên nhân lỗi ban đầu chưa được xác định. Không dùng sự kiện này để khẳng định code hiện tại còn lỗi hoặc đã được fix.

## 4. Việc cần làm trước sign-off

1. Chạy lại HR/Leave automated tests trên exact HEAD với SQL Server/Redis dành riêng cho test; lưu TRX mới, failed=0, skipped=0.
2. Chạy frontend tests và production build trên exact HEAD; lưu log hoặc CI run link.
3. Lặp lại Workforce 50/100 VU từ cùng HEAD và image; truyền GIT_SHA + immutable IMAGE_TAG/ID vào k6 metadata.
4. Thu thập CPU/Memory/SQL metrics đồng bộ trong workload nếu cần kết luận bottleneck; không suy luận từ snapshot lấy mẫu lệch thời gian.
5. Hoàn thiện P1 backlog: Balance initialization/sync, timezone, employee lookup active-state consistency.
6. Rà soát clean clone, secrets, TLS, backup/restore và migration/rollback trước deployment.
7. Review feature branch và xác nhận merge/release qua quy trình nhóm.

## 5. Rủi ro và giới hạn phải công khai

- Dynamic RBAC backend chưa đi cùng permission-driven React UI.
- Role/Permission catalog CRUD chưa có; chỉ có seed baseline và UserRole assignment API.
- Leave Balance chưa có bulk initialization hoặc sync khi policy đổi; GET balance chỉ đọc rows hiện hữu.
- Leave request date validation và default balance year dùng UTC, trong khi HR policy year dùng UTC+07.
- Inactive direct report có thể cần chuẩn hóa giữa lookup by-ID/batch và Team listing.
- Không có Leave History GET endpoint; History chỉ được ghi.
- /health chưa phải readiness check có dependency status; HR client chưa có circuit breaker.
- Access JWT đã phát không bị thu hồi tức thời khi user bị deactivate hoặc grant/revoke.
- Stored TRX có skipped integration tests và cũ hơn code HEAD; cần test mới.

## 6. Tài liệu liên quan

- Docs index: README.md
- System design: architecture/SYSTEM_DESIGN.md
- Database design: architecture/DATABASE_DESIGN.md
- Dynamic Authorization design: architecture/AUTHORIZATION_DESIGN.md
- API reference: API_REFERENCE.md
- Backlog: planning/BACKLOG.md
- Validation: testing/dynamic-authorization-validation.md
