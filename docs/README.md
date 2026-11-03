# Tài liệu dự án HR Leave Management

Mục lục dưới đây sắp xếp tài liệu theo mục đích. Nếu mới tiếp cận dự án, nên đọc
theo thứ tự: **yêu cầu → kiến trúc → thiết kế giao diện → hiệu năng/kiểm thử →
kịch bản demo**.

## 1. Yêu cầu và phạm vi

- [Đề tài thực tập 2026](requirements/internship-project-2026.docx): yêu cầu và tiêu chí bàn giao gốc.
- [Mô tả hệ thống HR Leave Management](requirements/system-description.docx): nghiệp vụ, vai trò và phạm vi hệ thống.
- [Backlog](planning/BACKLOG.md): các gap còn mở và thứ tự ưu tiên hoàn thiện dự án.

## 2. Kiến trúc và sơ đồ

- [Danh mục sơ đồ kiến trúc và database](architecture/README.md): kiến trúc tổng thể, ERD và sequence diagram; mỗi sơ đồ có source Mermaid và bản xem PNG/SVG.

## 3. Giao diện

- [Design System](design/DESIGN_SYSTEM.md): màu sắc, typography, layout, component, responsive và accessibility.
- [Wireframes](design/WIREFRAMES.md): màn hình và luồng theo vai trò.

## 4. Hiệu năng và kiểm thử

- [Báo cáo Redis và load test ON/OFF](performance/redis-performance.md): cache, fallback, concurrency và benchmark 50/100 VU.
- [Kết quả kiểm thử, chẩn đoán và checklist nghiệm thu](testing/demo-and-acceptance.md): test regression/integration, CPU/SQL/query plan và kịch bản demo.
- [Bằng chứng kiểm thử thô](testing/evidence/): TRX, log build, kết quả k6 và số liệu container.

## 5. Hướng dẫn chạy hệ thống

- [README dự án](../README.md): cấu hình môi trường, Docker Compose, tài khoản khởi tạo và lệnh test.

## Quy ước cập nhật

- Tài liệu yêu cầu gốc được giữ nguyên trong `requirements/`; các thay đổi triển khai nên được ghi ở tài liệu theo chủ đề tương ứng.
- Sơ đồ được sửa ở file Mermaid trong `architecture/diagrams/mermaid/`; xuất lại PNG/SVG cùng tên để bản đọc và source không lệch nhau.
- Số liệu hiệu năng phải đi kèm workload, môi trường và thời điểm đo. Không ghi secret hoặc thông tin đăng nhập vào tài liệu.
- Khi di chuyển tài liệu, cập nhật liên kết trong mục lục này và các tài liệu tham chiếu tới nó.
