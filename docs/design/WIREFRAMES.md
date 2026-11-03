
# Wireframe — Peopleflow HR Leave Management

> Đặc tả wireframe dựa trên React/TypeScript frontend, HR Service và Leave Service trên branch master tại thời điểm rà soát 02/11/2026. Nội dung phân biệt rõ chức năng đang có và đề xuất chưa triển khai.

## 1. Phạm vi và cách đọc

Frontend hiện có các page:
- LoginPage
- DashboardPage
- LeaveRequestsPage
- RequestLeavePage
- ApprovalsPage
- CalendarPage
- EmployeesPage
- DepartmentsPage
- PoliciesPage (tab Loại nghỉ & hạn mức, tab Ngày nghỉ lễ)
- ProfilePage

Frontend có hai chế độ:
- **Demo mode:** dữ liệu seed trong mockStore.ts, lưu ở localStorage của trình duyệt. Đăng nhập bằng tài khoản demo trong README; không ghi xuống SQL Server.
- **API mode:** gọi HR Service và Leave Service. Access token lưu trong sessionStorage; refresh token được backend đặt trong HttpOnly cookie.

Không được mô tả dữ liệu Demo như dữ liệu production. Những mục ghi **Đề xuất** là phần cần phát triển, không phải chức năng hiện có.

## 2. Vai trò và điều hướng

### 2.1. Vai trò

| Role trong token/code | Nhãn UI | Phạm vi |
|---|---|---|
| Employee | Nhân viên | Hồ sơ và yêu cầu nghỉ của bản thân |
| Manager | Quản lý | Yêu cầu bản thân và các yêu cầu thuộc phạm vi phê duyệt |
| HR | Nhân sự | Quản lý nhân sự/danh mục và xem đơn toàn tổ chức |

Backend là nguồn quyết định quyền cuối cùng. Ẩn link/action trong React chỉ là UX, không thay thế authorization.

### 2.2. Menu hiện tại trong AppShell

| Menu | Employee | Manager | HR |
|---|:---:|:---:|:---:|
| Tổng quan | ✓ | ✓ | ✓ |
| Đơn nghỉ của tôi | ✓ | ✓ | — |
| Tạo đơn nghỉ | ✓ | ✓ | — |
| Hộp thư phê duyệt | — | ✓ | ✓ |
| Lịch nghỉ của nhóm | ✓ | ✓ | ✓ |
| Nhân viên | — | — | ✓ |
| Phòng ban | — | — | ✓ |
| Chính sách nghỉ phép | — | — | ✓ |
| Tất cả đơn nghỉ | — | — | ✓ |
| Hồ sơ của tôi | ✓ | ✓ | ✓ |

Profile và Help nằm cuối sidebar. Header có ngày hiện tại, nhãn Demo (nếu bật), notification icon và profile menu.

### 2.3. Route guard đang triển khai

| Route | Quyền |
|---|---|
| /login | Public |
| / | Authenticated |
| /leaves | Authenticated |
| /all-leaves | HR |
| /request | Authenticated |
| /approvals | Manager, HR |
| /calendar | Authenticated |
| /employees | HR |
| /departments | HR |
| /policies | HR |
| /profile | Authenticated |

Nếu chưa đăng nhập, ứng dụng chuyển về Login. Nếu không đủ role, route guard chuyển về Dashboard.

**Điểm cần quyết định:** HR không có link “Tạo đơn nghỉ” trong sidebar nhưng route /request vẫn mở được và Dashboard/Calendar có CTA tới route này. Chọn một chính sách rõ ràng:
1. Cho HR tạo đơn nghỉ cho bản thân: thêm link phù hợp và hiển thị approver thực tế.
2. Không cho HR tạo đơn: ẩn CTA tương ứng với HR và chặn route bằng role guard.

Không nên để nav và route thể hiện hai ý định khác nhau.

## 3. Global application shell

