# Backlog và thứ tự hoàn thiện — cập nhật 26/11/2026

Dynamic Authorization V1 đã được merge vào `master` qua PR #6, merge commit `81b987043ce4663ad11800bb331ffdbc013bd1d2`. Backlog này chỉ ghi các gap còn tồn tại sau khi rà soát trạng thái đã merge; đây là kế hoạch, không phải mô tả tính năng đã triển khai.

## 1. Ưu tiên backend nghiệp vụ

| Ưu tiên | Hạng mục | Trạng thái hiện tại | Tiêu chí hoàn thành |
|---|---|---|---|
| P1 | **Chuẩn hóa business timezone** | Đã merge PR #11 (merge commit `049f4dc3f375fc583a756b599136d17a45631c2d`). Helper frontend dùng `Asia/Ho_Chi_Minh`; Calendar, Dashboard, Request Leave, Employees, Policies, AppShell và Login dùng business date/year; phép xử lý date-only chuyển sang UTC. Web regression trên checkout `master` có **84/84 tests pass**, TypeScript check và production build thành công; `npm audit` báo **0 vulnerabilities**. Regression được ghi nhận trên master revision `2735e9509e3e748b8e4032b9f214b406967c667a`. Đã nghiệm thu thủ công với browser timezone `America/Los_Angeles`: AppShell hiển thị **Thứ 4, 25 thg 11**; người dùng xác nhận Calendar, Request Leave, Employees và Policies đều đạt. | Đã hoàn tất implementation, automated tests và nghiệm thu thủ công các màn hình chính khi browser timezone khác Việt Nam. Kiểm tra boundary 31/12–01/01 vẫn là xác nhận bổ sung nếu cần nghiệm thu sâu hơn. |
| P1 | **Employee Lookup active-state consistency** | Đã merge PR #12 (merge commit `2735e9509e3e748b8e4032b9f214b406967c667a`). Scope Team chỉ đọc direct report đang active theo `ManagerId` hiện tại; scope All vẫn đọc được hồ sơ inactive. `GetLookupAsync` kiểm tra quyền trên cùng employee snapshot. Đã có unit tests và SQL integration test cho inactive/reassignment và đối chiếu batch/Team listing. HR regression trên `master` pass **158/158, 0 failed, 0 skipped**, build thành công tại revision `2735e9509e3e748b8e4032b9f214b406967c667a`. | Đã hoàn tất; duy trì cùng quy tắc active-state và reporting line hiện tại giữa lookup theo ID, batch lookup và Team listing. |
| P1 | **Regression evidence gắn exact revision** | Đã chạy regression trên `master` revision **`2735e9509e3e748b8e4032b9f214b406967c667a`**: HR **158/158 passed**, 0 failed/0 skipped; Leave **123/123 passed**, 0 failed/0 skipped; Web **17/17 test files, 84/84 tests passed**; TypeScript check và Vite production build thành công; `npm audit --audit-level=low` báo **0 vulnerabilities**. Các kết quả được người dùng cung cấp từ cùng checkout; TRX/log chưa được lưu thành artifact riêng. | Đã xác nhận automated regression cho HR, Leave và Web trên cùng revision. Nếu cần bộ hồ sơ nghiệm thu lưu trữ, xuất TRX và lưu console logs/build/audit output cùng SHA này. |
| P2 | **HR bulk Leave Balance reconciliation** | GET và POST self-sync theo employee/năm đều lấy policy authoritative từ HR và cập nhật `PolicyDays`; `Entitled = max(PolicyDays, Used + Pending)`. Create/decision/reject/cancel cũng refresh quota trong transaction. Chưa có endpoint/worker để reconcile toàn bộ nhân viên sau khi HR đổi policy. | Chỉ triển khai khi nghiệp vụ cần cập nhật chủ động cho mọi nhân viên thay vì lazy refresh khi họ đọc/tạo/xử lý đơn. Thiết kế batch idempotent, giữ `Used`/`Pending`, không ghi vào HR_DB; quota thấp hơn lịch sử không được làm balance âm. |
| P2 | **Leave request detail + history API/UI** | **Đã merge PR #15 vào `master`** (merge commit `2bfbafe6fac459ae64a21a05cf8575f2a330fd76`). `GET /api/v1/leave-requests/{id}` trả chi tiết kèm `history`; route `/history` riêng đã được xóa. Frontend tải detail một lần khi mở modal; nút **Xem lịch sử** chỉ đóng/mở timeline và đã nghiệm thu thủ công không phát sinh request bổ sung. | Đã hoàn tất. Backend **129/129 tests passed**, Web **17/17 test files, 89/89 tests passed**, TypeScript check và Vite production build thành công trên checkout sau merge. |
| P2 | **Readiness/liveness và dependency health** | Chỉ map generic `/health`. | Tách liveness khỏi readiness; readiness kiểm tra SQL của chính service; Redis là optional dependency nên Redis down không làm HR unready nếu SQL fallback vẫn hoạt động; ghi rõ HR dependency của Leave và behavior khi outage. |
| P2 | **HR client resilience** | Timeout 5 giây; chưa có circuit breaker/retry policy. | Đánh giá retry chỉ cho idempotent lookup; giữ cancellation/deadline; circuit breaker/bulkhead chỉ thêm khi benchmark chứng minh hữu ích; fail closed cho Team authorization; không retry mutation thiếu idempotency. |

