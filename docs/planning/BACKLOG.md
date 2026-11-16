# Backlog và thứ tự hoàn thiện — cập nhật 16/11/2026

Backlog này ghi các gap còn tồn tại sau khi rà soát code branch feature/dynamic-authorization-v1. Đây là kế hoạch, không phải mô tả tính năng đã triển khai.

## 1. Ưu tiên backend nghiệp vụ

| Ưu tiên | Hạng mục | Trạng thái hiện tại | Tiêu chí hoàn thành |
|---|---|---|---|
| P1 | **Leave Balance initialization & synchronization** | Chưa có bulk ensure/sync | Tạo balance row idempotent cho leave type active và deductsBalance theo employee/năm; race-safe khi nhiều request; policy đổi thì cập nhật Entitled nhưng giữ Used/Pending; nếu Entitled mới < Used + Pending thì không ghi đè gây số dư âm, phải trả conflict hoặc đưa vào reconciliation; không truy cập HR_DB trực tiếp; test new employee, row existing, policy change, giữ Used/Pending, concurrency và HR outage. |
| P1 | **Chuẩn hóa business timezone** | HR policy đã UTC+07; Leave request validation và balance default year đang UTC | Dùng một business clock/timezone nhất quán cho today, default year, policy year và boundary 31/12–01/01; test UTC time sát midnight và local-date conversion. |
| P1 | **Employee Lookup active-state consistency** | Có nguy cơ khác biệt giữa lookup by-ID/batch và Team list | Quy định Manager có được xem inactive direct report không; áp dụng cùng rule ở GetLookup, batch lookup và Team listing; test inactive, reassignment và out-of-team. |
| P1 | **Regression test exact HEAD** | TRX đang lưu trong branch cũ hơn HEAD và có skipped | HR + Leave chạy với SQL Server/Redis dependencies, failed=0, skipped=0; Web Vitest và production build pass; lưu TRX/log mới gắn Git SHA. |
| P2 | **Leave request history read API** | History đang được ghi, chưa có GET endpoint | Thêm GET /api/v1/leave-requests/{id}/history; áp dụng cùng visibility scope của request; sort ổn định theo CreatedAtUtc + Id; không lộ history của request ngoài scope. |
| P2 | **Readiness/liveness và dependency health** | Chỉ map generic /health | Tách liveness khỏi readiness; readiness kiểm tra SQL của chính service; Redis là optional dependency nên Redis down không làm HR unready nếu SQL fallback vẫn hoạt động; ghi rõ HR dependency của Leave và behavior khi outage. |
| P2 | **HR client resilience** | Timeout 5 giây; chưa có circuit breaker/retry policy | Đánh giá retry chỉ cho idempotent lookup; giữ cancellation/deadline; circuit breaker/bulkhead chỉ thêm khi benchmark chứng minh hữu ích; fail closed cho Team authorization; không retry mutation thiếu idempotency. |

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
| P1 | **Gắn revision/image vào k6 metadata** | Workforce paged-v2 JSON ngày 16/11 có SHA/image = not-provided | Chạy lại 50/100 VU từ cùng Git SHA; truyền SHA và immutable image ID; ghi PAGE_SIZE, seed version, CPU/RAM limit và timestamps. |
| P2 | **Profile bottleneck 50/100 VU** | Throughput tăng khoảng 68% khi tăng gấp đôi VU; p95 tăng khoảng 4,1 lần | Thu thập CPU, GC, thread pool, connection pool, SQL waits/query duration đồng bộ với 5m run; xác định bottleneck trước tối ưu. |
| P2 | **Redis ON/OFF cho Workforce** | Catalog ON/OFF có phép đo; Workforce paged-v2 chưa phải A/B Redis | Chỉ chạy nếu cần đo cache impact cho workload này; giữ image/HEAD/dataset/seed/page-size đồng nhất; tách khỏi Catalog benchmark. |
| P2 | **Điều tra 21 HTTP 500 lịch sử** | Workforce cũ ngày 03/11 có 21 HTTP 500 ở 100 VU; hai lượt chạy xác nhận sau đó 0 lỗi; nguyên nhân chưa rõ | Nếu tái hiện, lưu endpoint, response, exception/stack trace, SQL wait và timestamp; không đánh dấu resolved chỉ vì không tái hiện. |
| P2 | **Approval sustained load** | Approval script dùng per-vu-iterations burst, không phải 5 phút sustained | Tạo scenario constant/ramping VU; chuẩn bị đủ pending rows hoặc seed luân phiên; đo duration và HR lookup dưới tải duy trì. |

## 5. Thứ tự thực hiện đề xuất

1. Leave Balance initialization/synchronization.
2. Business timezone thống nhất ở Leave Service.
3. Employee lookup active-state consistency.
4. Regression tests và benchmark exact HEAD có SHA/image ID.
5. History API, readiness và HR client resilience.
6. Permission-driven UI và Role assignment UI.
7. Role/Permission catalog CRUD, Department scope và các phần mở rộng.

Không triển khai outbox, event bus hoặc distributed read model chỉ để xử lý một gap chưa đo. Trước mắt HR HTTP API vẫn là nguồn chính thức; chỉ nâng kiến trúc khi có yêu cầu hoặc bằng chứng benchmark.
