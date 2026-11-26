# Tài liệu dự án HR Leave Management

Mục lục này tách **yêu cầu ban đầu**, **mô tả hệ thống đã triển khai**, **bằng chứng kiểm thử** và **kế hoạch tiếp theo**. Khi các tài liệu khác nhau, mã nguồn trên `master` sau merge PR #6 và EF Core migrations là nguồn đối chiếu cuối cùng.

## 1. Bắt đầu và phạm vi

- [README dự án](../README.md): cấu hình môi trường, Docker Compose, seed tài khoản và lệnh test.
- [Mô tả hệ thống gốc](requirements/system-description.docx): yêu cầu/đề xuất ban đầu, không phải mô tả bảo đảm của code hiện tại.
- [Đề tài thực tập 2026](requirements/internship-project-2026.docx): tiêu chí bàn giao gốc.
- [Backlog](planning/BACKLOG.md): gap đã xác nhận và thứ tự ưu tiên tiếp theo.

## 2. Kiến trúc, bảo mật và API

- [System design](architecture/SYSTEM_DESIGN.md): service boundary, data ownership, request flow, consistency, failure mode và giới hạn vận hành.
- [Database design](architecture/DATABASE_DESIGN.md): HR_DB/Leave_DB schema, Dynamic RBAC, indexes, constraints, migrations và seed data.
- [Dynamic Authorization Design](architecture/AUTHORIZATION_DESIGN.md): permissions, scopes, JWT, UserRole assignment, escalation protection và Team rules.
- [API reference](API_REFERENCE.md): route, permission/scope, pagination, payload và lỗi HTTP.
- [Sơ đồ kiến trúc và database](architecture/README.md): Mermaid source và các bản xuất hình.

## 3. Giao diện

- [Design System](design/DESIGN_SYSTEM.md): visual token, typography, layout, component, responsive và accessibility.
- [Wireframes](design/WIREFRAMES.md): màn hình, route guard, API trace và giới hạn frontend hiện tại.
- [Hướng dẫn sử dụng, test và demo](USER-GUIDE.md): thao tác theo HR/Manager/Employee ở Demo mode và API mode.

## 4. Kiểm thử và hiệu năng

- [Kịch bản demo, kết quả test và acceptance](testing/demo-and-acceptance.md): bằng chứng test, chẩn đoán, demo và các hạng mục cần nghiệm thu lại.
- [Xác minh Dynamic RBAC và chạy benchmark](testing/dynamic-authorization-validation.md): stack hrbench, SQL/Redis integration tests, seed dữ liệu và k6 commands.
- [Approval benchmark guide](testing/approval-benchmark-guide.md): cấu hình dùng chung, seed/verifier, chạy constant-vus và constant-arrival-rate, cách đọc raw/summary evidence.
- [Kết quả benchmark Dynamic RBAC/HR lookup](testing/dynamic-authorization-benchmark-results.md): Workforce paged-v2 và các phép đo Approval lịch sử/arrival-rate, tách riêng theo workload.
- [Báo cáo Redis](performance/redis-performance.md): cache-aside, TTL, invalidation, fallback và benchmark Catalog Redis ON/OFF.
- [Bằng chứng thô](testing/evidence/): TRX, k6 JSON, log build và số liệu quan sát.

## 5. Hướng dẫn chạy và bàn giao hệ thống

- [README dự án](../README.md): cấu hình môi trường, Docker Compose, tài khoản khởi tạo và lệnh test.
- [Tài liệu bàn giao và checklist sign-off](HANDOFF.md): hướng dẫn setup, tài khoản/vai trò, Swagger, test, benchmark, đối chiếu 10 đầu ra đề tài và kịch bản nghiệm thu.

## Quy ước cập nhật

- Tài liệu trong requirements/ là baseline yêu cầu gốc; không sửa để biến yêu cầu ban đầu thành implementation đã có.
- Tài liệu kiến trúc/API/DB mô tả trạng thái as-built; khi code thay đổi phải cập nhật đồng thời.
- Backlog chỉ liệt kê việc chưa triển khai hoặc cần xác minh lại. Không biến đề xuất thành tính năng đã hoàn thành.
- Test phải ghi ngày chạy, revision và số skipped. TRX cũ không được xem là kết quả của HEAD mới.
- Benchmark phải ghi workload, VU, duration, page size, data set, Git SHA và image ID. Không lấy trung bình p95 của các lượt làm p95 của một tập dữ liệu gộp.
- Mermaid trong architecture/diagrams/mermaid/ là source chỉnh sửa. PNG/SVG cần xuất lại sau khi source đổi; nếu chưa xuất, phải ghi rõ ảnh là snapshot cũ.
- Không ghi secrets, password, access token hoặc connection string thật vào Git.
