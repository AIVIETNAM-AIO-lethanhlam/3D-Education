# Cập nhật tính năng – 24/09/2026

Bổ sung các tính năng còn thiếu (bỏ qua "tham gia lớp bằng mã lớp" theo yêu cầu).
Bản gốc của mọi file C# đã sửa nằm trong `_claude_backup_20260924/` (đã thêm vào .gitignore).

## 1. Bảo mật
**Database (Supabase migrations):**
- `class_members`: bỏ 3 policy INSERT quá lỏng. Học sinh chỉ tự thêm chính mình với role student:
  lớp public → `enrolled`/`pending`, lớp private → chỉ `pending`. Thêm policy UPDATE (xin lại sau khi bị từ chối) và UPDATE/DELETE cho giáo viên của lớp.
- `classes`: chỉ tài khoản có `profiles.role = teacher` mới tạo được lớp.
- `profiles`: người dùng sửa được hồ sơ của mình, nhưng **không đổi được role**
  (trigger `prevent_profile_role_change`). Riêng tài khoản Google lần đầu được chọn role một lần (`choose_initial_role`, cột `role_selected_at`).
- `lessons`: học sinh chỉ thấy bài học `published`, giáo viên thấy cả bản nháp.
- `quizzes`, `quiz_questions`, `get_quiz_options_for_student`: chỉ giáo viên hoặc học sinh đã tham gia lớp mới đọc được.
- `model_parts`: chỉ giáo viên của bài học mới sửa được (bỏ policy "for testing").
- Thu hồi quyền `anon` trên các hàm nội bộ và đặt `search_path` cho 2 hàm trigger.

**Edge Functions:**
- MỚI `r2-upload-url`: trả về presigned PUT URL (15 phút). Kiểm tra: đúng giáo viên, key bắt đầu bằng user id và giáo viên dạy lớp đó.
- `r2-signed-url`: người dùng phải gửi `asset_id`; quyền đọc do RLS của `lesson_assets` quyết định.
- `process-model-detail`: chỉ nhận secret của trigger (env hoặc Vault) hoặc service role. Đây cũng là lỗi cũ khiến 2 model bị kẹt ở `processing`; cả 2 đã được xử lý lại.
- `generate-model-details`: chỉ giáo viên của bài học (hoặc backend) mới gọi được. Model dự phòng đổi sang Gemini 3.5 (2.5 đã ngừng).

**Unity:**
- `CloudflareR2StorageService.cs` không còn chứa R2 key; upload qua `r2-upload-url`. Đã xoá key khỏi `CreateLessonScene.unity`.
- Đăng nhập (email + Google) lấy role từ `profiles`, không tin `user_metadata`.

## 2. Lớp học & bài học
- ClassDetail → tab Student List (giáo viên): mục **Yêu cầu tham gia** có nút Duyệt/Từ chối (RPC `respond_join_request`).
- ClassDetail → chế độ Edit: mỗi bài học có nút **Đăng / Ẩn** (published ↔ draft) và nhãn "Bản nháp".
- Điểm trung bình thật cho giáo viên (trước đây luôn hiện "100%").
- MyClasses: thẻ giáo viên hiện đúng số học sinh (+ số yêu cầu chờ duyệt), số học phần và điểm TB (`teacher_class_overview`). Thẻ học sinh hiện tiến độ thật từ `lesson_progress`.
- Sửa các view dùng sai trạng thái `active` → `enrolled`.

## 3. Tài khoản & phiên
- Tự đăng nhập ở Splash (refresh token; tôn trọng tuỳ chọn "Ghi nhớ đăng nhập").
- `SupabaseTokenRefresher` + `SupabaseSessionKeeper`: tự làm mới token trước khi hết hạn, khi mở lại app, và thử lại một lần khi gặp 401.
- UserInfo lưu họ tên và ngày sinh vào `profiles`, đổi mật khẩu qua Supabase Auth. Không còn lưu mật khẩu trong PlayerPrefs; email chỉ đọc.
- Nút Register ở Home mở AuthScene đúng tab Đăng ký.
- Sửa lỗi điều hướng 2 lần của thanh điều hướng dưới (SceneHistory chặn load trùng).

## 4. AI & 3D
- MỚI `generate-lesson-content`: nút "Generate with AI" ở CreateLesson sinh mô tả + 3 mục tiêu bằng Gemini (vi/en). Nếu AI lỗi thì điền nội dung mẫu như cũ.
- `ai-chat` nhận `history`: Chat AI gửi tối đa 12 lượt trước để hiểu câu hỏi nối tiếp.
- ClassDetail → 3D Labs: hiện trạng thái phân tích cấu trúc; giáo viên có nút "Phân tích lại bằng AI".
- CreateClass: chọn ảnh bìa trên Android/iOS (NativeFilePicker) và upload lên bucket public `class-covers`. Ảnh bìa hiện trên thẻ MyClasses.

## Cần làm khi mở Unity
1. Mở project, chờ compile, xem Console. Mình không compile được Unity từ đây, chỉ kiểm tra được cú pháp.
2. Test theo luồng:
   - Đăng nhập, rồi tắt và mở lại app (auto-login).
   - Tạo lớp có ảnh bìa.
   - Tạo bài học: dùng AI và upload GLB/PDF.
   - Học sinh xin vào lớp private, giáo viên duyệt.
   - Ẩn/đăng bài học; chat AI hỏi nối tiếp; sửa hồ sơ và đổi mật khẩu.
3. **Nên tạo lại (rotate) R2 Access Key** trên Cloudflare vì key cũ từng nằm trong scene/git. Cập nhật key mới vào Supabase secrets (`R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`).
4. Sau khi test ổn thì xoá thư mục `_claude_backup_20260924/`, vì trong đó còn bản scene cũ chứa key.

## Lưu ý
- VR: cờ "TEST ONLY" `allowAnyLoggedInUserForTesting` vẫn cho học sinh bấm đặt anchor, nhưng DB giờ chỉ cho giáo viên lưu.
- Gemini 3.6-flash đôi lúc báo hết quota/quá tải. Khi đó hệ thống dùng 3.5-flash, hoặc giữ cấu trúc lấy từ file GLB.
- Nên bật "Leaked password protection" trong Supabase Auth.
