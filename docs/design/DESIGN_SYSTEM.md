# Design System — Peopleflow HR Suite

> Tài liệu nền tảng giao diện được đối chiếu với source code repository ngày 02/11/2026. Đây là baseline cho giao diện hiện tại, không phải tuyên bố rằng mọi đề xuất trong tài liệu đã được triển khai.

## 1. Phạm vi và nguyên tắc

Sản phẩm hiển thị thương hiệu **Peopleflow HR Suite** trong Login và AppShell. Frontend sử dụng React, TypeScript, Vite, React Router, Axios, Recharts và Lucide React. Style hiện tại tập trung trong \`src/web/src/styles/global.css\`, component dùng chung nằm trong \`src/web/src/components\`.

### Nguyên tắc thiết kế

1. **Clarity first:** trạng thái đơn, hạn mức phép, người duyệt và hành động tiếp theo phải nhìn thấy ngay.
2. **Role-aware:** điều hướng và thao tác chỉ hiển thị theo vai trò HR, Manager hoặc Employee; backend vẫn là lớp quyết định quyền cuối cùng.
3. **Data before decoration:** dashboard ưu tiên số liệu và danh sách cần xử lý; biểu đồ chỉ bổ trợ.
4. **Predictable actions:** thao tác tạo, sửa, duyệt, từ chối, hủy và vô hiệu hóa phải có phản hồi thành công/thất bại.
5. **Consistent Vietnamese UI:** nội dung giao diện hiện tại dùng tiếng Việt; mã trạng thái và mã loại nghỉ chỉ hiển thị khi có ích cho người dùng.

### Quy ước ghi nhận

- **Đang dùng:** giá trị hoặc pattern đọc được trực tiếp từ CSS/TSX hiện tại.
- **Chuẩn hóa đề xuất:** token hoặc quy tắc nên thống nhất để tránh mỗi màn hình tự đặt style.
- **Thiếu UI:** backend/API có khả năng nhưng frontend chưa cung cấp thao tác tương ứng.

## 2. Visual identity

### 2.1. Logo và thương hiệu

- Tên sản phẩm: **Peopleflow**.
- Biểu tượng hiện tại: mark hình vuông bo góc, nền gradient xanh tím, icon Sparkles.
- Sidebar dùng wordmark Peopleflow và nhãn nhỏ “QUẢN TRỊ NHÂN SỰ”.
- Login dùng cùng mark và wordmark; trên màn hình hẹp, phần thương hiệu bên trái được ẩn và brand mobile xuất hiện trong form.

Không thay đổi tên hoặc biểu tượng trong các màn hình con để giữ nhận diện nhất quán.

### 2.2. Typography

| Vai trò | Font đang dùng | Quy tắc |
|---|---|---|
| Body / form / bảng | DM Sans | Font chính cho nội dung và điều khiển |
| Heading / số liệu | Manrope | Dùng cho H1, tiêu đề panel, KPI và số lớn |
| Fallback | Inter, system UI, Segoe UI | Dùng khi font web không tải được |

Font được import trong \`global.css\` từ Google Fonts. Heading đang dùng trọng lượng 700–800; body chủ yếu 400–700.

**Chuẩn hóa đề xuất:** giữ DM Sans + Manrope; đặt kích thước tối thiểu dễ đọc khi triển khai responsive:
- Page title: 24–30 px.
- Section/panel title: 15–18 px.
- Body / table content: 13–14 px.
- Field label / helper: 12–13 px.
- Metadata phụ: không thấp hơn 11 px nếu thông tin có ý nghĩa nghiệp vụ.

Một số text trong CSS hiện tại nhỏ khoảng 9–12 px. Khi cải tiến, cần tăng cỡ chữ ở bảng, field label và metadata trên màn hình laptop nhỏ/mobile; không giảm thêm.

## 3. Color system

### 3.1. Core tokens đang có trong :root

| Token | Giá trị | Công dụng |
|---|---|---|
| Ink | \`#1e2c43\` | Màu chữ chính theo token |
| Muted | \`#8490a4\` | Nội dung phụ |
| Line | \`#e8edf4\` | Border/divider |
| Panel | \`#ffffff\` | Bề mặt card/panel |
| Primary | \`#5276e8\` | Hành động chính, link |
| Primary dark | \`#4264d6\` | Hover của primary |
| Sidebar | \`#17243b\` | Nền điều hướng trái |
| Sidebar muted | \`#9ca8bd\` | Nhãn phụ trong sidebar |
| Green | \`#2eaf83\` | Thành công/đã duyệt |
| Amber | \`#e6a437\` | Chờ xử lý/cảnh báo |
| Red | \`#d95c6b\` | Lỗi/từ chối |
| Page background | \`#f5f7fb\` | Nền app |
| Body text | \`#26334a\` | Màu chữ mặc định |

### 3.2. Status colors

Status phải luôn đi cùng label chữ; không chỉ dùng màu hoặc chấm trạng thái.

| Trạng thái API | Nhãn tiếng Việt | Text | Background |
|---|---|---|---|
| Pending | Chờ duyệt | \`#ae781d\` | \`#fff5da\` |
| Approved | Đã duyệt | \`#218c69\` | \`#e2f7ef\` |
| Rejected | Bị từ chối | \`#c45666\` | \`#fff0f1\` |
| Cancelled | Đã hủy | \`#7b879b\` | \`#edf0f5\` |

Các nhãn này tương ứng với \`StatusBadge\` trong \`components/ui.tsx\`.

### 3.3. Role colors

| Role | Nhãn hiển thị | Text | Background |
|---|---|---|---|
| HR | Nhân sự | \`#9b6c1b\` | \`#fff7e5\` |
| Manager | Quản lý | \`#7c68c6\` | \`#f5f1ff\` |
| Employee | Nhân viên | \`#4d70d9\` | \`#f0f4ff\` |

Role badge là thông tin phân loại, không phải status tài khoản.

### 3.4. Leave type accents

| Code | Tên hiển thị | Accent hiện tại |
|---|---|---|
| ANNUAL | Nghỉ phép năm | Blue \`#5679e8\` |
| SICK | Nghỉ ốm | Violet \`#9c7de8\` |
| UNPAID | Nghỉ không lương | Amber \`#e0a340\` |

Các loại nghỉ được nạp từ HR Service ở API mode; hệ thống chỉ hỗ trợ ba mã chuẩn
`ANNUAL`, `SICK` và `UNPAID`. HR có thể khôi phục loại chuẩn còn thiếu, nhưng
backend từ chối mã loại nghỉ tùy ý. Chỉ loại trừ phép khỏi số dư được phép có
chính sách ngày phép theo năm.

### 3.5. Semantic states

- Success: green.
- Warning / Pending: amber.
- Error / destructive: red.
- Informational / neutral: blue hoặc slate.
- Disabled: giảm opacity và không cho click; không dùng màu disabled làm cách duy nhất để giải thích lý do.

## 4. Layout, grid và responsive

### 4.1. App shell đang dùng

- Sidebar desktop: 258 px; tại viewport tối đa 1250 px thu về 230 px.
- Topbar: 70 px; tại tablet giảm còn 64 px.
- Main content: padding desktop 30–35 px; dưới 1250 px padding ngang 25 px.
- Main content có max-width 1650 px.
- Page stack gap khoảng 22 px.
- Sidebar có trạng thái collapsed trên desktop và drawer + scrim trên mobile.

### 4.2. Breakpoints đang có

| Breakpoint | Hành vi |
|---|---|
| \`max-width: 1250px\` | Sidebar 230 px, giảm khoảng trống, department/policy grid còn 2 cột |
| \`max-width: 1024px\` | Điều chỉnh topbar, dashboard và các layout nhiều cột |
| \`max-width: 780px\` | Sidebar chuyển thành off-canvas drawer; main không còn margin trái; mobile menu/scrim xuất hiện |
| \`max-width: 480px\` | Thu gọn topbar, ẩn tên profile, giảm padding và kích thước button |

### 4.3. Grid đề xuất để giữ nhất quán

- KPI cards: 4 cột desktop; 2 cột tablet; 1–2 cột mobile tùy độ dài nhãn.
- Dashboard: chart + status breakdown; chuyển thành một cột ở tablet/mobile.
- Request form: form chính + summary sidebar; summary chuyển xuống dưới form trên mobile.
- Calendar: tháng + selected-day/upcoming panel; chuyển thành một cột trên màn hình hẹp.
- Bảng rộng: giữ table semantics và cho phép horizontal scroll; không thu chữ tới mức khó đọc.

## 5. Spacing, radius và elevation

CSS hiện tại dùng nhiều khoảng cách gần nhau, chưa có spacing scale độc lập. Chuẩn hóa đề xuất:

| Token | Giá trị |
|---|---:|
| space-1 | 4 px |
| space-2 | 8 px |
| space-3 | 12 px |
| space-4 | 16 px |
| space-5 | 20 px |
| space-6 | 24 px |
| space-8 | 32 px |
| space-10 | 40 px |

| Component | Radius hiện tại / đề xuất |
|---|---|
| Status badge | 6 px |
| Button, input, select | 8 px |
| Small selectable option | 9–10 px |
| Card / panel | 12–13 px |
| Modal | 15 px |
| Avatar | 9–12 px |

Shadow nền hiện tại: \`0 5px 18px rgba(29, 45, 74, 0.035)\`. Dùng shadow nhẹ cho card; modal dùng shadow rõ hơn để phân lớp. Border vẫn là cách phân tách chính, không lạm dụng shadow.

## 6. Component standards

### 6.1. Buttons

| Variant | Dùng khi nào |
|---|---|
| Primary | Hành động chính của màn hình: Gửi yêu cầu, Lưu, Thêm nhân viên |
| Secondary | Hành động phụ: Hủy, Quay lại, Hôm nay |
| Approve | Mở bước xem xét đơn; hiện tại dùng tông blue nhạt |
| Destructive | Xóa, từ chối, vô hiệu hóa; cần xác nhận khi hành động không thể hoàn tác |
| Icon button | Hành động phụ trong bảng/topbar; bắt buộc có aria-label hoặc title |

Button hiện tại có min-height khoảng 38 px, radius 8 px, font-weight 700. Loading state phải khóa thao tác lặp và đổi label như “Đang lưu…”.

### 6.2. Form controls

- Input/select/date: min-height khoảng 39 px, border \`#e3e8f0\`, radius 8 px.
- Focus: border primary tint và focus ring rõ.
- Required field có dấu * và label cụ thể.
- Validation đặt sát field; lỗi tổng thể đặt trong form error block.
- Helper text giải thích quy tắc, không lặp placeholder.
- Password field có show/hide; không hiển thị mật khẩu dạng text mặc định.
- Textarea có character counter khi có giới hạn.

### 6.3. Cards và panels

- Panel nền trắng, border line, radius 13 px.
- Padding phổ biến 18–24 px.
- Panel header gồm title, description và action ở bên phải.
- KPI card gồm label, icon tone, value và hint.
- Không dùng card lồng card nhiều tầng nếu không cần thiết.

### 6.4. Tables

Các bảng hiện dùng table HTML với header nền nhạt, border hàng và vùng scroll ngang.

Quy tắc:
- Cột đầu tiên là định danh chính (mã đơn hoặc nhân viên).
- Status và role dùng badge nhất quán.
- Hành động theo hàng đặt cuối bảng, icon có label hỗ trợ.
- Search/filter nằm trong toolbar ngay phía trên bảng.
- Footer hiển thị số bản ghi đang thấy / tổng số bản ghi.
- Khi có pagination trong tương lai, giữ bộ lọc sau khi chuyển trang.

### 6.5. StatusBadge, RoleBadge và EmployeeStatus

- StatusBadge: bốn trạng thái đơn Pending, Approved, Rejected, Cancelled.
- RoleBadge: HR, Manager, Employee.
- EmployeeStatus: “Đang hoạt động” / “Đã vô hiệu hóa”.
- Không dùng chung component status đơn nghỉ cho trạng thái nhân viên.

### 6.6. Avatar

Avatar hiện tạo initials từ tên; kích thước small 29 px, medium 34 px, large 42 px. Nếu chưa có ảnh hồ sơ thì initials là fallback chính thức.

### 6.7. Modal và toast

- Modal tiêu chuẩn max-width 480 px; modal form dài max-width 660 px.
- Header, body, footer actions có phân cách rõ.
- Không đóng modal bằng click backdrop trong khi request đang submit.
- Toast dùng cho kết quả thao tác; inline alert dùng cho lỗi tải dữ liệu hoặc lỗi nằm trong ngữ cảnh trang.
- Không dùng browser alert/confirm cho trải nghiệm cuối cùng; thay bằng modal xác nhận hoặc detail drawer đồng nhất.

### 6.8. Empty, loading và error states

- Loading: skeleton hoặc loading line; không nháy layout.
- Empty: icon + tiêu đề + hướng dẫn + CTA nếu có thao tác tạo.
- Error: mô tả dễ hiểu, hành động thử lại khi có thể.
- Forbidden: hiển thị không có quyền, không render action trái quyền.
- Success: toast ngắn và cập nhật dữ liệu liên quan.

## 7. Data visualization

Dashboard sử dụng Recharts:
- Bar chart: số yêu cầu theo tháng, hai series ANNUAL và SICK.
- Donut chart: trạng thái đơn.
- Progress track: tỷ lệ số dư còn lại theo hạn mức.

Quy tắc:
- Luôn có legend và label; tooltip không phải nguồn duy nhất để hiểu dữ liệu.
- Không biểu diễn status không có trong series như thể bằng 0 nếu nó chỉ đang bị bỏ khỏi biểu đồ.
- Tổng ở giữa donut phải đối chiếu được với tổng các phần; hiện biểu đồ status chưa có phần Cancelled trong khi số tổng lấy toàn bộ request. Cần bổ sung Cancelled hoặc đổi mẫu số.
- Số dư UNPAID không có entitlement cố định; hiển thị “Không giới hạn” thay vì thanh progress dựa trên số ngày.

## 8. Content & interaction rules

- Ngày dùng định dạng tiếng Việt; khoảng ngày hiển thị rõ ngày bắt đầu và kết thúc.
- Số ngày ghi kèm đơn vị “ngày”.
- Pending = “Chờ duyệt”; Approved = “Đã duyệt”; Rejected = “Bị từ chối”; Cancelled = “Đã hủy”.
- Confirmation phải nêu rõ đối tượng và tác động, ví dụ vô hiệu hóa tài khoản sẽ chặn đăng nhập.
- Lý do nghỉ là dữ liệu cá nhân; chỉ hiển thị cho người có quyền xem request. Form hiện cũng nhắc người dùng hạn chế nhập thông tin nhạy cảm.
- Client-side estimate không được mô tả là kết quả cuối cùng: backend tính lại ngày làm việc, ngày lễ, overlap và số dư.

## 9. Accessibility checklist

- Tất cả icon-only buttons có accessible name.
- Dùng label liên kết với input; không chỉ dùng placeholder.
- Focus-visible phải nhìn thấy bằng bàn phím.
- Modal có role dialog, aria-modal, title và focus management.
- Status có text, không chỉ màu.
- Bảng có header semantics và vùng cuộn có thể thao tác.
- Responsive không làm mất action quan trọng.
- Text có độ tương phản phù hợp; kiểm tra lại các màu muted nhỏ trước khi phát hành.

## 10. Source map

| Khu vực | Source |
|---|---|
| App shell, nav, topbar, footer | \`src/web/src/components/AppShell.tsx\` |
| Shared UI primitives | \`src/web/src/components/ui.tsx\` |
| Modal / toast | \`src/web/src/components/Modal.tsx\`, \`ToastProvider.tsx\` |
| Route guard / role gating | \`src/web/src/App.tsx\` |
| Global tokens và component CSS | \`src/web/src/styles/global.css\` |
| Role/status/type definitions | \`src/web/src/types/index.ts\` |
| Auth context | \`src/web/src/contexts.tsx\` |
