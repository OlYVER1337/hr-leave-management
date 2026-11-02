# Kiểm thử giao diện

Frontend sử dụng Vitest và React Testing Library để kiểm tra các luồng chính trên
DOM giả lập jsdom.

```powershell
npm ci
npm test -- --run
npm run build
```

Các bài kiểm thử hiện bao phủ đăng nhập thành công/thất bại, gửi đơn nghỉ hợp lệ,
chặn đơn không có ngày làm việc, phê duyệt đơn và xử lý lỗi khi quyết định thất bại.