Bố cục desktop:

    ┌──────────────────────┬──────────────────────────────────────────────────────────────┐
    │ PEOPLEFLOW           │ Không gian làm việc  >  Quản trị nhân sự       Ngày · User    │
    │                      ├──────────────────────────────────────────────────────────────┤
    │ Không gian nhân sự   │                                                              │
    │                      │  PAGE CONTENT                                                │
    │ LÀM VIỆC             │                                                              │
    │  Tổng quan           │  Page title                                                  │
    │  Đơn nghỉ của tôi    │  Description                           [Secondary] [Primary]│
    │  Tạo đơn nghỉ        │                                                              │
    │  Hộp thư phê duyệt   │  ┌────────────────────────────────────────────────────────┐  │
    │  Lịch nghỉ của nhóm  │  │ Panels / tables / forms / cards                         │  │
    │                      │  │                                                        │  │
    │ QUẢN TRỊ             │  └────────────────────────────────────────────────────────┘  │
    │  Nhân viên           │                                                              │
    │  Phòng ban           │                                                              │
    │  Chính sách nghỉ     │                                                              │
    │  Tất cả đơn nghỉ     │                                                              │
    │                      │                                                              │
    │ Hồ sơ của tôi        │                                                              │
    │ Trợ giúp & tài liệu  │                                                              │
    │ ──────────────────── │                                                              │
    │ Avatar · User · Role │ Bộ công cụ nhân sự Peopleflow                                │
    └──────────────────────┴──────────────────────────────────────────────────────────────┘

Yêu cầu:
- Desktop sidebar fixed, có nút collapse.
- Mobile sidebar chuyển thành drawer, có hamburger và scrim để đóng.
- Menu hiện tại phải có active state.
- Page header gồm eyebrow, title, description và action.
- Footer nằm dưới content.
- Profile menu hiện có Hồ sơ, mục “Cài đặt” và Đăng xuất. Hiện “Cài đặt” đang điều hướng tới /policies; nên đổi nhãn thành “Chính sách nghỉ phép” hoặc tạo Settings route riêng.

## 4. Wireframe từng màn hình

### 4.1. Login — /login

**Mục tiêu:** xác thực người dùng và giải thích giá trị sản phẩm.

    ┌──────────────────────────────────────┬─────────────────────────────────────┐
    │ PEOPLEFLOW                           │          [Fingerprint icon]         │
    │                                      │                                     │
    │ NỀN TẢNG QUẢN TRỊ NHÂN SỰ            │ CHÀO MỪNG TRỞ LẠI                   │
    │                                      │ Đăng nhập vào hệ thống              │
    │ Con người là trên hết.               │ Sử dụng tài khoản công việc...      │
    │ Làm việc hiệu quả hơn.               │                                     │
    │                                      │ [Demo mode note - nếu bật]          │
    │ Benefit: gửi yêu cầu nhanh           │                                     │
    │ Benefit: phối hợp đội nhóm           │ Email công việc                     │
    │ Benefit: phân quyền                  │ [ mail  you@company.com             ]│
    │                                      │ Mật khẩu                            │
    │                                      │ [ lock ***************       eye   ]│
    │                                      │ [Error / restore error]             │
    │                                      │ [       Đăng nhập Peopleflow       ]│
    │                                      │                                     │
    │                                      │ [Demo accounts - chỉ Demo mode]     │
    └──────────────────────────────────────┴─────────────────────────────────────┘

Fields: email, password.  
Actions: show/hide password, submit, quick-select demo account (chỉ Demo mode).  
States: invalid credentials, API error, restore session, submitting.  
API: HR POST /api/v1/auth/login; refresh/logout dùng các endpoint auth tương ứng. Refresh token được backend đặt trong cookie HttpOnly, không render token ra UI.

### 4.2. Dashboard — /

Dashboard hiện có welcome strip, KPI cards, chart hoạt động sáu tháng, donut status, recent requests và leave balance. KPI thay đổi theo role.

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ TỔNG QUAN                                           [Xuất báo cáo] [Tạo đơn]│
    │ Xin chào, {firstName}                                                       │
    │ Mô tả thay đổi theo Employee / Manager / HR                                │
    ├────────────────────────────────────────────────────────────────────────────┤
    │ Welcome strip: lời chào + hướng dẫn ngắn                                    │
    ├──────────────────┬──────────────────┬──────────────────┬───────────────────┤
    │ KPI 1            │ KPI 2            │ KPI 3            │ KPI 4             │
    ├────────────────────────────────────────────┬───────────────────────────────┤
    │ Hoạt động nghỉ phép (bar chart)            │ Trạng thái đơn (donut)        │
    │ 6 tháng gần đây                            │ Approved / Pending / Rejected │
    ├────────────────────────────────────────────┼───────────────────────────────┤
    │ Đơn nghỉ gần đây (table)                   │ Số dư phép (balance list)     │
    │                                             │ progress + used/pending       │
    └────────────────────────────────────────────┴───────────────────────────────┘

