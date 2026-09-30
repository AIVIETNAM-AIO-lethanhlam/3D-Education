# Cập nhật: Role Admin & Kiểm duyệt nội dung (2026-09-24)

## 1. Đăng nhập Admin
- Màn hình đăng nhập → chọn tab **Giáo viên** → username `adminadmin1` (hoặc `adminadmin1@admin.local`) → mật khẩu do chủ dự án quản lý.
- Client tự thêm `@admin.local` khi nhập username không có "@". Tài khoản có `profiles.role = 'admin'` → vào **AdminScene** (Splash cũng tự đưa admin về AdminScene).

## 2. Quy tắc kiểm duyệt
| Loại báo cáo | Admin có thể | Kết quả |
|---|---|---|
| Tin nhắn chat | Bác bỏ / Gửi cảnh cáo | Chỉ gửi thông báo cảnh cáo. **Không khóa, không xóa tài khoản. Tin nhắn không bị che/xóa.** |
| Bài học / Quiz / Model 3D | Bác bỏ / Yêu cầu giải trình | Nội dung tạm ẩn với học sinh → giáo viên giải trình → Hợp lệ: hiện lại · Không hợp lệ: **xóa nội dung** (không ảnh hưởng tài khoản). |

## 3. Database (Supabase – đã áp dụng)
- Migration `admin_moderation_schema`: `is_admin()`, cột `moderation_hidden` (lessons, quizzes, lesson_assets), bảng `notifications`, `chat_reports`, `content_reports`, `moderation_cases` + RLS (nội dung đang ẩn không hiển thị với học sinh, admin đọc được tất cả).
- Migration `admin_moderation_rpcs`: `submit_chat_report`, `submit_content_report`, `admin_dashboard_counts`, `admin_resolve_chat_report`, `admin_list_content_queue`, `admin_get_content_detail`, `admin_dismiss_content_reports`, `admin_request_explanation`, `admin_decide_case`, `teacher_submit_explanation`, `mark_notifications_read`.
- Báo cáo chat lưu **snapshot 30 tin nhắn gần nhất** để admin xem; tin nhắn gốc giữ nguyên.

## 4. Unity
**Scene mới** (đã thêm vào Build Settings): `AdminScene`, `NotificationScene`.

**File mới**
- `Scripts/Supabase/Moderation/SupabaseModerationModels.cs`, `SupabaseModerationService.cs`
- `Scripts/Moderation/ModerationReportSheet.cs` (sheet báo cáo nội dung, toast), `AdminUI.cs` (helper UI)
- `Resources/Moderation/ModerationUI.uss`
- `UI/Admin/AdminPage.uxml|uss`, `AdminPageController.cs` – 4 tab: Tổng quan · Báo cáo chat · Nội dung · Cài đặt
- `UI/Notification/NotificationPage.uxml`, `NotificationPageController.cs` – thông báo + form giải trình của giáo viên

**File sửa**
- `SupabaseSession`, `SupabaseProfileService`: nhận role `admin`.
- `AuthPageController`, `SplashPageController`: điều hướng admin.
- `ChatPageController`: "Báo cáo người dùng" + chọn lý do + mô tả → `submit_chat_report`.
- `ShowLessonPageController`, `StartQuizPageController`, `Mode3DPageController`: nút báo cáo (ẩn với giáo viên & admin).
- `ClassDetailPageController(.uss)`: giáo viên thấy nhãn "Bị báo cáo · tạm ẩn" + nút "Giải trình".
- `MainHomePageController`, `GeneralHeaderController`: chấm đỏ thông báo chưa đọc, chuông → NotificationScene.
- `SupabaseLessonModels`: `LessonRecord.moderation_hidden`.

Bản sao lưu file cũ: `_claude_backup_20260925_admin/`.

## 5. Icon
Không tải được từ Flaticon (mạng bị chặn trong môi trường làm việc), nên dùng lại icon có sẵn trong `UI/Images/Icons`:
alert, warning, notification, explain, check, check-white, delete, message, document, cube, video, home, settings, logout, back, chevron-right, globe, eye, book.

## 6. Lưu ý
- Cần mở Unity để compile & test (chỉ mới kiểm tra cú pháp).
- Khi admin xóa model/tài liệu, bản ghi DB bị xóa nhưng **file trên R2 chưa bị xóa**.
- Figma: sửa tay 2 chỗ — tiêu đề sheet R3 → Hug; khung giữa R4 → Fill.

## 7. Kịch bản test nhanh
1. Học sinh báo cáo một bài học (ShowLesson → nút báo cáo đỏ).
2. Admin đăng nhập → tab Nội dung → Báo cáo mới → mở → *Yêu cầu giải trình*.
3. Học sinh: bài học biến mất. Giáo viên: chuông có chấm đỏ → Thông báo → *Gửi giải trình*.
4. Admin → Nội dung → Cần duyệt → *Hợp lệ – giữ lại* (bài hiện lại) hoặc *Không hợp lệ – xóa*.
5. Chat: người dùng A báo cáo B → Admin tab Báo cáo chat → *Gửi cảnh cáo* → B nhận thông báo; tin nhắn trong chat không đổi.
