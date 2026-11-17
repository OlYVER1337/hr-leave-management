# Backlog và thứ tự hoàn thiện — cập nhật 17/11/2026

Backlog này ghi các gap còn tồn tại sau khi rà soát code branch feature/dynamic-authorization-v1. Đây là kế hoạch, không phải mô tả tính năng đã triển khai.

## 1. Ưu tiên backend nghiệp vụ

| Ưu tiên | Hạng mục | Trạng thái hiện tại | Tiêu chí hoàn thành |
|---|---|---|---|
| P2 | **HR bulk Leave Balance reconciliation** | Self-sync theo employee/năm đã có qua `POST /api/v1/leave-balances/mine/synchronize`; GET chỉ đọc. `CreateAsync` cũng refresh Entitled từ policy authoritative trong transaction. Chưa có endpoint/worker để reconcile toàn bộ nhân viên sau khi HR đổi policy. | Chỉ triển khai khi nghiệp vụ cần cập nhật chủ động cho mọi nhân viên thay vì lazy refresh khi họ synchronize/tạo đơn. Thiết kế batch idempotent, xử lý quota mới thấp hơn `Used + Pending`, không ghi vào HR_DB và không làm mất `Used`/`Pending`. |
| P1 | **Chuẩn hóa business timezone** | Backend HR policy, Leave request validation, balance default year dùng UTC+07; frontend leaveService dùng `Intl` với `Asia/Ho_Chi_Minh` cho ngày hiện tại và default year. Cần xác nhận thêm các luồng UI khác có đang dựa browser-local time hay không. | Duy trì một business clock/timezone cho today, default year, policy year và boundary 31/12–01/01; test UTC time sát midnight, browser timezone khác VN và local-date conversion. |
| P1 | **Employee Lookup active-state consistency** | Có nguy cơ khác biệt giữa lookup by-ID/batch và Team list | Quy định Manager có được xem inactive direct report không; áp dụng cùng rule ở GetLookup, batch lookup và Team listing; test inactive, reassignment và out-of-team. |
| P1 | **Regression evidence gắn exact revision** | Người dùng báo cáo ngày 17/11: HR 132/132 và Leave 118/118 pass, 0 failed/0 skipped. Output chưa kèm local Git SHA; TRX commit sẵn vẫn cũ và Web chưa có kết quả mới sau balance-sync. | Chạy/ghi nhận HR, Leave và Web trên cùng revision; lưu `git rev-parse HEAD`, TRX/log, Vitest và production build; không gắn kết quả vào SHA khi chưa có bằng chứng. |
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
| P2 | **Render lại sơ đồ PNG/SVG** | Mermaid source đã cập nhật; ảnh hiện tại vẫn là snapshot trước thay đổi | Xuất lại đủ 6 sơ đồ từ Mermaid source, commit đồng thời PNG/SVG; kiểm tra nội dung và liên kết trong architecture/README.md. |

## 5. Thứ tự thực hiện đề xuất

1. Kiểm tra business timezone UTC+07 ở các màn hình còn lại.
2. Thống nhất active-state behavior giữa employee lookup theo ID/batch và Team listing.
3. Lưu regression test evidence cùng Git SHA và chạy Web test/build trên cùng revision.
4. Gắn revision/image ID cho Workforce benchmark và đo tài nguyên đồng bộ.
5. Cân nhắc bulk balance reconciliation, History API, readiness và HR client resilience theo nhu cầu.
6. Permission-driven UI và Role assignment UI.
7. Role/Permission catalog CRUD, Department scope và các phần mở rộng.

Không triển khai outbox, event bus hoặc distributed read model chỉ để xử lý một gap chưa đo. Trước mắt HR HTTP API vẫn là nguồn chính thức; chỉ nâng kiến trúc khi có yêu cầu hoặc bằng chứng benchmark.
