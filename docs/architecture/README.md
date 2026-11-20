# Sơ đồ kiến trúc và dữ liệu

> Các file Mermaid trong `diagrams/mermaid/` là source chuẩn. PNG và SVG được render tự động từ các source này bằng GitHub Actions khi source sơ đồ thay đổi. Không chỉnh sửa trực tiếp ảnh PNG/SVG; hãy sửa Mermaid rồi để workflow tái tạo.

## Danh sách sơ đồ

| Sơ đồ | PNG | SVG | Mermaid source |
|---|---|---|---|
| Kiến trúc API và ranh giới service/database | [PNG](diagrams/png/api-architecture.png) | [SVG](diagrams/svg/api-architecture.svg) | [Source](diagrams/mermaid/api-architecture.mmd) |
| ERD HR_DB — nhân viên, phòng ban và Dynamic RBAC | [PNG](diagrams/png/erd-hr-db.png) | [SVG](diagrams/svg/erd-hr-db.svg) | [Source](diagrams/mermaid/erd-hr-db.mmd) |
| ERD Leave_DB — đơn nghỉ, số dư và lịch sử | [PNG](diagrams/png/erd-leave-db.png) | [SVG](diagrams/svg/erd-leave-db.svg) | [Source](diagrams/mermaid/erd-leave-db.mmd) |
| Đăng nhập và làm mới JWT/permission | [PNG](diagrams/png/seq-login.png) | [SVG](diagrams/svg/seq-login.svg) | [Source](diagrams/mermaid/seq-login.mmd) |
| Luồng tạo đơn nghỉ và giữ số dư | [PNG](diagrams/png/seq-create-leave.png) | [SVG](diagrams/svg/seq-create-leave.svg) | [Source](diagrams/mermaid/seq-create-leave.mmd) |
| Luồng lấy hộp thư và duyệt/từ chối đơn | [PNG](diagrams/png/seq-approve-leave.png) | [SVG](diagrams/svg/seq-approve-leave.svg) | [Source](diagrams/mermaid/seq-approve-leave.mmd) |
| Luồng khởi tạo/đồng bộ số dư cá nhân | [PNG](diagrams/png/seq-balance-sync.png) | [SVG](diagrams/svg/seq-balance-sync.svg) | [Source](diagrams/mermaid/seq-balance-sync.mmd) |

## Quy tắc cập nhật

- Sửa file `.mmd` trong `diagrams/mermaid/`.
- Push thay đổi lên branch `feature/dynamic-authorization-v1`; workflow `.github/workflows/render-diagrams.yml` sẽ render tất cả sơ đồ sang PNG và SVG.
- Workflow commit các ảnh được sinh ra vào chính branch này.
- `legacy-png/` chỉ lưu các hình cũ để tham khảo lịch sử, không dùng làm sơ đồ hiện hành.
- Nếu workflow thất bại, xem log ở tab Actions và không coi các ảnh cũ là đã đồng bộ với Mermaid source.
