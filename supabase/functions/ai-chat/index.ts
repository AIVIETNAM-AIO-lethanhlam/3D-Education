// ai-chat: AI Learning Assistant (Gemini).
// 2026-09: accepts an optional `history` array so answers keep the context of the
// conversation: history = [{ role: "user" | "model", text: "..." }, ...] (oldest first).
// 2026-10-03: model fallback. The free tier of the Gemini API limits requests per model
// (per minute and per day). When a model answers 429 (quota), 404 (not available) or
// 5xx (overloaded), the same request is sent to the next model of GEMINI_CHAT_MODELS.
// When every model is out of quota the function returns 429 with
// error_code = "ai_quota_exceeded" so the app can show a clear message.

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const MAX_HISTORY_TURNS = 12;
const MAX_HISTORY_TEXT = 4000;
const MAX_HISTORY_TOTAL = 24000;

const CHAT_MODELS = Array.from(new Set(
  (Deno.env.get("GEMINI_CHAT_MODELS") ??
    "gemini-3.5-flash,gemini-2.5-flash,gemini-3.5-flash-lite,gemini-2.5-flash-lite,gemini-3.6-flash")
    .split(",")
    .map((model) => model.trim())
    .filter(Boolean),
));

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), { status, headers: { ...corsHeaders, "Content-Type": "application/json" } });
}

