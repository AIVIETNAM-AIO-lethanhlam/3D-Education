using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class SupabaseQuizService : MonoBehaviour
{
    private const string EDGE_FUNCTION_URL = "https://nfribubvehdzjyguxejq.supabase.co/functions/v1/parse-quiz-pdf";
    private const string SUPABASE_REST_URL = "https://nfribubvehdzjyguxejq.supabase.co/rest/v1";

    /// <summary>
    /// Gọi AI Gemini để bóc tách file PDF bài tập (Phía Giáo viên)
    /// </summary>
    public IEnumerator CallParseQuizFunction(string lessonId, string teacherId, string quizTitle, string pdfUrl, Action<bool> onComplete)
    {
        Debug.Log("⏳ Đang gửi PDF sang AI Gemini để phân tích câu hỏi...");

        ParseQuizPayload payload = new ParseQuizPayload
        {
            lesson_id = lessonId,
            teacher_id = teacherId,
            title = quizTitle,
            pdf_url = pdfUrl
        };

        string jsonBody = JsonUtility.ToJson(payload);

        using (UnityWebRequest www = new UnityWebRequest(EDGE_FUNCTION_URL, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log(" AI đã tách và lưu câu hỏi vào Database thành công!");
                onComplete?.Invoke(true);
            }
            else
            {
                Debug.LogError(" Lỗi xử lý Quiz PDF: " + www.error + " | " + www.downloadHandler.text);
                onComplete?.Invoke(false);
            }
        }
    }

    /// <summary>
    /// Tải danh sách câu hỏi từ Supabase (Phía Học sinh)
    /// </summary>
    public IEnumerator LoadQuizQuestions(string quizId, string anonKey, Action<List<QuizQuestion>> onSuccess)
    {
        string url = $"{SUPABASE_REST_URL}/quiz_questions" +
                     $"?select=id,question_text,question_order,quiz_options(id,option_key,option_text)" +
                     $"&quiz_id=eq.{quizId}&order=question_order.asc";

        using (UnityWebRequest www = UnityWebRequest.Get(url))
        {
            www.SetRequestHeader("apikey", anonKey);
            www.SetRequestHeader("Authorization", "Bearer " + anonKey);

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string jsonResult = "{\"items\":" + www.downloadHandler.text + "}";
                QuestionListWrapper wrapper = JsonUtility.FromJson<QuestionListWrapper>(jsonResult);
                onSuccess?.Invoke(wrapper.items);
            }
            else
            {
                Debug.LogError("Lỗi tải câu hỏi: " + www.error);
            }
        }
    }
}