KPI theo code hiện tại:
- **HR:** nhân viên đang hoạt động; đơn chờ; đơn đã duyệt; số phòng ban.
- **Employee / Manager:** số ngày phép còn; số ngày đã nghỉ; đang chờ xử lý; tổng đơn của tôi.

Các điểm phải xử lý trước khi xem Dashboard là chuẩn production:
1. HR dashboard có nhãn “Tổng quan số dư phép” nhưng getBalances(user) chỉ trả balance của user hiện tại. Đổi thành “Số dư phép của tôi” hoặc bỏ khỏi dashboard toàn tổ chức.
2. Manager pending KPI đang đếm Pending trong tất cả request nhìn thấy; list này có thể gồm request của chính manager. Dùng approval inbox count cho KPI chờ manager xử lý và tách khỏi đơn pending của chính họ.
3. Bar chart đếm **số yêu cầu**, không phải tổng số ngày nghỉ. Đổi nhãn/subtitle cho đúng hoặc sửa phép tính nếu muốn thể hiện ngày nghỉ.
4. Donut hiện có Approved/Pending/Rejected nhưng số giữa donut lấy tổng toàn bộ request, có thể gồm Cancelled. Thêm slice Cancelled hoặc đổi mẫu số.
5. “Xuất báo cáo” hiện chỉ hiển thị toast thông báo API báo cáo chưa có. Không mô tả nó như một chức năng download đã hoạt động.

### 4.3. Đơn nghỉ của tôi — /leaves

**Mục tiêu:** xem lịch sử yêu cầu, lọc, mở chi tiết và hủy request Pending của chính mình.

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ NGHỈ PHÉP                                           [Xuất báo cáo]          │
    │ Đơn nghỉ phép của tôi                                                       │
    ├────────────────────────────────────────────────────────────────────────────┤
    │ Tổng số đơn      Chờ duyệt        Đã duyệt         Bị từ chối                 │
    ├────────────────────────────────────────────────────────────────────────────┤
    │ [Search mã đơn / nhân viên...] [Status ▼] [Leave type ▼]                    │
    ├──────────┬────────────┬───────────────┬────────┬──────────┬─────────┬────────┤
    │ Mã đơn   │ Loại nghỉ  │ Thời gian     │ Số ngày│ Người duyệt│Status │Action │
    ├──────────┼────────────┼───────────────┼────────┼──────────┼─────────┼────────┤
    │ LR-...   │ Nghỉ năm   │ 01/..—03/..   │ 3      │ Manager   │Pending │View X │
    │ LR-...   │ Nghỉ ốm    │ 10/..         │ 1      │ Manager   │Approved│View   │
    └──────────┴────────────┴───────────────┴────────┴──────────┴─────────┴────────┘
    Hiển thị {n} / {total} đơn

Scope:
- Employee thấy request của bản thân.
- Manager thấy request của bản thân và request có ApproverId snapshot trỏ tới manager.
- HR dùng route /all-leaves để xem toàn tổ chức.

Search hiện lọc id, employee name/code, reason và leave type; filter theo status và type. Cancel xuất hiện chỉ với request Pending của user hiện tại.

**Đề xuất:** thay window.alert hiện dùng cho “Xem chi tiết” bằng modal/drawer gồm request reason, dates, approver, status và decision note. Export button hiện là placeholder.

### 4.4. Tạo đơn nghỉ phép — /request

