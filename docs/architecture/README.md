# Sơ đồ kiến trúc và dữ liệu

Các sơ đồ được chia theo định dạng để tiện sửa và xem:

- `mermaid/`: source chỉnh sửa được.
- `png/`: ảnh tương thích với hầu hết trình xem Markdown.
- `svg/`: ảnh vector khi cần phóng to.
- `legacy-png/`: các bản PNG cũ giữ lại để bảo toàn lịch sử; dùng bản trong `png/` làm bản hiển thị hiện hành.

| Sơ đồ | PNG hiện hành | SVG | Mermaid |
|---|---|---|---|
| Kiến trúc API | [PNG](diagrams/png/api-architecture.png) | [SVG](diagrams/svg/api-architecture.svg) | [Source](diagrams/mermaid/api-architecture.mmd) |
| ERD HR database | [PNG](diagrams/png/erd-hr-db.png) | [SVG](diagrams/svg/erd-hr-db.svg) | [Source](diagrams/mermaid/erd-hr-db.mmd) |
| ERD Leave database | [PNG](diagrams/png/erd-leave-db.png) | [SVG](diagrams/svg/erd-leave-db.svg) | [Source](diagrams/mermaid/erd-leave-db.mmd) |
| Đăng nhập | [PNG](diagrams/png/seq-login.png) | [SVG](diagrams/svg/seq-login.svg) | [Source](diagrams/mermaid/seq-login.mmd) |
| Tạo đơn nghỉ | [PNG](diagrams/png/seq-create-leave.png) | [SVG](diagrams/svg/seq-create-leave.svg) | [Source](diagrams/mermaid/seq-create-leave.mmd) |
| Duyệt đơn nghỉ | [PNG](diagrams/png/seq-approve-leave.png) | [SVG](diagrams/svg/seq-approve-leave.svg) | [Source](diagrams/mermaid/seq-approve-leave.mmd) |

Khi cập nhật sơ đồ, sửa Mermaid trước, sau đó xuất lại PNG và SVG cùng tên. Không
đặt bản mới ở thư mục gốc `docs/`; dùng đúng các thư mục định dạng ở trên.
