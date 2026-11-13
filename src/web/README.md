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
Trang Nhân viên có kiểm thử tìm kiếm, lọc, sắp xếp, phân trang theo block, giới hạn
cache LRU, tải summary theo một request và truyền tham số phân trang đến HR API.
