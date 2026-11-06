# Checklist bàn giao

Checklist này phân biệt giữa **bằng chứng hiện có trong repository** và những công việc vẫn cần **release chính thức hoặc xác nhận trực tiếp của con người**.

**Lần rà soát bằng chứng gần nhất: 03/11/2026**

| # | Hạng mục bàn giao | Trạng thái | Nội dung đã có / còn lại |
|---:|---|---|---|
| **1** | **Source code và Git history** | **Đã có; còn chờ thao tác release** | Frontend, HR Service, Leave Service, automated tests, Docker/Compose và tài liệu đều đã có trong repository. Git đã có lịch sử commit được thiết lập. Worktree hiện tại vẫn chứa các thay đổi chưa commit; cần kiểm tra, commit và push lên remote dự kiến trước khi xác nhận bàn giao source code hoàn tất. |
| **2** | **Ứng dụng đang chạy và các workflow theo vai trò** | **Luồng API đã được kiểm tra; UI đã được chấp nhận** | Demo dựa trên Docker Compose, đăng nhập, nhân viên tạo đơn nghỉ, Manager phê duyệt và cập nhật số dư phép đã được kiểm tra thông qua API trên stack `hrbench` độc lập. Người dùng đã xác nhận chấp nhận giao diện UI vào ngày 03/11/2026. Các workflow của HR/Manager/Employee được mô tả trong `USER-GUIDE.md`. Dashboard có dữ liệu; chức năng xuất báo cáo và thông báo hiện chưa được triển khai. |
| **3** | **Thiết kế hệ thống** | **Đã được tài liệu hóa** | `SYSTEM_DESIGN.md` bao gồm thiết kế hệ thống, sơ đồ kiến trúc và sequence diagram, quyền sở hữu dữ liệu của từng service, cơ chế giao tiếp và hành vi khi xảy ra lỗi. |
| **4** | **Thiết kế cơ sở dữ liệu** | **Đã được tài liệu hóa** | `DATABASE_DESIGN.md` bao gồm cả hai ERD, migrations, cơ chế seed dữ liệu khi chạy hệ thống, indexes và constraints. |
| **5** | **Tài liệu API** | **Đã được tài liệu hóa** | `API_REFERENCE.md`, OpenAPI JSON trong môi trường Development, ví dụ request/response và các HTTP status code. Các API HR chỉ cho phép khôi phục các loại nghỉ mặc định hoặc thêm policy bị thiếu cho các loại nghỉ có trừ số dư. |
| **6** | **Frontend Design System** | **Đã được tài liệu hóa; UI đã được chấp nhận, accessibility audit vẫn chưa thực hiện** | `DESIGN_SYSTEM.md` bao gồm sơ đồ các shared UI component/source, quy tắc layout và tương tác. Người dùng đã chấp nhận UI; tuy nhiên chưa thực hiện một đợt kiểm tra accessibility riêng biệt. |
| **7** | **Thiết kế và bằng chứng Redis** | **Đã được tài liệu hóa và kiểm tra** | `redis-performance.md` ghi nhận Redis key, TTL, cơ chế invalidation, fallback, hành vi khi Redis bị dừng và các phép đo Catalog khi Redis ON/OFF. Redis được sử dụng làm cache cho HR catalog; các quyết định liên quan đến Leave không phụ thuộc vào Redis. |
| **8** | **Tests và báo cáo hiệu năng** | **Automated tests đã pass; lỗi load test lịch sử chưa xác định nguyên nhân** | Kết quả mới nhất: HR **53/53**, Leave **25/25**, Web **31/31**; build đã pass. Catalog k6 bao gồm Redis ON/OFF tại 50/100 VU. Lần chạy Workforce đầu tiên ở 100 VU xuất hiện **21 HTTP 500 (0,31%)**; hai lần chạy lại sau đó ở 100 VU đều không có lỗi, nhưng nguyên nhân của lỗi ban đầu vẫn chưa xác định và Workforce Redis OFF chưa được đo. Chưa có code fix nào được xác nhận. Xem `demo-and-acceptance.md`. |
| **9** | **Deployment package** | **Đã có; còn chờ kiểm tra triển khai trong môi trường sạch** | Root Compose file, shared .NET Dockerfile, Web Dockerfile/Nginx config, `.env.example`, persistent volumes và hướng dẫn khởi động mới đều đã có. Cần chạy thử từ một clean clone/máy mới chỉ sử dụng các prerequisites đã được tài liệu hóa trước khi production hoặc bàn giao ra bên ngoài. |

---

# Các yêu cầu bắt buộc trước khi ký xác nhận bàn giao cuối cùng

## 1. Kiểm tra và hoàn tất Git worktree

Cần:

- Kiểm tra các thay đổi hiện tại trong worktree.
- Loại bỏ các benchmark artifact, file local hoặc file được sinh tự động nếu không cần đưa vào repository.
- Commit source code và tài liệu cần thiết.
- Push lên Git remote đã thống nhất.

Chỉ sau đó mới nên xác nhận bàn giao source code hoàn tất.

---

## 2. Tiếp tục điều tra lỗi HTTP 500 trong Workforce load test

Vấn đề này vẫn cần được theo dõi cho đến khi:

- Xác định được nguyên nhân của **21 HTTP 500** trong lần chạy 100 VU đầu tiên.
- Ghi lại nguyên nhân.
- Kiểm tra và xác nhận kết quả sau khi xử lý.

Hiện tại:

- Hai lần chạy lại ở 100 VU đều có **0 lỗi**.
- Nguyên nhân lỗi ban đầu vẫn chưa được xác định.
- Chưa có code fix nào được xác nhận.

Nếu Redis OFF là một phần của acceptance criteria, cần thu thập thêm kết quả:

```text id="6tz49b"
Workforce + Redis OFF
```

để so sánh.

---

## 3. Accessibility audit — tùy chọn

Có thể thực hiện và ghi lại một đợt kiểm tra accessibility riêng.

Hiện tại:

- UI đã được người dùng kiểm tra và chấp nhận về mặt trực quan.
- Chưa có accessibility audit chính thức.

Việc người dùng chấp nhận giao diện trực quan **không đồng nghĩa** với việc hệ thống đã đạt accessibility compliance.

---

## 4. Kiểm tra triển khai từ môi trường sạch

Cần thực hiện thử nghiệm triển khai bằng cách:

1. Clone repository mới.
2. Sử dụng một môi trường/máy sạch.
3. Chỉ cài đặt các prerequisites được ghi trong tài liệu.
4. Làm theo hướng dẫn deployment từ đầu.
5. Xác nhận toàn bộ hệ thống có thể chạy thành công.

Đối với môi trường production, cần thay thế các cấu hình local mặc định bằng:

- Quản lý secrets.
- TLS/HTTPS.
- Backup.
- Operational monitoring.

---

# Lưu ý quan trọng

**Không được xem các hạng mục đang mở là đã hoàn thành chỉ vì chúng đã được ghi trong tài liệu.**

Hai rủi ro bàn giao cần được lưu ý rõ ràng là:

1. **Kết quả HTTP 500 lịch sử trong Workforce load test vẫn chưa xác định được nguyên nhân.**
2. **Worktree có thay đổi chưa commit là rủi ro đối với việc bàn giao source code.**

Các vấn đề này cần được xử lý hoặc được chấp nhận chính thức như các rủi ro còn tồn tại trước khi thực hiện **final sign-off**.