**Mục tiêu:** nhập loại nghỉ, khoảng thời gian, lý do; xem số ngày ước tính và số dư trước khi gửi.

    ┌──────────────────────────────────────────────┬─────────────────────────────┐
    │ NGHỈ PHÉP                [Đơn nghỉ của tôi]  │ TÓM TẮT YÊU CẦU              │
    │ Tạo đơn nghỉ phép                          │ Loại nghỉ                    │
    ├──────────────────────────────────────────────┤ Ngày bắt đầu                 │
    │ 01  THÔNG TIN NGHỈ PHÉP                    │ Ngày kết thúc                │
    │ [ANNUAL card / radio]                      │ Thời lượng                   │
    │ [SICK card / radio]                        │ Người duyệt                  │
    │ [UNPAID card / radio]                      │ Tổng số ngày ước tính        │
    │                                             ├─────────────────────────────┤
    │ 02  THỜI GIAN NGHỈ                         │ SỐ DƯ PHÉP HIỆN CÓ          │
    │ [Ngày bắt đầu]       [Ngày kết thúc]       │ Available / Entitled         │
    │                                             │ Used / Pending               │
    │ [Preview: n ngày làm việc - ƯỚC TÍNH]       │ Progress                     │
    │                                             ├─────────────────────────────┤
    │ 03  BỔ SUNG THÔNG TIN                      │ Lưu ý: backend xác minh lại │
    │ Lý do nghỉ *                               │ ngày làm việc, overlap, số dư│
    │ [Textarea................................] │                             │
    │ 0/600                                       │                             │
    │                                             │                             │
    │ [Hủy]                            [Gửi]     │                             │
    └──────────────────────────────────────────────┴─────────────────────────────┘

Client validation đang có:
- Ngày bắt đầu/kết thúc từ hôm nay trở đi.
- Ngày kết thúc không trước ngày bắt đầu.
- Ít nhất một ngày làm việc (estimate bỏ cuối tuần và holidays).
- Lý do bắt buộc, UI max length 600.
- Không cho gửi nếu vượt balance đang hiển thị.

Backend authoritative validation:
- Range tối đa 366 ngày.
- Lý do bắt buộc, tối đa 1000 ký tự.
- Employee active; leave type active.
- Backend tự tính ngày làm việc, bỏ cuối tuần và holidays.
- Không cho overlap với request Pending hoặc Approved.
- Với leave type deductsBalance, kiểm tra quota theo từng năm; request qua năm được phân bổ riêng.
- Request mới trạng thái Pending; ApproverId snapshot lấy từ ManagerId của employee trong HR Service.

**Mismatch quan trọng:** request summary đang hard-code người duyệt theo role. Backend thực tế dùng ManagerId của employee hiện tại cho mọi role. Nếu ManagerId null, HR có thể xử lý. Nên lấy approver thật từ employee profile/API; nếu chưa có thì dùng label “Tuyến phê duyệt sẽ được xác định khi gửi”.

Text “ƯỚC TÍNH” và note backend tính lại phải được giữ lại.

### 4.5. Hộp thư phê duyệt — /approvals

**Mục tiêu:** Manager/HR xem request pending được giao, kiểm tra lý do, approve/reject với optional note.

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ QUY TRÌNH ĐỘI NHÓM                                      [n đơn chờ duyệt]   │
    │ Hộp thư phê duyệt                                                         │
    ├───────────────────┬───────────────────┬───────────────────────────────────┤
    │ Chờ bạn xem xét   │ Đơn đã duyệt      │ Tổng số đơn                       │
    ├────────────────────────────────────────────────────────────────────────────┤
    │ [Search nhân viên / loại nghỉ...]                     [✓ Chỉ đơn chờ duyệt]│
    ├────────────────────────────────────────────────────────────────────────────┤
    │ [Avatar] Employee Name  CODE     [Chờ duyệt]                               │
    │         Reason summary                                                     │
    │         Leave type · Start → End · n ngày                 [Xem xét đơn]     │
    │         Gửi ngày ...                                                       │
    ├────────────────────────────────────────────────────────────────────────────┤
    │ [Approval card...]                                                         │
    └────────────────────────────────────────────────────────────────────────────┘

Modal xem xét:

    ┌──────────────────────── XEM XÉT ĐƠN NGHỈ PHÉP ──────────────────────────────┐
    │ Request ID                                                           [X]   │
    │ [Avatar] Employee · Department · Employee code                             │
    │ Loại nghỉ                                              n ngày               │
    │ Start date — End date                                                       │
    │ Lý do nghỉ: ...                                                             │
    │ Ghi chú phê duyệt (không bắt buộc)                                          │
    │ [Textarea.............................................................]    │
    │ [Từ chối]                                                   [Phê duyệt]    │
    └────────────────────────────────────────────────────────────────────────────┘