async function delay(ms: number) {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

const systemInstruction = `
You are an AI Learning Assistant for a 3D education application.

Your main purpose is to help students understand lessons clearly, accurately, and efficiently.

LANGUAGE RULES:
1. Always respond in the same language as the student's question.
2. If the student asks in Vietnamese, respond entirely in Vietnamese.
3. If the student asks in English, respond entirely in English.
4. If the student mixes languages, use the dominant language of the question.

TEACHING RULES:
5. Explain concepts in a student-friendly and easy-to-understand way.
6. Start with the simplest explanation first.
7. Add more detail only when it helps the student understand.
8. When explaining difficult concepts, break them into logical steps.
9. When explaining formulas: write the formula clearly, explain each symbol, explain when it is used,
   and show substitutions step by step when calculations are requested.
10. For science subjects such as Physics, Chemistry and Biology, use scientifically accurate terminology
    but explain difficult terms in simple language.
11. Use practical examples or analogies when they improve understanding.
12. If the student's question is ambiguous, ask a short clarification instead of guessing.
13. If you are uncertain about information, clearly say that you are uncertain.
14. Do not invent lesson-specific information that has not been provided.
15. Do not pretend that you can see a lesson, PDF, image, 3D model, AR object or VR object unless that
    context is explicitly provided to you.
16. Use the previous messages of this conversation (if any) to understand follow-up questions such as
    "explain more", "why?", "give an example" or "cái đó là gì?".

ANSWER LENGTH:
17. For simple conceptual questions, aim for approximately 150 to 300 words unless the student explicitly
    asks for more detail.
18. Give longer step-by-step explanations only when the student asks "why", "how", requests a calculation,
    a solution, or asks for detail.
19. Complete the current explanation before introducing additional optional information.
20. Avoid unnecessarily long responses.

FORMATTING:
21. Return plain readable text suitable for display inside a Unity mobile app.
22. Do NOT use Markdown heading syntax such as #, ##, ###.
23. Do NOT use Markdown bold syntax such as **text**.
24. Do NOT use Markdown horizontal rules such as ---.
25. Simple numbered lists and bullet points are allowed when useful.
26. Keep paragraphs relatively short so the answer is easy to read on a mobile screen.

SAFETY AND ACCURACY:
27. Prioritize educational accuracy over creativity.
28. Never deliberately provide false information.
29. If the question is outside your knowledge or lacks enough context, say so clearly instead of making up an answer.

You are a learning assistant, not merely a general-purpose chatbot. Your goal is to help the student understand the subject.
`.trim();

function buildHistory(raw: unknown): Array<Record<string, unknown>> {
  if (!Array.isArray(raw)) return [];
  const items = raw
    .map((item: any) => ({
      role: String(item?.role ?? "").toLowerCase() === "user" ? "user" : "model",
      text: typeof item?.text === "string" ? item.text.trim().slice(0, MAX_HISTORY_TEXT) : "",
    }))
    .filter((item) => item.text.length > 0)
    .slice(-MAX_HISTORY_TURNS);

  // Keep the newest messages within the total size budget.
  let total = 0;
  const kept: typeof items = [];
  for (let i = items.length - 1; i >= 0; i--) {
    total += items[i].text.length;
    if (total > MAX_HISTORY_TOTAL) break;
    kept.unshift(items[i]);
  }

  // Gemini expects the conversation to start with a user turn.
  while (kept.length > 0 && kept[0].role !== "user") kept.shift();

  // Merge consecutive turns with the same role (Gemini requires alternating roles).
  const merged: Array<{ role: string; text: string }> = [];
  for (const item of kept) {
    const last = merged[merged.length - 1];
    if (last && last.role === item.role) last.text += "\n\n" + item.text;
    else merged.push({ ...item });
  }
  // The new question is a user turn, so history must end with a model turn.
  if (merged.length > 0 && merged[merged.length - 1].role === "user") merged.pop();

  return merged.map((item) => ({ role: item.role, parts: [{ text: item.text }] }));
}

type GeminiResult = { ok: true; model: string; data: any } | { ok: false; status: number; data: any; model: string };

async function callGeminiWithFallback(apiKey: string, requestBody: unknown): Promise<GeminiResult> {
  let last: GeminiResult = { ok: false, status: 502, data: null, model: "" };
  const body = JSON.stringify(requestBody);

  for (const model of CHAT_MODELS) {
    for (let attempt = 1; attempt <= 2; attempt++) {
      const response = await fetch(
        `https://generativelanguage.googleapis.com/v1beta/models/${encodeURIComponent(model)}:generateContent`,
        { method: "POST", headers: { "Content-Type": "application/json", "x-goog-api-key": apiKey }, body },
      );

      let data: any = null;
      try {
        data = await response.json();
      } catch {
        data = null;
      }

      if (response.ok) {
        if (model !== CHAT_MODELS[0]) console.log(`[ai-chat] fallback model used: ${model}`);
        return { ok: true, model, data };
      }

      last = { ok: false, status: response.status, data, model };
      console.warn(`[ai-chat] ${model} HTTP ${response.status}: ${data?.error?.message ?? ""}`.slice(0, 300));

      if (![404, 429, 500, 502, 503, 504].includes(response.status)) return last;
      // Quota exhausted or model not available: try the next model immediately.
      if (response.status === 429 || response.status === 404) break;
      if (attempt < 2) await delay(800);
    }
  }

  return last;
}

function retryAfterSeconds(data: any): number | null {
  const details = Array.isArray(data?.error?.details) ? data.error.details : [];
  for (const detail of details) {
    const delayText = typeof detail?.retryDelay === "string" ? detail.retryDelay : "";
    const match = delayText.match(/^(\d+(?:\.\d+)?)s$/);
    if (match) return Math.ceil(Number(match[1]));
  }
  return null;
}

Deno.serve(async (req) => {
  try {
    if (req.method === "OPTIONS") return new Response("ok", { status: 200, headers: corsHeaders });
    if (req.method !== "POST") return json({ success: false, error: "Method not allowed" }, 405);

    const GEMINI_API_KEY = Deno.env.get("GEMINI_API_KEY");
    if (!GEMINI_API_KEY) {
      console.error("[ai-chat] GEMINI_API_KEY is missing.");
      return json({ success: false, error: "GEMINI_API_KEY is missing" }, 500);
    }

    let body: any;
    try {
      body = await req.json();
    } catch {
      return json({ success: false, error: "Invalid JSON request body" }, 400);
    }

    const str = (v: unknown) => (typeof v === "string" ? v.trim() : "");
    const message = str(body?.message);
    const mode = str(body?.mode).toLowerCase() || "chat";
    const classId = str(body?.classId);
    const lessonId = str(body?.lessonId);
    const modelId = str(body?.modelId);
    const selectedPart = str(body?.selectedPart);
    const imageBase64 = str(body?.imageBase64);
    const imageMimeType = str(body?.imageMimeType).toLowerCase();

    const allowedImageTypes = new Set(["image/jpeg", "image/png", "image/webp"]);
    const images = Array.isArray(body?.images)
      ? body.images.slice(0, 5).map((image: Record<string, unknown>) => ({
          imageBase64: str(image?.imageBase64),
          imageMimeType: str(image?.imageMimeType).toLowerCase(),
        })).filter((image: any) => image.imageBase64.length > 0)
      : [];
    if (imageBase64.length > 0 && images.length === 0) images.push({ imageBase64, imageMimeType });
    const hasImage = images.length > 0;

    if (!message && !hasImage) return json({ success: false, error: "Message is required" }, 400);
    if (images.length > 4) return json({ success: false, error: "Maximum 4 images" }, 400);
    if (images.some((image: any) => !allowedImageTypes.has(image.imageMimeType))) {
      return json({ success: false, error: "Unsupported image format" }, 400);
    }
    if (images.some((image: any) => image.imageBase64.length > 5_000_000) ||
        images.reduce((total: number, image: any) => total + image.imageBase64.length, 0) > 16_000_000) {
      return json({ success: false, error: "Image is too large" }, 413);
    }
    if (message.length > 10000) return json({ success: false, error: "Message is too long" }, 400);

    let appContext = "";
    switch (mode) {
      case "3d":
        appContext = `The student is asking from the 3D model viewer.\n\nModel ID:\n${modelId || "Not provided"}\n\nSelected model part:\n${selectedPart || "Not selected"}`;
        break;
      case "ar":
        appContext = `The student is currently using AR mode.\n\nModel ID:\n${modelId || "Not provided"}\n\nSelected AR object or model part:\n${selectedPart || "Not selected"}\n\nIf the student uses words such as "this", "this part", "phần này", "chỗ này", or similar expressions, use the selected model part above as the reference.`;
        break;
      case "vr":
        appContext = `The student is currently using VR mode.\n\nModel ID:\n${modelId || "Not provided"}\n\nSelected VR object or model part:\n${selectedPart || "Not selected"}\n\nIf the student refers to "this part" or similar wording, use the selected model part above as the reference.`;
        break;
      default:
        appContext = `The student is using the normal AI learning chat.\n\nClass ID:\n${classId || "Not provided"}\n\nLesson ID:\n${lessonId || "Not provided"}`;
        break;
    }

    const userParts: Array<Record<string, unknown>> = [{
      text: `APPLICATION CONTEXT:\n\n${appContext}\n\nSTUDENT QUESTION:\n\n${message || "Please analyze this image and explain its educational content."}`,
    }];
    for (const image of images) {
      userParts.push({ inlineData: { mimeType: image.imageMimeType, data: image.imageBase64 } });
    }

    const history = buildHistory(body?.history);

    const geminiRequestBody = {
      systemInstruction: { parts: [{ text: systemInstruction }] },
      contents: [...history, { role: "user", parts: userParts }],
      generationConfig: { temperature: 0.5, maxOutputTokens: 5012 },
    };

    const result = await callGeminiWithFallback(GEMINI_API_KEY, geminiRequestBody);

    if (!result.ok) {
      console.error("[ai-chat] All Gemini models failed. Last status:", result.status);
      if (result.status === 429) {
        return json({
          success: false,
          error_code: "ai_quota_exceeded",
          error: "Gemini quota exceeded (429). The AI assistant has reached its usage limit; please try again later.",
          message: "Trợ lý AI đã hết lượt sử dụng (quota) của gói miễn phí. Vui lòng thử lại sau.",
          retry_after_seconds: retryAfterSeconds(result.data),
        }, 429);
      }
      if (result.status >= 500) {
        return json({
          success: false,
          error_code: "ai_busy",
          error: "Gemini is overloaded. Please try again in a moment.",
          message: "Máy chủ AI đang quá tải. Vui lòng thử lại sau ít phút.",
        }, 503);
      }
      return json({ success: false, error: "Gemini API request failed", details: result.data }, result.status || 502);
    }

    const geminiData = result.data;
    const parts = geminiData?.candidates?.[0]?.content?.parts;
    const answer = Array.isArray(parts)
      ? parts.map((part: any) => (typeof part?.text === "string" ? part.text : "")).join("").trim()
      : "";
    const finishReason = geminiData?.candidates?.[0]?.finishReason ?? "UNKNOWN";
    const truncated = finishReason === "MAX_TOKENS";
    console.log("[ai-chat] model:", result.model, "finish reason:", finishReason, "history turns:", history.length);

    if (!answer) {
      console.error("[ai-chat] Gemini returned no text.", geminiData);
      return json({ success: false, error: "Gemini returned an empty answer", finishReason, truncated }, 502);
    }

    return json({ success: true, answer, finishReason, truncated, historyTurnsUsed: history.length, model: result.model });
  } catch (error) {
    console.error("[ai-chat] Unexpected error:", error);
    return json({ success: false, error: error instanceof Error ? error.message : "Unknown server error" }, 500);
  }
});