## 2. Dynamic Authorization và frontend

| Ưu tiên | Hạng mục | Trạng thái hiện tại | Tiêu chí hoàn thành |
|---|---|---|---|
| P2 | **Permission-driven React UI** | Backend Dynamic RBAC đã có; UI vẫn dùng Role legacy | Đưa permission/scope claim hoặc effective-permission DTO vào session; guard menu/route/action theo permission; backend vẫn là enforcement cuối; test multi-role union, scope, token refresh và thiếu permission. |
| P2 | **Role assignment UI** | Backend GET/POST/DELETE UserRole APIs có; chưa có UI | HR admin xem/gán/gia hạn/thu hồi Role; hiển thị expiry/audit; giữ backend guard chống self-grant/escalation. |
| P3 | **Role/Permission catalog management** | Chỉ seed baseline; chưa có CRUD catalog | Chỉ triển khai sau khi chốt lifecycle/versioning; tạo/sửa/disable Role, Permission, Scope rules và Role grants có audit, validation scope, bảo vệ last admin. |
| P3 | **Department scope** | Có trong schema nhưng chưa thực thi | Định nghĩa Department dựa trên quan hệ hiện tại hay snapshot; triển khai SQL filter cho từng resource trước khi cấp scope này. |
| P3 | **JWT revocation tức thời** | Access JWT stateless, còn hiệu lực đến expiry; refresh token có thể revoke | Nếu cần revoke ngay, thiết kế token-version/denylist/cache chung giữa HR và Leave; đánh giá latency/availability; test deactivate và revoke Role sau khi JWT đã phát. |

## 3. Data consistency và API contract

| Ưu tiên | Hạng mục | Trạng thái hiện tại | Tiêu chí hoàn thành |
|---|---|---|---|
| P2 | **Annual quota source alignment** | DemoDataSeeder tạo ANNUAL 12; frontend built-in fallback metadata khai báo 15 | Chốt giá trị policy đã duyệt; một nguồn HR policy/SQL là authoritative; bỏ hard-coded quota thừa trong frontend; test Demo/API mode và balance. |
| P2 | **Leave calendar scope consistency** | Leave Service resolve Team bằng current reporting line; cần nghiệm thu UI behavior sau reassignment | Xác nhận wording và bảo đảm calendar/list/inbox cùng business rule; test đổi Manager sau ngày tạo request. |
| P2 | **Pagination/search contract** | Employees, Leave requests và Pending inbox dùng server pagination 1–100 | Giữ page/pageSize required; filter/search/count chạy phía SQL; test boundary, stable ordering, query plan, payload; tránh client filter trên một tập dữ liệu đã bị giới hạn. |
| P2 | **Export và notifications** | UI control disabled; chưa có API/backend delivery | Chỉ bật UI khi có endpoint, authorization, audit và delivery/retry strategy. |

## 4. Benchmark và operational evidence