Quyền và dữ liệu:
- Employee không có route này.
- Manager inbox lấy Pending request thuộc direct reports theo reporting line hiện tại (Leave Service gọi HR Service).
- HR inbox lấy tất cả Pending.
- Backend cấm tự approve/reject request của chính mình.
- Manager chỉ xử lý request khi hiện tại là ManagerId của employee; HR được xử lý tất cả.
- Chỉ request Pending được quyết định.

UI hiện có: search, toggle Pending-only, summary cards, modal có note optional, approve/reject, toast và reload.

### 4.6. Lịch nghỉ của nhóm — /calendar

    ┌───────────────────────────────────────────────────┬────────────────────────┐
    │ LẬP KẾ HOẠCH NHÓM            [+ Tạo đơn nghỉ]     │ NGÀY ĐÃ CHỌN           │
    │ Lịch nghỉ của nhóm                                │ {weekday, date}         │
    ├───────────────────────────────────────────────────┤ Holiday                 │
    │ < THÁNG NĂM > [Hôm nay]                            │ Person · Leave type     │
    │ Legend: Đã duyệt / Chờ duyệt / Ngày lễ             │ Status dot              │
    │                                                   ├────────────────────────┤
    │ T2    T3    T4    T5    T6    T7    CN             │ SẮP TỚI                │
    │ ┌────┬────┬────┬────┬────┬────┬────┐               │ Date · Person · Status  │
    │ │ 01 │ 02 │ 03 │ 04 │ 05 │ 06 │ 07 │               │                        │
    │ │... │... │... │... │... │... │... │               │                        │
    │ └────┴────┴────┴────┴────┴────┴────┘               │                        │
    └───────────────────────────────────────────────────┴────────────────────────┘

- Có tháng trước/sau và nút Hôm nay.
- Chọn ngày để xem request và holiday.
- Calendar hiển thị request Pending/Approved và holidays.
- Upcoming list tối đa 5 request từ dữ liệu đang tải.
- API scope: HR toàn tổ chức; non-HR thấy request bản thân hoặc request có approver snapshot trỏ tới họ.
- Calendar manager scope hiện không giống Approval Inbox: inbox dựa trên reporting line hiện tại; calendar dựa trên approver snapshot. Cần quyết định scope sản phẩm và đồng bộ.
- Mô tả “Lịch nghỉ của nhóm” có thể gây hiểu nhầm nếu user không phải HR và dữ liệu không bao gồm toàn bộ team theo reporting line.

### 4.7. Nhân viên — /employees (HR only)

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ QUẢN TRỊ                                            [Xuất danh sách] [+ Add]│
    │ Nhân viên                                                                  │
    ├─────────────────┬─────────────────┬─────────────────┬──────────────────────┤
    │ Tổng nhân viên  │ Active          │ Manager         │ Phòng ban            │
    ├────────────────────────────────────────────────────────────────────────────┤
    │ [Search name/email/code] [Role ▼] [Status ▼]                              │
    ├────────────┬─────────┬──────────┬───────────┬──────────┬─────────┬──────────┤
    │ Nhân viên  │ Mã NV   │ Phòng ban│ Vai trò   │ Quản lý  │ Hire date│ Status  │
    ├────────────┼─────────┼──────────┼───────────┼──────────┼─────────┼──────────┤
    │ Avatar     │ EMP001  │ Kỹ thuật │ Employee  │ Manager  │ Date    │ Active  │ [Edit] [Off]
    └────────────┴─────────┴──────────┴───────────┴──────────┴─────────┴──────────┘

Create/edit modal fields:
- Code, full name, email, role, department, hire date, direct manager.
- Create only: temporary password required, 12–30 characters.
- Edit: password field is not shown.
- Manager dropdown only includes active Manager records and excludes currently edited employee.
- Deactivate action requires confirmation and refreshes list.

HR API also supports reset employee password (PUT /api/v1/employees/{id}/password), but current UI does not expose it. Export button is a toast placeholder.

Deactivation is a soft account disable, not delete. Keep employee row and show “Đã vô hiệu hóa”.

