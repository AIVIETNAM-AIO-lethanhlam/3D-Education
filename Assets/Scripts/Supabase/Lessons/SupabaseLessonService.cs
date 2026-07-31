using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(SupabaseRuntimeRestService))]
public class SupabaseLessonService : MonoBehaviour
{
    private SupabaseRuntimeRestService rest;

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

        Debug.LogError(
            "[SupabaseLessonService] SupabaseRuntimeRestService is missing."
        );

        return false;
    }

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
        string relativeUrl =
            $"rest/v1/chapters?class_id=eq.{encodedClassId}" +
            "&select=id,class_id,title,chapter_order" +
            "&order=chapter_order.asc";

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
            ChapterRecordList wrapper =
                JsonUtility.FromJson<ChapterRecordList>($"{{\"items\":{response}}}");

            onSuccess?.Invoke(
                wrapper?.items == null
                    ? new List<ChapterRecord>()
                    : new List<ChapterRecord>(wrapper.items)
            );
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

        if (!Guid.TryParse(requestData.class_id, out _))
        {
            onError?.Invoke("class_id is not a valid UUID.");
            yield break;
        }

        string payload = JsonUtility.ToJson(requestData);
        string response = null;
        string error = null;

        Debug.Log("[CreateChapter] Payload:\n" + payload);

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
            ChapterRecordList wrapper =
                JsonUtility.FromJson<ChapterRecordList>(
                    $"{{\"items\":{response}}}"
                );

            if (wrapper?.items == null ||
                wrapper.items.Length == 0)
            {
                onError?.Invoke(
                    "Supabase created the chapter but returned no row."
                );
                yield break;
            }

            onSuccess?.Invoke(wrapper.items[0]);
        }
        catch (Exception exception)
        {
            onError?.Invoke(
                "Cannot parse created chapter: " +
                exception.Message
            );
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

        if (requestData == null)
        {
            onError?.Invoke("CreateLessonRequest is null.");
            yield break;
        }

        string response = null;
        string error = null;
        string payload = JsonUtility.ToJson(requestData);

        Debug.Log("[CreateLesson] Payload:\n" + payload);

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
            LessonRecordList wrapper =
                JsonUtility.FromJson<LessonRecordList>($"{{\"items\":{response}}}");

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

        if (string.IsNullOrWhiteSpace(lessonId))
        {
            onError?.Invoke("lessonId is empty.");
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