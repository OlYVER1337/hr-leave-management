# Sơ đồ kiến trúc và dữ liệu


> **Lưu ý cập nhật 17/11/2026:** Mermaid source được đối chiếu với code hiện tại, gồm Dynamic RBAC, Team scope từ HR, Leave year allocations, balance self-sync và ranh giới HR_DB/Leave_DB. Các PNG/SVG hiện có vẫn là snapshot cũ; riêng sequence balance self-sync mới chỉ có Mermaid source, chưa có bản PNG/SVG xuất. Đọc Mermaid source làm chuẩn cho đến khi render lại ảnh.


Các sơ đồ được chia theo định dạng để tiện sửa và xem:

- `mermaid/`: source chỉnh sửa được.
- `png/`: bản xuất hiện có là snapshot cũ, cần render lại từ Mermaid source mới.
- `svg/`: bản vector hiện có cũng là snapshot cũ, cần render lại từ Mermaid source mới.
- `legacy-png/`: các bản PNG cũ giữ lại để bảo toàn lịch sử; dùng bản trong `png/` làm bản hiển thị hiện hành.

| Sơ đồ | PNG (snapshot cũ) | SVG (snapshot cũ) | Mermaid source (chuẩn hiện tại) |
|---|---|---|---|
| Kiến trúc API | [PNG](diagrams/png/api-architecture.png) | [SVG](diagrams/svg/api-architecture.svg) | [Source](diagrams/mermaid/api-architecture.mmd) |
| ERD HR database | [PNG](diagrams/png/erd-hr-db.png) | [SVG](diagrams/svg/erd-hr-db.svg) | [Source](diagrams/mermaid/erd-hr-db.mmd) |
| ERD Leave database | [PNG](diagrams/png/erd-leave-db.png) | [SVG](diagrams/svg/erd-leave-db.svg) | [Source](diagrams/mermaid/erd-leave-db.mmd) |
| Đăng nhập | [PNG](diagrams/png/seq-login.png) | [SVG](diagrams/svg/seq-login.svg) | [Source](diagrams/mermaid/seq-login.mmd) |
| Tạo đơn nghỉ | [PNG](diagrams/png/seq-create-leave.png) | [SVG](diagrams/svg/seq-create-leave.svg) | [Source](diagrams/mermaid/seq-create-leave.mmd) |
| Duyệt đơn nghỉ | [PNG](diagrams/png/seq-approve-leave.png) | [SVG](diagrams/svg/seq-approve-leave.svg) | [Source](diagrams/mermaid/seq-approve-leave.mmd) |
| Đồng bộ số dư cá nhân | Chưa xuất | Chưa xuất | [Source](diagrams/mermaid/seq-balance-sync.mmd) |

Khi cập nhật sơ đồ, sửa Mermaid trước, sau đó xuất lại PNG và SVG cùng tên. Với sơ đồ balance self-sync mới, hiện chỉ có Mermaid source; chưa tạo PNG/SVG. Không đặt bản mới ở thư mục gốc `docs/`; dùng đúng các thư mục định dạng ở trên.