### 4.8. Phòng ban — /departments (HR only)

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ QUẢN TRỊ                                                   [+ Thêm phòng ban]│
    │ Phòng ban                                                                  │
    ├─────────────────┬────────────────────────┬────────────────────────────────┤
    │ Tổng phòng ban  │ Nhân viên active       │ Nhân viên chưa phân phòng       │
    ├─────────────────┴────────────────────────┴────────────────────────────────┤
    │ ┌────────────────────┐ ┌────────────────────┐ ┌───────────────────────────┐│
    │ │ [Building] [Edit]   │ │ [Building] [Edit]   │ │ [Building] [Edit]          ││
    │ │ Department name    │ │ Department name    │ │ Department name           ││
    │ │ Department id      │ │ Department id      │ │ Department id             ││
    │ │ n employees        │ │ n employees        │ │ n employees               ││
    │ │ Avatar stack       │ │ Avatar stack       │ │ Avatar stack              ││
    │ │                 Del│ │                 Del│ │                       Del ││
    │ └────────────────────┘ └────────────────────┘ └───────────────────────────┘│
    └────────────────────────────────────────────────────────────────────────────┘

- Cards hiển thị tên, ID, số nhân viên và avatar stack.
- Create/edit modal chỉ có tên phòng ban.
- Delete yêu cầu xác nhận; lỗi constraint từ API phải được hiển thị cho người dùng.
- Tên phòng ban phải unique theo schema/domain.

### 4.9. Chính sách nghỉ phép — /policies (HR only)

Tab Loại nghỉ & hạn mức:

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ QUẢN TRỊ  Chính sách nghỉ phép                                             │
    ├────────────────────────────────────────────────────────────────────────────┤
    │ [Loại nghỉ & hạn mức (n)] [Ngày nghỉ lễ (n)]                              │
    ├──────────────────────┬─────────────────────┬───────────────────────────────┤
    │ [Icon] Active        │ [Icon] Active        │ [Icon] Active                 │
    │ Nghỉ phép năm        │ Nghỉ ốm              │ Nghỉ không lương              │
    │ Description          │ Description          │ Description                   │
    │ 12 ngày / năm        │ 10 ngày / năm        │ Không giới hạn                │
    │ Hưởng lương          │ Hưởng lương          │ Không hưởng lương             │
    │ Trừ số dư            │ Trừ số dư            │ Không trừ số dư               │
    │ [Sửa chính sách]     │ [Sửa chính sách]     │ [Sửa chính sách]              │
    └──────────────────────┴─────────────────────┴───────────────────────────────┘

Policy edit modal:
- Tên hiển thị.
- Days per year (disabled khi không deduct balance).
- Paid checkbox; UNPAID bị khóa false.
- Deducts balance checkbox.
- Is active checkbox.
- Save.

Tab Ngày nghỉ lễ:

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ Lịch nghỉ lễ                                              [+ Thêm ngày lễ]│
    │ Ngày lễ không được tính vào ngày làm việc khi tạo đơn                     │
    ├───────────────────────────┬──────────────┬──────────┬───────────────────────┤
    │ Ngày lễ                   │ Ngày         │ Thứ      │ Action                │
    ├───────────────────────────┼──────────────┼──────────┼───────────────────────┤
    │ Tết Dương lịch            │ 01/01/2027   │ Thứ ...  │ Edit / Delete          │
    └───────────────────────────┴──────────────┴──────────┴───────────────────────┘

Holiday modal: tên ngày lễ + ngày. Create/edit/delete đã được nối frontend HR API.

**API/UI gap:** HR API có endpoint tạo Leave Type (POST /api/v1/leave-types) nhưng Policies UI không có flow tạo mới. Service hiện chọn current year; không có year selector trên UI. Nên thêm year selector trước khi phát triển cấu hình nhiều năm.

### 4.10. Hồ sơ của tôi — /profile

    ┌────────────────────────────────────────────────────────────────────────────┐
    │ TÀI KHOẢN CỦA BẠN                                                          │
    │ Hồ sơ của tôi                                                              │
    ├────────────────────────────────────┬───────────────────────────────────────┤
    │ PROFILE CARD                       │ ĐỔI MẬT KHẨU                          │
    │ Cover                              │ Current password [********]          │
    │ Avatar  Full name    [Role badge]  │ New password     [********]          │
    │         Email                      │ Confirm password [********]          │
    │                                    │ Password rule: 12–30 chars            │
    │ Email                              │ [Cập nhật mật khẩu]                   │
    │ Employee code                      │                                       │
    │ Department                         │ Security footnote                     │
    │ Hire date                          │                                       │
    │ Manager                            │                                       │
    │ Account active status              │                                       │
    └────────────────────────────────────┴───────────────────────────────────────┘