| Ưu tiên | Hạng mục | Trạng thái hiện tại | Tiêu chí hoàn thành |
|---|---|---|---|
| P1 | **Gắn revision/image vào k6 metadata** | Workforce paged-v2 JSON ngày 16/11 vẫn thiếu SHA/image. Approval `constant-arrival-rate` 50/100 VU ngày 26/11 đã có raw/summary evidence gắn cùng Git SHA `d34ca9100f1db34261b7792dcc2058855bcd29aa` và immutable HR/Leave image IDs. Các benchmark Approval `constant-vus` lịch sử là workload riêng, không gộp với kết quả arrival-rate. | Hoàn thiện metadata cho Workforce paged-v2: chạy từ cùng Git SHA và immutable image IDs; ghi PAGE_SIZE, seed version, CPU/RAM limit và timestamps. Giữ evidence các workload Approval tách biệt theo executor. |
| P2 | **Profile bottleneck Workforce/Approval** | Workforce paged-v2 lịch sử có p95 tăng khi tăng VU. Approval constant-arrival-rate ngày 26/11 đạt 50/s ở cả pool 50/100, nhưng chưa thu CPU/RAM/GC/thread pool/connection pool/SQL waits đồng bộ. Chưa có bằng chứng xác định bottleneck cho cặp arrival-rate. | Chỉ profiling khi cần phân tích sâu: thu CPU, GC, thread pool, connection pool và SQL waits/query duration đồng bộ với cùng workload 5 phút; giữ source/image/seed cố định. Không trì hoãn merge tài liệu vì thiếu profiling. |
| P2 | **Redis ON/OFF cho Workforce** | Catalog ON/OFF có phép đo; Workforce paged-v2 chưa phải A/B Redis | Chỉ chạy nếu cần đo cache impact cho workload này; giữ image/HEAD/dataset/seed/page-size đồng nhất; tách khỏi Catalog benchmark. |
| — | **21 HTTP 500 lịch sử trong Workforce benchmark** | Đã xác định nguyên nhân là Docker bị dừng giữa chừng trong lúc benchmark ngày 03/11; không phải lỗi ứng dụng được tái hiện. Không cần điều tra thêm theo phạm vi hiện tại. | Đã giải thích và đóng hạng mục. Chỉ mở lại nếu HTTP 500 tái xuất hiện trong một lượt benchmark có stack Docker hoạt động ổn định. |
| — | **Approval arrival-rate 50/s trong 5 phút** | **Đã hoàn thành phép đo 300 giây ngày 26/11/2026.** Hai lượt cuối 50/50 và 100/100 VU chạy cùng source SHA `d34ca9100f1db34261b7792dcc2058855bcd29aa`, cùng immutable HR/Leave image IDs và cùng seed `5.000 × 20 = 100.000` đơn, `50%` Pending (50.000 Pending; 2.000/Manager). Cả hai hoàn tất 15.000/15.000 approvals, `dropped_iterations=0`, HTTP errors = 0; pool 100 có 1 out-of-plan iteration bị guard bỏ qua trước HTTP. Lượt 50 VU 11:18:07 bị loại vì 27 dropped; lượt 100 VU 11:33:47 đạt riêng nhưng khác SHA. Bảng đầy đủ và lý do loại ở [benchmark results, Mục 15.2](../testing/dynamic-authorization-benchmark-results.md). Các phép đo `constant-vus` ngày 25/11 là workload riêng, không gộp vào cặp arrival-rate. | Đã hoàn tất mục tiêu đo arrival-rate 300 giây; không cần chạy lại theo phạm vi hiện tại. Chưa thực hiện soak test dài hơn 300 giây hoặc thu CPU/RAM/SQL waits đồng bộ; chỉ bổ sung khi có yêu cầu phân tích riêng. |
| — | **Render sơ đồ PNG/SVG** | Đã hoàn tất: GitHub Actions workflow Render architecture diagrams #5 thành công ngày 17/11/2026; các bước render và commit ảnh đều success. | Không còn là backlog; render lại khi Mermaid source tiếp tục thay đổi. |

## 5. Thứ tự thực hiện đề xuất

1. Lưu TRX/log regression thành artifact riêng nếu cần bộ hồ sơ nghiệm thu có thể tái kiểm chứng; baseline hiện tại đã được ghi nhận trên SHA `2735e9509e3e748b8e4032b9f214b406967c667a`.
2. Approval arrival-rate 50/s × 300 giây đã hoàn tất ngày 26/11; không chạy lại theo phạm vi hiện tại. Chỉ thực hiện thêm soak dài hơn hoặc repeat nhiều lần khi có yêu cầu đánh giá ổn định.
3. Approval arrival-rate đã có raw/summary, commit SHA và immutable image IDs. Phần còn thiếu metadata là Workforce paged-v2: ghi PAGE_SIZE, seed version, exact SHA, image IDs và timestamps; thu resource metrics đồng bộ nếu cần profiling.
4. Kiểm tra thêm business timezone tại boundary 31/12–01/01 khi có thể mô phỏng thời điểm phù hợp.
5. Cân nhắc bulk balance reconciliation, readiness và HR client resilience theo nhu cầu; API chi tiết kèm history đã hoàn tất và merge ở PR #15.
6. Permission-driven UI và Role assignment UI.
7. Role/Permission catalog CRUD, Department scope và các phần mở rộng.

Không triển khai outbox, event bus hoặc distributed read model chỉ để xử lý một gap chưa đo. Trước mắt HR HTTP API vẫn là nguồn chính thức; chỉ nâng kiến trúc khi có yêu cầu hoặc bằng chứng benchmark.
