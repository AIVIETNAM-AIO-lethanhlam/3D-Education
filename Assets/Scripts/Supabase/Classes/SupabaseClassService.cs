using System;
using System.Collections;
using UnityEngine;

public static class SupabaseClassService
{
    public static IEnumerator CreateClass(
        string className,
        string description,
        string classCode,
        string visibility,
        string coverTemplate,
        string coverImageUrl,
        string categoryId,
        Action<SupabaseClass> onSuccess,
        Action<string> onError)
    {
        if (!ValidateTeacherSession(onError))
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(className))
        {
            onError?.Invoke("Class name không được để trống.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(classCode))
        {
            onError?.Invoke("Class code không được để trống.");
            yield break;
        }

        if (!IsValidVisibility(visibility))
        {
            onError?.Invoke("visibility chỉ nhận giá trị public hoặc private.");
            yield break;
        }

        string json;

        // category_id là UUID. Không được gửi category_id = "".
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            CreateClassWithoutCategoryRequest payload =
                new CreateClassWithoutCategoryRequest
                {
                    teacher_id = SupabaseSession.UserId,
                    class_name = className.Trim(),
                    description = description?.Trim() ?? string.Empty,
                    class_code = classCode.Trim().ToUpperInvariant(),
                    visibility = NormalizeVisibility(visibility),
                    cover_image_url = coverImageUrl?.Trim() ?? string.Empty,
                    cover_template = coverTemplate?.Trim() ?? string.Empty
                };

            json = JsonUtility.ToJson(payload);
        }
        else
        {
            if (!Guid.TryParse(categoryId.Trim(), out _))
            {
                onError?.Invoke("category_id không đúng định dạng UUID.");
                yield break;
            }

            CreateClassRequest payload =
                new CreateClassRequest
                {
                    category_id = categoryId.Trim(),
                    teacher_id = SupabaseSession.UserId,
                    class_name = className.Trim(),
                    description = description?.Trim() ?? string.Empty,
                    class_code = classCode.Trim().ToUpperInvariant(),
                    visibility = NormalizeVisibility(visibility),
                    cover_image_url = coverImageUrl?.Trim() ?? string.Empty,
                    cover_template = coverTemplate?.Trim() ?? string.Empty
                };

            json = JsonUtility.ToJson(payload);
        }

        Debug.Log("Create class payload gửi lên Supabase:\n" + json);

        yield return SupabaseRestService.Post(
            "classes?select=*",
            json,
            responseJson =>
            {
                if (!TryParseFirstClass(
                        responseJson,
                        out SupabaseClass createdClass,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(createdClass);
            },
            onError,
            true);
    }

    public static IEnumerator GetTeacherClasses(
        Action<SupabaseClass[]> onSuccess,
        Action<string> onError)
    {
        if (!ValidateTeacherSession(onError))
        {
            yield break;
        }

        string teacherId = Uri.EscapeDataString(SupabaseSession.UserId);

        string query =
            "classes" +
            $"?teacher_id=eq.{teacherId}" +
            "&select=*" +
            "&order=created_at.desc";

        yield return SupabaseRestService.Get(
            query,
            json =>
            {
                if (!TryParseClasses(
                        json,
                        out SupabaseClass[] classes,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(classes);
            },
            onError);
    }


    public static IEnumerator GetStudentEnrolledClasses(
        Action<StudentEnrolledClass[]> onSuccess,
        Action<string> onError)
    {
        if (!ValidateStudentSession(onError))
        {
            yield break;
        }

        string studentId = Uri.EscapeDataString(SupabaseSession.UserId);
        string query =
            "student_enrolled_classes_view" +
            $"?student_id=eq.{studentId}" +
            "&select=*" +
            "&order=joined_at.desc";

        yield return SupabaseRestService.Get(
            query,
            json =>
            {
                if (!TryParseStudentClasses(
                        json,
                        out StudentEnrolledClass[] classes,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(classes);
            },
            onError);
    }

    public static IEnumerator GetStudentActiveQuizzes(
        Action<StudentActiveQuiz[]> onSuccess,
        Action<string> onError)
    {
        if (!ValidateStudentSession(onError))
        {
            yield break;
        }

        string studentId = Uri.EscapeDataString(SupabaseSession.UserId);
        string query =
            "student_active_quizzes_view" +
            $"?student_id=eq.{studentId}" +
            "&select=*" +
            "&order=closes_at.asc";

        yield return SupabaseRestService.Get(
            query,
            json =>
            {
                if (!TryParseStudentQuizzes(
                        json,
                        out StudentActiveQuiz[] quizzes,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(quizzes);
            },
            onError);
    }


    public static IEnumerator GetClassDetailStats(
        string classId,
        Action<ClassDetailStats> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        if (!Guid.TryParse(classId, out _))
        {
            onError?.Invoke("classId không đúng định dạng UUID.");
            yield break;
        }

        string escapedClassId = Uri.EscapeDataString(classId.Trim());
        string query =
            "class_detail_stats_view" +
            $"?class_id=eq.{escapedClassId}" +
            "&select=*" +
            "&limit=1";

        yield return SupabaseRestService.Get(
            query,
            json =>
            {
                if (!TryParseClassDetailStats(
                        json,
                        out ClassDetailStats stats,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(stats);
            },
            onError);
    }

    public static IEnumerator GetClassEnrolledStudents(
        string classId,
        Action<ClassMemberStudent[]> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        if (!Guid.TryParse(classId, out _))
        {
            onError?.Invoke("classId không đúng định dạng UUID.");
            yield break;
        }

        string escapedClassId = Uri.EscapeDataString(classId.Trim());

        // The embedded profiles relation comes from:
        // class_members.user_id -> profiles.id
        string query =
            "class_members" +
            $"?class_id=eq.{escapedClassId}" +
            "&member_role=eq.student" +
            "&status=eq.enrolled" +
            "&select=id,class_id,user_id,member_role,status,joined_at," +
            "profiles(full_name,avatar_url,role)" +
            "&order=joined_at.asc";

        yield return SupabaseRestService.Get(
            query,
            json =>
            {
                if (!TryParseClassMembers(
                        json,
                        out ClassMemberStudent[] students,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(students);
            },
            onError);
    }

    public static IEnumerator GetUserPresence(
        string userId,
        Action<UserPresenceRecord> onSuccess,
        Action<string> onError)
    {
        if (!Guid.TryParse(userId, out _))
        {
            onError?.Invoke("userId không đúng định dạng UUID.");
            yield break;
        }

        string escapedUserId = Uri.EscapeDataString(userId.Trim());

        yield return SupabaseRestService.Get(
            "user_presence" +
            $"?user_id=eq.{escapedUserId}" +
            "&select=user_id,is_online,last_seen_at,updated_at" +
            "&limit=1",
            json =>
            {
                string trimmed = json?.Trim() ?? string.Empty;

                try
                {
                    string wrapped = "{\"items\":" + trimmed + "}";
                    UserPresenceRecordArrayWrapper wrapper =
                        JsonUtility.FromJson<UserPresenceRecordArrayWrapper>(wrapped);

                    UserPresenceRecord result =
                        wrapper?.items != null && wrapper.items.Length > 0
                            ? wrapper.items[0]
                            : null;

                    onSuccess?.Invoke(result);
                }
                catch (Exception exception)
                {
                    onError?.Invoke(
                        "Không parse được trạng thái online: " +
                        exception.Message
                    );
                }
            },
            onError);
    }



    public static IEnumerator UnenrollStudent(
        string classId,
        Action onSuccess,
        Action<string> onError)
    {
        if (!ValidateStudentSession(onError))
        {
            yield break;
        }

        if (!Guid.TryParse(classId, out _))
        {
            onError?.Invoke("classId không đúng định dạng UUID.");
            yield break;
        }

        string escapedClassId = Uri.EscapeDataString(classId.Trim());
        string escapedStudentId = Uri.EscapeDataString(SupabaseSession.UserId);

        yield return SupabaseRestService.Delete(
            "class_members" +
            $"?class_id=eq.{escapedClassId}" +
            $"&user_id=eq.{escapedStudentId}",
            _ => onSuccess?.Invoke(),
            onError);
    }

    public static IEnumerator GetClassById(
        string classId,
        Action<SupabaseClass> onSuccess,
        Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            yield break;
        }

        if (!Guid.TryParse(classId, out _))
        {
            onError?.Invoke("classId không đúng định dạng UUID.");
            yield break;
        }

        string escapedId = Uri.EscapeDataString(classId.Trim());
        string query = $"classes?id=eq.{escapedId}&select=*";

        yield return SupabaseRestService.Get(
            query,
            json =>
            {
                if (!TryParseFirstClass(
                        json,
                        out SupabaseClass result,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(result);
            },
            onError);
    }

    public static IEnumerator UpdateClass(
        string classId,
        UpdateClassRequest payload,
        Action<SupabaseClass> onSuccess,
        Action<string> onError)
    {
        if (!ValidateTeacherSession(onError))
        {
            yield break;
        }

        if (!Guid.TryParse(classId, out _))
        {
            onError?.Invoke("classId không đúng định dạng UUID.");
            yield break;
        }

        if (payload == null)
        {
            onError?.Invoke("Dữ liệu cập nhật lớp học đang null.");
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(payload.visibility))
        {
            if (!IsValidVisibility(payload.visibility))
            {
                onError?.Invoke("visibility chỉ nhận giá trị public hoặc private.");
                yield break;
            }

            payload.visibility = NormalizeVisibility(payload.visibility);
        }

        if (string.IsNullOrWhiteSpace(payload.category_id))
        {
            payload.category_id = null;
        }
        else if (!Guid.TryParse(payload.category_id, out _))
        {
            onError?.Invoke("category_id không đúng định dạng UUID.");
            yield break;
        }

        string escapedId = Uri.EscapeDataString(classId.Trim());
        string query = $"classes?id=eq.{escapedId}&select=*";

        yield return SupabaseRestService.Patch(
            query,
            JsonUtility.ToJson(payload),
            json =>
            {
                if (!TryParseFirstClass(
                        json,
                        out SupabaseClass updatedClass,
                        out string parseError))
                {
                    onError?.Invoke(parseError);
                    return;
                }

                onSuccess?.Invoke(updatedClass);
            },
            onError,
            true);
    }

    public static IEnumerator DeleteClass(
        string classId,
        Action onSuccess,
        Action<string> onError)
    {
        if (!ValidateTeacherSession(onError))
        {
            yield break;
        }

        if (!Guid.TryParse(classId, out _))
        {
            onError?.Invoke("classId không đúng định dạng UUID.");
            yield break;
        }

        string escapedId = Uri.EscapeDataString(classId.Trim());

        yield return SupabaseRestService.Delete(
            $"classes?id=eq.{escapedId}",
            _ => onSuccess?.Invoke(),
            onError);
    }



    private static bool TryParseClassDetailStats(
        string json,
        out ClassDetailStats stats,
        out string error)
    {
        stats = null;
        string trimmed = json?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Supabase trả về response rỗng.";
            return false;
        }

        try
        {
            string wrapped = "{\"items\":" + trimmed + "}";
            ClassDetailStatsArrayWrapper wrapper =
                JsonUtility.FromJson<ClassDetailStatsArrayWrapper>(wrapped);

            if (wrapper?.items == null || wrapper.items.Length == 0)
            {
                error = "Không tìm thấy thống kê của lớp học.";
                return false;
            }

            stats = wrapper.items[0];
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "Không parse được thống kê lớp học: " + exception.Message;
            return false;
        }
    }

    private static bool TryParseClassMembers(
        string json,
        out ClassMemberStudent[] students,
        out string error)
    {
        students = Array.Empty<ClassMemberStudent>();
        string trimmed = json?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Supabase trả về response rỗng khi tải student list.";
            return false;
        }

        try
        {
            string wrapped = "{\"items\":" + trimmed + "}";
            ClassMemberStudentArrayWrapper wrapper =
                JsonUtility.FromJson<ClassMemberStudentArrayWrapper>(wrapped);

            students = wrapper?.items ?? Array.Empty<ClassMemberStudent>();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error =
                "Không parse được danh sách student: " +
                exception.Message;
            return false;
        }
    }


    private static bool ValidateStudentSession(Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            return false;
        }

        if (!Guid.TryParse(SupabaseSession.UserId, out _))
        {
            onError?.Invoke("user_id trong session không đúng định dạng UUID.");
            return false;
        }

        if (SupabaseSession.IsTeacher)
        {
            onError?.Invoke("Chức năng này chỉ dành cho student.");
            return false;
        }

        return true;
    }

    private static bool TryParseStudentClasses(
        string json,
        out StudentEnrolledClass[] classes,
        out string error)
    {
        classes = Array.Empty<StudentEnrolledClass>();
        string trimmed = json?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Supabase trả về response rỗng.";
            return false;
        }

        try
        {
            string wrapped = "{\"items\":" + trimmed + "}";
            StudentEnrolledClassArrayWrapper wrapper =
                JsonUtility.FromJson<StudentEnrolledClassArrayWrapper>(wrapped);
            classes = wrapper?.items ?? Array.Empty<StudentEnrolledClass>();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "Không parse được lớp học của student: " + exception.Message;
            return false;
        }
    }

    private static bool TryParseStudentQuizzes(
        string json,
        out StudentActiveQuiz[] quizzes,
        out string error)
    {
        quizzes = Array.Empty<StudentActiveQuiz>();
        string trimmed = json?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Supabase trả về response rỗng.";
            return false;
        }

        try
        {
            string wrapped = "{\"items\":" + trimmed + "}";
            StudentActiveQuizArrayWrapper wrapper =
                JsonUtility.FromJson<StudentActiveQuizArrayWrapper>(wrapped);
            quizzes = wrapper?.items ?? Array.Empty<StudentActiveQuiz>();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error = "Không parse được active quizzes: " + exception.Message;
            return false;
        }
    }

    private static bool ValidateTeacherSession(Action<string> onError)
    {
        if (!SupabaseSession.IsLoggedIn)
        {
            onError?.Invoke("Không có phiên đăng nhập hợp lệ.");
            return false;
        }

        if (!Guid.TryParse(SupabaseSession.UserId, out _))
        {
            onError?.Invoke("user_id trong session không đúng định dạng UUID.");
            return false;
        }

        if (!SupabaseSession.IsTeacher)
        {
            onError?.Invoke("Chỉ teacher mới có quyền thực hiện thao tác này.");
            return false;
        }

        return true;
    }

    private static bool IsValidVisibility(string visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility))
        {
            return true;
        }

        string normalized = visibility.Trim().ToLowerInvariant();
        return normalized == "public" || normalized == "private";
    }

    private static string NormalizeVisibility(string visibility)
    {
        return string.IsNullOrWhiteSpace(visibility)
            ? "public"
            : visibility.Trim().ToLowerInvariant();
    }

    private static bool TryParseFirstClass(
        string json,
        out SupabaseClass result,
        out string error)
    {
        result = null;

        if (!TryParseClasses(json, out SupabaseClass[] classes, out error))
        {
            return false;
        }

        if (classes.Length == 0)
        {
            error = "Supabase không trả về dữ liệu lớp học.";
            return false;
        }

        result = classes[0];
        error = null;
        return true;
    }

    private static bool TryParseClasses(
        string json,
        out SupabaseClass[] classes,
        out string error)
    {
        classes = Array.Empty<SupabaseClass>();

        string trimmed = json?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            error = "Supabase trả về response rỗng.";
            return false;
        }

        try
        {
            string wrapped = "{\"items\":" + trimmed + "}";

            SupabaseClassArrayWrapper wrapper =
                JsonUtility.FromJson<SupabaseClassArrayWrapper>(wrapped);

            classes = wrapper?.items ?? Array.Empty<SupabaseClass>();
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            error =
                "Không parse được danh sách lớp học: " +
                exception.Message;

            return false;
        }
    }

    [Serializable]
    private class CreateClassWithoutCategoryRequest
    {
        public string teacher_id;
        public string class_name;
        public string description;
        public string class_code;
        public string visibility;
        public string cover_image_url;
        public string cover_template;
    }
}