- Profile data read-only.
- Change password requires current password, new password and confirmation.
- Frontend validates length 12–30 and matching confirmation.
- Demo mode intentionally does not persist password change and returns explanatory error.
- API mode calls HR PUT /api/v1/me/password.

## 5. Luồng nghiệp vụ chính

### 5.1. Gửi đơn nghỉ

1. Người dùng mở Create Leave.
2. Frontend tải leave types đang hoạt động, balance và holidays.
3. Người dùng chọn loại nghỉ, ngày bắt đầu/kết thúc và nhập lý do.
4. Client ước tính workdays (loại weekends/holidays).
5. Client validate và gửi POST request.
6. Leave Service kiểm tra employee/type/holidays/policy từ HR Service.
7. Backend kiểm tra overlap và balance trong transaction.
8. Tạo request Pending, history và các yearly allocation tương ứng.
9. Frontend hiển thị thành công và điều hướng về My Leaves.

### 5.2. Approve hoặc reject

1. Manager/HR mở approval inbox.
2. Frontend gọi pending-approval.
3. Người duyệt mở modal xem employee, dates, type, reason.
4. Có thể nhập optional note.
5. Gửi approve hoặc reject.
6. Backend xác thực role, quyền trên employee, trạng thái Pending và chống tự duyệt.
7. Cập nhật status, balance, history.
8. Frontend toast kết quả và reload inbox.

### 5.3. Cancel request

Employee mở My Leaves → action chỉ hiển thị với request Pending của chính mình → xác nhận → backend kiểm tra ownership/status → chuyển trạng thái Cancelled và giải phóng pending balance.

## 6. API-to-screen traceability

| Màn hình / thao tác | Service/controller | Endpoint |
|---|---|---|
| Login | HR AuthenticationController | POST /api/v1/auth/login |
| Refresh session | HR AuthenticationController | POST /api/v1/auth/refresh |
| Logout | HR AuthenticationController | POST /api/v1/auth/logout |
| Current employee | HR CurrentEmployeeController | GET /api/v1/me |
| Team | HR CurrentEmployeeController | GET /api/v1/me/team |
| Change own password | HR CurrentEmployeeController | PUT /api/v1/me/password |
| Employee list/detail/create/update/deactivate | HR EmployeesController | GET/POST/PUT /api/v1/employees; POST /{id}/deactivate |
| Reset employee password | HR EmployeesController | PUT /api/v1/employees/{id}/password |
| Departments | HR DepartmentsController | GET/POST/PUT/DELETE /api/v1/departments |
| Leave types | HR LeaveTypesController | GET/POST/PUT /api/v1/leave-types |
| Leave policies | HR LeavePoliciesController | GET/POST/PUT /api/v1/leave-policies |
| Holidays | HR HolidaysController | GET/POST/PUT/DELETE /api/v1/holidays |
| Leave requests list/detail/create/cancel | Leave LeaveRequestsController | GET /api/v1/leave-requests; GET /{id}; POST; POST /{id}/cancel |
| My requests | Leave LeaveRequestsController | GET /api/v1/leave-requests/mine |
| Calendar | Leave LeaveRequestsController | GET /api/v1/leave-requests/calendar?from=&to= |
| Approval inbox | Leave LeaveRequestsController | GET /api/v1/leave-requests/pending-approval |
| Approve/reject | Leave LeaveRequestsController | POST /{id}/approve; POST /{id}/reject |
| My balance | Leave LeaveBalancesController | GET /api/v1/leave-balances/mine?year= |

Endpoints are relative to each service's API base URL.

## 7. Gaps và thứ tự đề xuất

