# Backlog sau kiểm tra nghiệm thu (03/11/2026)

Danh sách dưới đây chỉ giữ việc còn mở, dựa trên gap đã đối chiếu trong
[wireframe](../design/WIREFRAMES.md), [checklist nghiệm thu](../testing/demo-and-acceptance.md)
và phép đo CPU hiện có. Các luồng backend chính, benchmark Redis 50/100 VU trong
5 phút và cảnh báo NuGet đã được kiểm chứng riêng; không lặp lại chúng như việc
chưa làm.

| Ưu tiên | Việc còn lại | Tiêu chí hoàn thành | Bằng chứng hiện tại |
|---|---|---|---|
| P0 | Sửa số liệu và attribution trên dashboard HR/Manager | HR không gọi số dư cá nhân là số dư toàn tổ chức; approver hiển thị đúng người hoặc nhãn trung tính; Manager KPI khớp inbox approval. Có test theo từng role và khi Manager có đơn riêng. | [Gap UI](../design/WIREFRAMES.md), các mục P0 |
| P1 | Hoàn tất các thao tác quản trị còn thiếu | Chốt scope HR tự tạo đơn; thêm UI tạo Leave Type và reset mật khẩu nhân viên nếu thuộc scope; Settings điều hướng đúng hoặc đổi nhãn. API hiện có nhưng UI thiếu một số flow. | [Gap UI](../design/WIREFRAMES.md), mục P1 |
| P1 | Bỏ trạng thái UI giả/chưa hoàn thiện | Donut tính Cancelled nhất quán; chi tiết đơn dùng modal/drawer accessible; nút export và notification được triển khai hoặc ẩn/disable thay vì chỉ thông báo; kiểm thử error/loading/empty states. | [Gap UI](../design/WIREFRAMES.md), mục P1 |
| P1 | Chạy nghiệm thu giao diện trực tiếp ở API mode | Thực hiện kịch bản demo 8–10 phút trên Web với HR/Manager/Employee; xác nhận role guards, tạo/duyệt/từ chối/hủy, lỗi validation và responsive/accessibility; người nghiệm thu đánh dấu checklist. API flow đã chạy trên `hrbench`, nhưng UI/manual sign-off còn mở. | [Kịch bản và checklist](../testing/demo-and-acceptance.md) |
| P2 | Profile CPU của HR Service dưới tải | Dùng `dotnet-counters` hoặc trace trong workload catalog 100 VU; xác định hot path trước khi tối ưu; báo cáo cùng cấu hình và so sánh RPS/p95. Chưa thêm index nếu chưa có bằng chứng SQL. | HR Service trung bình 119.07% CPU trong workload SQL-authoritative 100 VU/2 phút; SQL catalog query dưới 0.3 ms CPU trung bình. |
| P2 | Chốt hành vi lịch và dữ liệu lớn | Quy định Calendar Manager theo current reporting line hay approver snapshot; phân trang bảng khi quy mô cần; đồng bộ giới hạn lý do 600/1.000 và nhãn “nhóm/tổ chức” theo đúng phạm vi dữ liệu. | [Gap UI](../design/WIREFRAMES.md), mục P2 |

## Thứ tự đề xuất

1. Xử lý các mismatch P0 để tránh hiển thị sai số liệu hoặc người duyệt.
2. Chốt phạm vi nghiệp vụ HR rồi hoàn tất các flow quản trị và trạng thái UI.
3. Chạy browser acceptance trên stack demo hiện có; chỉ đánh dấu các mục UI sau
   khi người nghiệm thu thực sự xác nhận.
4. Profile CPU API theo bằng chứng, sau đó mới quyết định tối ưu code hoặc SQL.
