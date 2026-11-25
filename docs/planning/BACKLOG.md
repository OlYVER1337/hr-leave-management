# Backlog và thứ tự hoàn thiện — cập nhật 25/11/2026

Dynamic Authorization V1 đã được merge vào `master` qua PR #6, merge commit `81b987043ce4663ad11800bb331ffdbc013bd1d2`. Backlog này chỉ ghi các gap còn tồn tại sau khi rà soát trạng thái đã merge; đây là kế hoạch, không phải mô tả tính năng đã triển khai.

## 1. Ưu tiên backend nghiệp vụ

| Ưu tiên | Hạng mục | Trạng thái hiện tại | Tiêu chí hoàn thành |
|---|---|---|---|
| P1 | **Chuẩn hóa business timezone** | Đã bổ sung helper frontend sử dụng `Asia/Ho_Chi_Minh`; Calendar, Dashboard, Request Leave, Employees, Policies, AppShell và Login dùng business date/year. Các phép xử lý date-only chuyển sang UTC để tránh lệch ngày theo timezone của trình duyệt. PR #11 đã có kết quả Web **84/84 tests pass**, TypeScript check và production build thành công trên local Docker; output kiểm thử chưa kèm Git SHA. | Merge PR #11; nghiệm thu thủ công khi browser timezone khác Việt Nam và tại ranh giới ngày/năm, đặc biệt 31/12–01/01. Các màn hình phải dùng thống nhất business date/year thay vì browser-local date/year. |
| P1 | **Employee Lookup active-state consistency** | Đã thống nhất rule: scope Team chỉ đọc direct report đang active theo `ManagerId` hiện tại; scope All vẫn đọc được hồ sơ inactive. `GetLookupAsync` lấy một employee snapshot rồi kiểm tra quyền trên cùng snapshot, tránh TOCTOU khi deactivate/reassign. Đã bổ sung unit tests và SQL integration test đối chiếu lookup, batch lookup và Team listing. HR suite pass **158/158, 0 failed, 0 skipped**, build thành công trên branch revision `e74474294fef8bb35d989c9ec7b94ad12f9fc0dd`. | Merge PR #12; giữ cùng quy tắc active-state và reporting line hiện tại giữa lookup theo ID, batch lookup và Team listing. |
| P1 | **Regression evidence gắn exact revision** | HR đã được xác nhận **158/158 tests pass** trên revision `e74474294fef8bb35d989c9ec7b94ad12f9fc0dd`. Web có kết quả **84/84 tests**, TypeScript check và production build thành công trong quá trình kiểm tra PR #11 nhưng output chưa kèm SHA. Kết quả Leave **123/123** được báo cáo trước đó cũng chưa gắn với cùng revision. Chưa có bộ evidence HR/Leave/Web cùng một commit. | Sau khi merge các PR, chạy HR, Leave và Web trên cùng một master SHA; lưu `git rev-parse HEAD`, TRX/log, Vitest output và production build log. Chỉ gắn kết quả với revision có bằng chứng thực tế. |
| P2 | **HR bulk Leave Balance reconciliation** | GET và POST self-sync theo employee/năm đều lấy policy authoritative từ HR và cập nhật `PolicyDays`; `Entitled = max(PolicyDays, Used + Pending)`. Create/decision/reject/cancel cũng refresh quota trong transaction. Chưa có endpoint/worker để reconcile toàn bộ nhân viên sau khi HR đổi policy. | Chỉ triển khai khi nghiệp vụ cần cập nhật chủ động cho mọi nhân viên thay vì lazy refresh khi họ đọc/tạo/xử lý đơn. Thiết kế batch idempotent, giữ `Used`/`Pending`, không ghi vào HR_DB; quota thấp hơn lịch sử không được làm balance âm. |
| P2 | **Leave request history read API** | History đang được ghi, chưa có GET endpoint. | Thêm GET `/api/v1/leave-requests/{id}/history`; áp dụng cùng visibility scope của request; sort ổn định theo `CreatedAtUtc + Id`; không lộ history của request ngoài scope. |
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
| P1 | **Gắn revision/image vào k6 metadata** | Workforce paged-v2 JSON ngày 16/11 chưa có SHA/image; Approval 100 VU ngày 20/11 đạt 1.000/1.000 nhưng cũng chưa lưu SHA/image và raw JSON trên GitHub | Chạy lại 50/100 VU từ cùng Git SHA; truyền SHA và immutable image ID; ghi PAGE_SIZE, seed version, CPU/RAM limit và timestamps. |
| P2 | **Profile bottleneck 50/100 VU** | Throughput tăng khoảng 68% khi tăng gấp đôi VU; p95 tăng khoảng 4,1 lần | Thu thập CPU, GC, thread pool, connection pool, SQL waits/query duration đồng bộ với 5m run; xác định bottleneck trước tối ưu. |
| P2 | **Redis ON/OFF cho Workforce** | Catalog ON/OFF có phép đo; Workforce paged-v2 chưa phải A/B Redis | Chỉ chạy nếu cần đo cache impact cho workload này; giữ image/HEAD/dataset/seed/page-size đồng nhất; tách khỏi Catalog benchmark. |
| P2 | **Điều tra 21 HTTP 500 lịch sử** | Workforce cũ ngày 03/11 có 21 HTTP 500 ở 100 VU; hai lượt chạy xác nhận sau đó 0 lỗi; nguyên nhân chưa rõ | Nếu tái hiện, lưu endpoint, response, exception/stack trace, SQL wait và timestamp; không đánh dấu resolved chỉ vì không tái hiện. |
| P2 | **Approval sustained load** | Approval 100 VU ngày 20/11 hoàn tất 1.000/1.000, 0 errors, P95 193,42 ms; executor vẫn là per-vu-iterations burst (khoảng 1,4 giây), không phải 5 phút sustained. Mới một lượt và chưa gắn SHA/image | Tạo scenario constant/ramping VU; chuẩn bị đủ pending rows hoặc seed luân phiên; đo duration và HR lookup dưới tải duy trì. |
| — | **Render sơ đồ PNG/SVG** | Đã hoàn tất: GitHub Actions workflow Render architecture diagrams #5 thành công ngày 17/11/2026; các bước render và commit ảnh đều success. | Không còn là backlog; render lại khi Mermaid source tiếp tục thay đổi. |

## 5. Thứ tự thực hiện đề xuất

1. Kiểm tra business timezone UTC+07 ở các màn hình còn lại.
2. Thống nhất active-state behavior giữa employee lookup theo ID/batch và Team listing.
3. Chạy lại regression tests trên merge commit/master hiện tại và lưu evidence cùng Git SHA; chạy Web test/build trên cùng revision.
4. Lưu raw JSON và gắn revision/image ID cho Approval 100 VU ngày 20/11; lặp lại cùng điều kiện. Gắn SHA/image ID cho Workforce benchmark và đo tài nguyên đồng bộ.
5. Cân nhắc bulk balance reconciliation, History API, readiness và HR client resilience theo nhu cầu.
6. Permission-driven UI và Role assignment UI.
7. Role/Permission catalog CRUD, Department scope và các phần mở rộng.

Không triển khai outbox, event bus hoặc distributed read model chỉ để xử lý một gap chưa đo. Trước mắt HR HTTP API vẫn là nguồn chính thức; chỉ nâng kiến trúc khi có yêu cầu hoặc bằng chứng benchmark.
