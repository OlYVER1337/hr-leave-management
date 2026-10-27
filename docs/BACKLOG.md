# Việc cần làm tiếp theo

## HR Service

- Bổ sung unit test cho các nghiệp vụ HR còn lại.

## Leave Service và giao diện

- Xây dựng Leave Service: số dư, tạo/hủy đơn, luồng phê duyệt và lịch sử.
- Xử lý đồng thời khi cập nhật số dư và trạng thái đơn; kiểm thử xung đột.
- Xây dựng giao diện React cho nhân viên, quản lý và HR.
- Bổ sung kiểm thử hiệu năng và kiểm thử chịu lỗi theo đề tài.

## Giới hạn đã biết

- JWT hiện là stateless access token. Khi tài khoản bị vô hiệu hóa, token đã phát hành vẫn hợp lệ đến khi hết hạn (mặc định tối đa 60 phút). Cần cân nhắc rút ngắn thời hạn hoặc bổ sung cơ chế thu hồi/token version nếu cần vô hiệu hóa ngay.
