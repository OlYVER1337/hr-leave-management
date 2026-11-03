# Backlog sau kiểm tra nghiệm thu (03/11/2026)

Danh sách dưới đây chỉ giữ việc còn mở, dựa trên gap đã đối chiếu trong
[wireframe](../design/WIREFRAMES.md), [checklist nghiệm thu](../testing/demo-and-acceptance.md)
và phép đo CPU hiện có. Các luồng backend chính, benchmark Redis 50/100 VU trong
5 phút và cảnh báo NuGet đã được kiểm chứng riêng; không lặp lại chúng như việc
chưa làm.

| Ưu tiên | Việc còn lại | Tiêu chí hoàn thành | Bằng chứng hiện tại |
|---|---|---|---|
| P1 | Triển khai reset mật khẩu an toàn khi backend hỗ trợ | Thêm action cho HR sau khi có mật khẩu tạm thời và cờ bắt buộc đổi mật khẩu ở lần đăng nhập kế tiếp. Tạm hoãn theo quyết định nghiệp vụ; không đưa UI đặt mật khẩu trực tiếp. | [Gap UI](../design/WIREFRAMES.md), mục P1 |
| P1 | Chạy nghiệm thu giao diện trực tiếp ở API mode | Thực hiện kịch bản demo 8–10 phút trên Web với HR/Manager/Employee; xác nhận role guards, tạo/duyệt/từ chối/hủy, lỗi validation và responsive/accessibility; người nghiệm thu đánh dấu checklist. API flow đã chạy trên `hrbench`, nhưng UI/manual sign-off còn mở. | [Kịch bản và checklist](../testing/demo-and-acceptance.md) |
| P1 | Điều tra lỗi workforce ở 100 VU | Xác định nguyên nhân 21 HTTP 500 (0,31%) trên workload đọc nhân viên/đơn nghỉ; khắc phục nếu là lỗi ứng dụng, chạy lại 100 VU và đo thêm Redis OFF. 50 VU/5 phút hiện 0 lỗi. | [Workforce load summary](../testing/evidence/workforce-load-summary.json) |
| P1 | Chốt kỳ vọng access token sau deactivate | Nếu cần vô hiệu hóa JWT tức thời, chọn giải pháp revocation dùng chung giữa HR/Leave và đánh giá ảnh hưởng lên xác thực stateless; hiện deactivate thu hồi refresh token, JWT đã phát hành còn hiệu lực tới expiry. | [Đối chiếu yêu cầu](../testing/demo-and-acceptance.md) |
| P2 | Profile CPU của HR Service dưới tải | Dùng `dotnet-counters` hoặc trace trong workload catalog 100 VU; xác định hot path trước khi tối ưu; báo cáo cùng cấu hình và so sánh RPS/p95. Chưa thêm index nếu chưa có bằng chứng SQL. | HR Service trung bình 119.07% CPU trong workload SQL-authoritative 100 VU/2 phút; SQL catalog query dưới 0.3 ms CPU trung bình. |
| P2 | Chốt hành vi lịch và dữ liệu lớn | Quy định Calendar Manager theo current reporting line hay approver snapshot; chuyển phân trang danh sách nhân viên/hộp thư sang server-side nếu quy mô dữ liệu cần; đồng bộ giới hạn lý do 600/1.000 và nhãn “nhóm/tổ chức” theo đúng phạm vi dữ liệu. | [Gap UI](../design/WIREFRAMES.md), mục P2 |

## Thứ tự đề xuất

1. Chạy browser acceptance trên stack demo hiện có; chỉ đánh dấu các mục UI sau
   khi người nghiệm thu thực sự xác nhận.
2. Triển khai reset mật khẩu khi backend hỗ trợ ép đổi mật khẩu.
3. Profile CPU API theo bằng chứng, sau đó mới quyết định tối ưu code hoặc SQL.