| Ưu tiên | Phát hiện từ source | Đề xuất |
|---|---|---|
| P0 | HR dashboard gắn label số dư toàn tổ chức nhưng service chỉ lấy balance của HR hiện tại | Đổi thành “Số dư phép của tôi” hoặc bỏ card khỏi HR org dashboard |
| P0 | Request summary đoán approver dựa role; backend dùng ManagerId snapshot | Hiển thị approver thực tế hoặc nhãn trung tính |
| P0 | Manager pending KPI đếm visible pending, có thể gồm request của chính manager | Dùng approvals inbox count |
| P1 | Donut bỏ Cancelled nhưng center total gồm tất cả request | Thêm slice Cancelled hoặc sửa mẫu số |
| P1 | Xem chi tiết request dùng window.alert | Thay bằng accessible modal/drawer |
| P1 | Export buttons chỉ toast báo API chưa có | Ẩn/disable cho tới khi có endpoint hoặc triển khai export |
| P1 | Notification bell không có click handler | Ẩn hoặc làm rõ trạng thái “Chưa có thông báo” đến khi có notification backend |
| P1 | HR mở được /request nhưng nav không có mục Create Leave | Chốt chính sách HR self-request và đồng bộ nav/route/CTA |
| P1 | UI không thể tạo Leave Type dù API có POST | Thêm create leave type flow |
| P1 | API reset password employee có nhưng UI thiếu | Thêm action HR reset password nếu nằm trong scope |
| P1 | “Cài đặt” điều hướng tới /policies | Đổi nhãn hoặc tạo Settings route |
| P2 | Bảng chưa có pagination | Thêm khi lượng dữ liệu lớn |
| P2 | Calendar manager scope không như approval inbox | Chốt scope theo current reporting line hay approver snapshot rồi đồng bộ |
| P2 | Reason UI max 600 nhưng backend nhận 1000 | Đồng bộ giới hạn hoặc ghi rõ lý do UI giới hạn ngắn hơn |
| P2 | Text “nhóm/tổ chức” không luôn khớp scope API | Dùng mô tả theo role/scope |

## 8. Acceptance checklist

### Global
- [ ] Navigation khác nhau theo HR, Manager, Employee và khớp route guard.
- [ ] Sidebar có active, collapsed, mobile drawer và keyboard states.
- [ ] Focus-visible, labels và accessible names cho interactive controls.
- [ ] Data page có loading, empty, error và forbidden states.

### Leave flow
- [ ] Client label số ngày là ước tính.
- [ ] Server validation errors được hiển thị rõ.
- [ ] Pending/Approved/Rejected/Cancelled được thể hiện nhất quán.
- [ ] Chỉ request Pending của chính user mới có thể cancel.
- [ ] Manager chỉ thấy inbox thuộc scope; HR thấy toàn bộ pending.
- [ ] Không gửi trùng approve/reject khi đang submit.

### HR administration
- [ ] Create/edit employee validate required fields.
- [ ] Deactivated employee còn trong danh sách và có inactive status.
- [ ] Department uniqueness và delete conflict có thông báo rõ.
- [ ] Policy year rõ ràng trước khi quản lý multi-year.
- [ ] Holiday duplicate date được báo dễ hiểu.

### Data mode
- [ ] Demo badge và demo credential panel chỉ có trong demo mode.
- [ ] API mode không silent fallback về mock data.
- [ ] 401, 403, 409, timeout và network errors có feedback.

## 9. Source map

| Source | Nội dung định nghĩa |
|---|---|
| src/web/src/App.tsx | Routes và role guards |
| src/web/src/components/AppShell.tsx | Role-specific sidebar, shell, profile menu |
| src/web/src/pages/*.tsx | Screen UI và interaction |
| src/web/src/services/hrService.ts | HR frontend API calls |
| src/web/src/services/leaveService.ts | Leave frontend API calls |
| src/web/src/services/mockStore.ts | Demo seed và localStorage |
| src/web/src/services/config.ts | Demo/API mode và API base URLs |
| src/hr-service/HrService.Api/Controllers | HR endpoint và role authorization |
| src/leave-service/LeaveService.Api/Controllers | Leave request/balance authorization |
| src/leave-service/LeaveService.Application/Services/LeaveRequestService.cs | Server-side validation và approval rules |
| src/leave-service/LeaveService.Infrastructure/Persistence/LeaveRequestRepository.cs | Visibility, overlap và balance persistence |
| src/web/src/styles/global.css | Visual tokens, layout, components, responsive rules |
