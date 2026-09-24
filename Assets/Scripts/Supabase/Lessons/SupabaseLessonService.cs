using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class SupabaseLessonService : MonoBehaviour
{
    private SupabaseRuntimeRestService rest;

    [Serializable]
    private class JsonArrayWrapper<T>
    {
        public T[] items;
    }

    private void Awake()
    {
        ResolveRestService();
    }

    private bool ResolveRestService()
    {
        if (rest == null)
            rest = GetComponent<SupabaseRuntimeRestService>();

        if (rest != null)
            return true;

        Debug.LogError("[SupabaseLessonService] SupabaseRuntimeRestService is missing.");
        return false;
    }

    #region Chapter Queries

    public IEnumerator GetChaptersByClass(
        string classId,
        Action<List<ChapterRecord>> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(classId))
        {
            onError?.Invoke("classId is empty.");
            yield break;
        }

        string encodedClassId = UnityWebRequest.EscapeURL(classId);
        string relativeUrl = $"rest/v1/chapters?class_id=eq.{encodedClassId}&select=id,class_id,title,chapter_order&order=chapter_order.asc";

        string response = null;
        string error = null;

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbGET,
            relativeUrl,
            null,
            null,
            value => response = value,
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        try
        {
            ChapterRecordList wrapper = JsonUtility.FromJson<ChapterRecordList>($"{{\"items\":{response}}}");
            onSuccess?.Invoke(wrapper?.items == null ? new List<ChapterRecord>() : new List<ChapterRecord>(wrapper.items));
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Cannot parse chapter response: {ex.Message}");
        }
    }

    public IEnumerator CreateChapter(
        CreateChapterRequest requestData,
        Action<ChapterRecord> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        if (requestData == null)
        {
            onError?.Invoke("CreateChapterRequest is null.");
            yield break;
        }

        string payload = JsonUtility.ToJson(requestData);
        string response = null;
        string error = null;

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/chapters?select=id,class_id,title,chapter_order",
            payload,
            "return=representation",
            value => response = value,
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        try
        {
            ChapterRecordList wrapper = JsonUtility.FromJson<ChapterRecordList>($"{{\"items\":{response}}}");
            if (wrapper?.items == null || wrapper.items.Length == 0)
            {
                onError?.Invoke("Supabase created the chapter but returned no row.");
                yield break;
            }

            onSuccess?.Invoke(wrapper.items[0]);
        }
        catch (Exception exception)
        {
            onError?.Invoke("Cannot parse created chapter: " + exception.Message);
        }
    }

    #endregion

    #region Lesson Queries

    public IEnumerator GetLessonsByChapter(
        string chapterId,
        Action<List<LessonRecord>> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string encodedChapterId = UnityWebRequest.EscapeURL(chapterId);
        string relativeUrl = $"rest/v1/lessons?chapter_id=eq.{encodedChapterId}&select=*";

        string response = null;
        string error = null;

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbGET,
            relativeUrl,
            null,
            null,
            value => response = value,
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        try
        {
            LessonRecordList wrapper = JsonUtility.FromJson<LessonRecordList>($"{{\"items\":{response}}}");
            onSuccess?.Invoke(wrapper?.items == null ? new List<LessonRecord>() : new List<LessonRecord>(wrapper.items));
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Cannot parse lessons response: {ex.Message}");
        }
    }

    public IEnumerator CreateLesson(
        CreateLessonRequest requestData,
        Action<LessonRecord> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string response = null;
        string error = null;
        string payload = JsonUtility.ToJson(requestData);

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/lessons?select=*",
            payload,
            "return=representation",
            value => response = value,
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        try
        {
            LessonRecordList wrapper = JsonUtility.FromJson<LessonRecordList>($"{{\"items\":{response}}}");
            if (wrapper?.items == null || wrapper.items.Length == 0)
            {
                onError?.Invoke("Supabase created the lesson but returned no lesson row.");
                yield break;
            }

            onSuccess?.Invoke(wrapper.items[0]);
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Cannot parse created lesson: {ex.Message}");
        }
    }

    #endregion

    #region Asset & Objective Queries

    public IEnumerator GetLessonAssetsByLesson(
        string lessonId,
        Action<List<LessonAssetRecord>> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string encodedLessonId = UnityWebRequest.EscapeURL(lessonId);
        string relativeUrl = $"rest/v1/lesson_assets?lesson_id=eq.{encodedLessonId}&select=*&order=display_order.asc";

        string response = null;
        string error = null;

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbGET,
            relativeUrl,
            null,
            null,
            value => response = value,
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        try
        {
            LessonAssetRecordList wrapper = JsonUtility.FromJson<LessonAssetRecordList>($"{{\"items\":{response}}}");
            onSuccess?.Invoke(wrapper?.items == null ? new List<LessonAssetRecord>() : new List<LessonAssetRecord>(wrapper.items));
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Cannot parse lesson assets: {ex.Message}");
        }
    }

    public IEnumerator CreateLessonAsset(
        LessonAssetInsert asset,
        Action onSuccess,
        Action<string> onError)
    {
        yield return InsertWithoutResponse(
            "rest/v1/lesson_assets",
            JsonUtility.ToJson(asset),
            onSuccess,
            onError
        );
    }

    /// <summary>
    /// Inserts an asset and returns the authoritative lesson_assets row. The
    /// returned id is required when queueing GLB analysis; guessing it from a
    /// lesson id or storage path can process the wrong model.
    /// </summary>
    public IEnumerator CreateLessonAsset(
        LessonAssetInsert asset,
        Action<LessonAssetRecord> onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string response = null;
        string error = null;
        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "rest/v1/lesson_assets?select=id,lesson_id,uploaded_by,asset_type,file_name,storage_bucket,storage_path,mime_type,file_extension,file_size_bytes,display_order,created_at",
            JsonUtility.ToJson(asset),
            "return=representation",
            value => response = value,
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        try
        {
            JsonArrayWrapper<LessonAssetRecord> wrapper =
                JsonUtility.FromJson<JsonArrayWrapper<LessonAssetRecord>>(
                    "{\"items\":" + response + "}");
            LessonAssetRecord created =
                wrapper?.items != null && wrapper.items.Length > 0
                    ? wrapper.items[0]
                    : null;
            if (created == null || string.IsNullOrWhiteSpace(created.id))
            {
                onError?.Invoke("Supabase created the model asset but returned no id.");
                yield break;
            }
            onSuccess?.Invoke(created);
        }
        catch (Exception exception)
        {
            onError?.Invoke("Cannot parse created lesson asset: " + exception.Message);
        }
    }

    /// <summary>Runs GLB structure analysis immediately after the R2 upload.</summary>
    public IEnumerator GenerateModelDetails(
        string assetId,
        Action onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(assetId))
        {
            onError?.Invoke("assetId is empty.");
            yield break;
        }

        string error = null;
        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            "functions/v1/generate-model-details",
            // `action` makes this request self-describing in Edge Function logs.
            // The generate-model-details function accepts it, while asset_id
            // remains the authoritative input.
            "{\"action\":\"generate_model_details\",\"asset_id\":\"" +
                EscapeJson(assetId.Trim()) + "\"}",
            null,
            _ => { },
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            if (error.IndexOf("Unsupported action", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                onError?.Invoke(
                    "Edge Function 'generate-model-details' đang chạy nhầm mã nguồn " +
                    "(mã quiz/update_deadline). Hãy deploy file index.ts của pipeline " +
                    "phân tích GLB vào đúng function 'generate-model-details', sau đó thử lại."
                );
            }
            else
            {
                onError?.Invoke(error);
            }
            yield break;
        }

        onSuccess?.Invoke();
    }

    private static string EscapeJson(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    public IEnumerator CreateLessonObjective(
        LessonObjectiveInsert objective,
        Action onSuccess,
        Action<string> onError)
    {
        yield return InsertWithoutResponse(
            "rest/v1/lesson_objectives",
            JsonUtility.ToJson(objective),
            onSuccess,
            onError
        );
    }

    public IEnumerator UpdateLessonStatus(
        string lessonId,
        string status,
        Action onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string encodedId = UnityWebRequest.EscapeURL(lessonId);
        LessonStatusUpdate payload = new() { status = status };
        string error = null;

        yield return rest.SendJson(
            "PATCH",
            $"rest/v1/lessons?id=eq.{encodedId}",
            JsonUtility.ToJson(payload),
            "return=minimal",
            _ => { },
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        onSuccess?.Invoke();
    }

    #endregion

    private IEnumerator InsertWithoutResponse(
        string relativeUrl,
        string json,
        Action onSuccess,
        Action<string> onError)
    {
        if (!ResolveRestService())
        {
            onError?.Invoke("SupabaseRuntimeRestService is missing.");
            yield break;
        }

        string error = null;

        yield return rest.SendJson(
            UnityWebRequest.kHttpVerbPOST,
            relativeUrl,
            json,
            "return=minimal",
            _ => { },
            value => error = value
        );

        if (!string.IsNullOrWhiteSpace(error))
        {
            onError?.Invoke(error);
            yield break;
        }

        onSuccess?.Invoke();
    }
}
