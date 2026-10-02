import { createClient } from "npm:@supabase/supabase-js@2";
import {
  DeleteObjectCommand,
  PutObjectCommand,
  S3Client,
} from "npm:@aws-sdk/client-s3@3";

// parse-quiz-pdf (parser_version 3)
//
// Reads a quiz PDF with Gemini and stores it as quizzes / quiz_questions /
// quiz_options / quiz_question_keys.
//
// Version 3 accepts real-world quiz PDFs:
//  - document title, school/teacher header, footer, page numbers and
//    instructions are ignored;
//  - multiple-choice questions may have 2..4 options (not always A/B/C/D);
//  - the correct answer may be printed under the question, in an answer
//    table at the end, marked in bold/underline/*, or missing entirely;
//    missing answers are solved by AI (answer_source = 'ai_generated');
//  - essay (tự luận) questions are supported: the reference answer comes
//    from the PDF when printed, otherwise AI writes one. Reference answers
//    are stored in quiz_question_keys, which students cannot read.
// 2026-10-03: Gemini model fallback when a model is out of quota / overloaded.

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const MAX_PDF_BYTES = 25 * 1024 * 1024;
const GEMINI_MAX_ATTEMPTS = 3;
const MAX_OPTIONS = 4;
const MAX_QUESTIONS = 100;
const OPTION_KEYS = ["A", "B", "C", "D"] as const;

type OptionKey = typeof OPTION_KEYS[number];
type QuestionType = "multiple_choice" | "essay";

type ExtractedOption = { label?: string; text?: string };

type ExtractedQuestion = {
  source_question_number?: number;
  question_type?: string;
  question_text?: string;
  options?: ExtractedOption[];
  printed_answer?: string | null;
};

type ExtractedQuiz = {
  document_title?: string | null;
  questions?: ExtractedQuestion[];
};

type FinalOption = { key: OptionKey; label: string; text: string };

type FinalQuestion = {
  question_order: number;
  source_question_number: number;
  question_type: QuestionType;
  question_text: string;
  options: FinalOption[];
  printed_answer: string | null;
  correct_answer: OptionKey | null; // multiple choice only
  reference_answer: string | null; // essay only
  key_points: string | null; // essay only
  answer_source: "from_pdf" | "ai_generated";
};

async function delay(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

// 2026-10-03: model fallback. Free-tier Gemini quotas are counted per model, so when a
// model returns 429 (quota / rate limit), 404 (model not available) or keeps returning
// 5xx (overloaded), the same request is sent to the next model of the chain.
// The chain can be changed with the GEMINI_FALLBACK_MODELS secret (comma separated).
const GEMINI_FALLBACK_MODELS = (Deno.env.get("GEMINI_FALLBACK_MODELS") ??
  "gemini-3.5-flash,gemini-2.5-flash,gemini-3.5-flash-lite,gemini-2.5-flash-lite")
  .split(",")
  .map((model) => model.trim())
  .filter(Boolean);

async function fetchGeminiWithRetry(
  url: string,
  init: RequestInit,
): Promise<Response> {
  const match = url.match(/\/models\/([^:/]+):/);
  const primary = match ? decodeURIComponent(match[1]) : "";
  const models = primary
    ? [primary, ...GEMINI_FALLBACK_MODELS.filter((model) => model !== primary)]
    : [""];
  const attemptsPerModel = Math.min(GEMINI_MAX_ATTEMPTS, 2);
  let last: Response | null = null;

  for (const model of models) {
    const modelUrl = model && match
      ? url.replace(match[0], `/models/${encodeURIComponent(model)}:`)
      : url;

    for (let attempt = 1; attempt <= attemptsPerModel; attempt++) {
      const response = await fetch(modelUrl, init);

      if (response.ok) {
        if (model !== primary) console.log(`[gemini] fallback model used: ${model}`);
        return response;
      }

      const tryNextModel = [404, 429, 500, 502, 503, 504].includes(response.status);
      if (!tryNextModel) return response;

      const text = await response.text();
      last = new Response(text, { status: response.status, headers: response.headers });
      console.warn(`[gemini] ${model || "model"} HTTP ${response.status}`);

      // Quota or unknown model: retrying the same model does not help.
      if (response.status === 429 || response.status === 404) break;
      if (attempt < attemptsPerModel) await delay(750 * 2 ** (attempt - 1));
    }
  }

  return last!;
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      ...corsHeaders,
      "Content-Type": "application/json; charset=utf-8",
    },
  });
}

function getDefaultKeyFromJsonSecret(envName: string): string {
  const raw = Deno.env.get(envName);
  if (!raw) throw new Error(`${envName} is missing.`);

  try {
    const parsed = JSON.parse(raw);
    if (
      parsed && typeof parsed === "object" &&
      typeof parsed.default === "string" && parsed.default.trim()
    ) {
      return parsed.default.trim();
    }
  } catch {
    if (raw.trim()) return raw.trim();
  }

  throw new Error(`${envName} does not contain a usable default key.`);
}

function bytesToBase64(bytes: Uint8Array): string {
  const chunkSize = 0x8000;
  let binary = "";

  for (let i = 0; i < bytes.length; i += chunkSize) {
    const chunk = bytes.subarray(i, Math.min(i + chunkSize, bytes.length));
    let chunkString = "";
    for (let j = 0; j < chunk.length; j++) {
      chunkString += String.fromCharCode(chunk[j]);
    }
    binary += chunkString;
  }

  return btoa(binary);
}

function sanitizeFileName(fileName: string): string {
  const withoutPath = fileName.replaceAll("\\", "/").split("/").pop() ??
    "quiz.pdf";
  const cleaned = withoutPath
    .replace(/[^a-zA-Z0-9._-]+/g, "_")
    .replace(/_+/g, "_")
    .replace(/^_+|_+$/g, "");

  return cleaned || "quiz.pdf";
}

function buildR2ObjectKey(
  teacherId: string,
  lessonId: string,
  originalFileName: string,
): string {
  const safeFileName = sanitizeFileName(originalFileName);
  const uniqueId = crypto.randomUUID().replaceAll("-", "");
  return `${teacherId}/${lessonId}/exercise/${uniqueId}-${safeFileName}`;
}

class GeminiHttpError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function callGeminiJson(
  geminiUrl: string,
  geminiApiKey: string,
  parts: unknown[],
  responseSchema: unknown,
  label: string,
): Promise<unknown> {
  const response = await fetchGeminiWithRetry(geminiUrl, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-goog-api-key": geminiApiKey,
    },
    body: JSON.stringify({
      contents: [{ role: "user", parts }],
      generationConfig: {
        temperature: 0,
        responseMimeType: "application/json",
        responseSchema,
      },
    }),
  });

  const bodyText = await response.text();

  if (!response.ok) {
    let details = bodyText;
    try {
      const parsedError = JSON.parse(bodyText);
      details = parsedError?.error?.message || parsedError?.error?.status ||
        bodyText;
    } catch {
      // keep raw text
    }
    throw new GeminiHttpError(
      response.status,
      `Gemini ${label} HTTP ${response.status}: ${details}`,
    );
  }

  const body = JSON.parse(bodyText);
  const modelText = body?.candidates?.[0]?.content?.parts
    ?.map((part: { text?: string }) => part.text ?? "")
    .join("")
    .trim();

  if (!modelText) {
    throw new Error(`Gemini returned no ${label} content.`);
  }

  try {
    return JSON.parse(modelText);
  } catch {
    throw new Error(`Gemini returned invalid ${label} JSON.`);
  }
}

// ---------------------------------------------------------------------------
// Step 1: read the PDF and extract questions exactly as printed.
// ---------------------------------------------------------------------------

const EXTRACTION_SCHEMA = {
  type: "OBJECT",
  properties: {
    document_title: { type: "STRING", nullable: true },
    questions: {
      type: "ARRAY",
      items: {
        type: "OBJECT",
        properties: {
          source_question_number: { type: "INTEGER" },
          question_type: {
            type: "STRING",
            enum: ["multiple_choice", "essay"],
          },
          question_text: { type: "STRING" },
          options: {
            type: "ARRAY",
            items: {
              type: "OBJECT",
              properties: {
                label: { type: "STRING" },
                text: { type: "STRING" },
              },
              required: ["label", "text"],
            },
          },
          printed_answer: { type: "STRING", nullable: true },
        },
        required: [
          "source_question_number",
          "question_type",
          "question_text",
          "options",
        ],
      },
    },
  },
  required: ["questions"],
};

const EXTRACTION_PROMPT = `
You are reading a quiz / exam PDF written by a teacher (usually Vietnamese).
Extract every question a student must answer. Copy the text exactly as printed.
Do NOT rewrite, translate, summarise or invent questions.

IGNORE everything that is not a question:
- the document title (e.g. "ĐỀ KIỂM TRA 15 PHÚT", "BÀI TẬP CHƯƠNG 1", "Quiz 1"),
- school / class / teacher / subject / date / time-limit / score boxes,
- "Họ và tên: ......", "Lớp: ....", "Điểm", "Lời phê" lines,
- general instructions ("Khoanh tròn vào đáp án đúng", "Thời gian làm bài..."),
- section headings ("PHẦN I. TRẮC NGHIỆM", "PHẦN II. TỰ LUẬN") — but use them
  as a hint for the question type of the questions below them,
- page headers, page footers, page numbers, watermarks, "--- HẾT ---".
Put the document title (if any) in document_title only.

For each question:
- source_question_number: the printed number ("Câu 3", "Bài 2", "3.", "3)").
  If questions are unnumbered, number them 1, 2, 3... in reading order.
- question_text: the full question text without the "Câu N." prefix. Keep
  multi-line text, formulas and sub-parts (a, b, c of the SAME question) together.
  A question may continue onto the next page.
- question_type:
  * "multiple_choice" when the question lists answer choices to pick from
    (A. B. C. D. / a) b) c) / 1) 2) 3) / ◯ choices). It may have only 2, 3 or 4
    choices — do NOT require all of A, B, C, D, and never invent missing choices.
    True/False ("Đúng/Sai") questions are multiple choice with 2 options.
  * "essay" when the student must write the answer (tự luận, giải thích,
    trình bày, tính, chứng minh, điền vào chỗ trống...) and no choices are listed.
- options: for multiple choice, each choice in printed order with its printed
  label (e.g. "A", "b", "2") and its text without the label. Empty array for essay.
- printed_answer: ONLY if the PDF itself shows the correct answer. It can appear
  as "Đáp án: B", "Answer: B", "ĐA: B", a highlighted/bold/underlined/circled/
  starred choice, or an answer table at the end of the document
  ("BẢNG ĐÁP ÁN: 1-A 2-C ..." / "Hướng dẫn chấm"). Match answer tables to
  questions by number. For multiple choice return the printed label of the
  correct choice. For essays return the printed model answer / solution text.
  Otherwise return null — do NOT guess the answer here.

The answer key itself (answer table, "Hướng dẫn chấm", "Đáp án") is NOT a question.
`.trim();

async function extractQuizFromPdf(
  geminiUrl: string,
  geminiApiKey: string,
  pdfBase64: string,
): Promise<ExtractedQuiz> {
  const result = await callGeminiJson(
    geminiUrl,
    geminiApiKey,
    [
      { text: EXTRACTION_PROMPT },
      { inlineData: { mimeType: "application/pdf", data: pdfBase64 } },
    ],
    EXTRACTION_SCHEMA,
    "extraction",
  );

  return (result ?? {}) as ExtractedQuiz;
}

// ---------------------------------------------------------------------------
// Step 2: normalise and validate what Gemini extracted.
// ---------------------------------------------------------------------------

function cleanText(value: unknown): string {
  return String(value ?? "")
    .replace(/\r\n?/g, "\n")
    .replace(/\u00a0/g, " ")
    .replace(/[ \t]+/g, " ")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

function stripQuestionPrefix(text: string): string {
  return text
    .replace(/^(?:Câu|Cau|Bài|Bai|Question|Q)\s*\d{1,3}\s*[\.\):\-]?\s*/i, "")
    .replace(/^\d{1,3}\s*[\.\)]\s+/, "")
    .trim();
}

function normaliseLabel(value: unknown): string {
  return String(value ?? "")
    .toUpperCase()
    .replace(/^(?:ĐÁP\s*ÁN|DAP\s*AN|ĐA|ANSWER)\s*[:\-]?\s*/i, "")
    .replace(/[^A-Z0-9]/g, "")
    .trim();
}

function normaliseForCompare(value: string): string {
  return value.toLowerCase().normalize("NFC").replace(/\s+/g, " ").trim();
}

function stripOptionLabel(text: string): string {
  return text.replace(/^\(?[A-Da-d1-4]\s*[\.\):]\s+/, "").trim();
}

function findOptionKeyForPrintedAnswer(
  options: FinalOption[],
  printed: string | null,
): OptionKey | null {
  if (!printed) return null;

  const label = normaliseLabel(printed);
  if (label) {
    const byLabel = options.find((o) => normaliseLabel(o.label) === label);
    if (byLabel) return byLabel.key;

    // Printed answer like "C. 25" -> take the leading label.
    const leading = normaliseLabel(printed.trim().split(/[\s\.\):]/)[0]);
    const byLeading = options.find((o) => normaliseLabel(o.label) === leading);
    if (byLeading) return byLeading.key;
  }

  const printedText = normaliseForCompare(stripOptionLabel(printed));
  if (printedText) {
    const byText = options.find((o) =>
      normaliseForCompare(o.text) === printedText
    );
    if (byText) return byText.key;
  }

  return null;
}

function normaliseExtractedQuiz(extracted: ExtractedQuiz): FinalQuestion[] {
  const rawQuestions = Array.isArray(extracted?.questions)
    ? extracted.questions
    : [];

  const questions: FinalQuestion[] = [];

  for (const raw of rawQuestions) {
    const questionText = stripQuestionPrefix(cleanText(raw?.question_text));
    if (!questionText) continue;

    const options: FinalOption[] = [];
    const rawOptions = Array.isArray(raw?.options) ? raw.options : [];

    for (const option of rawOptions) {
      const text = stripOptionLabel(cleanText(option?.text));
      if (!text) continue;
      options.push({
        key: "A", // assigned below
        label: cleanText(option?.label),
        text,
      });
    }

    let questionType: QuestionType =
      raw?.question_type === "essay" ? "essay" : "multiple_choice";

    // A "multiple choice" question with fewer than 2 choices is really an essay.
    if (questionType === "multiple_choice" && options.length < 2) {
      questionType = "essay";
    }

    const sourceNumber = Number(raw?.source_question_number);
    const order = questions.length + 1;

    if (questionType === "multiple_choice" && options.length > MAX_OPTIONS) {
      throw new Error(
        `Câu ${Number.isInteger(sourceNumber) ? sourceNumber : order} có ` +
          `${options.length} lựa chọn; hiện app hỗ trợ tối đa ${MAX_OPTIONS} lựa chọn (A–D).`,
      );
    }

    // Keys are always A, B, C, D in printed order so the app can show them,
    // even if the PDF used a), b), c) or 1), 2), 3).
    options.forEach((option, index) => {
      option.key = OPTION_KEYS[index];
    });

    const printedAnswer = cleanText(raw?.printed_answer ?? "") || null;

    questions.push({
      question_order: order,
      source_question_number: Number.isInteger(sourceNumber) && sourceNumber > 0
        ? sourceNumber
        : order,
      question_type: questionType,
      question_text: questionText,
      options: questionType === "essay" ? [] : options,
      printed_answer: printedAnswer,
      correct_answer: questionType === "multiple_choice"
        ? findOptionKeyForPrintedAnswer(options, printedAnswer)
        : null,
      reference_answer: questionType === "essay" ? printedAnswer : null,
      key_points: null,
      answer_source: "from_pdf",
    });
  }

  if (questions.length === 0) {
    throw new Error("AI không tìm thấy câu hỏi nào trong file PDF.");
  }

  if (questions.length > MAX_QUESTIONS) {
    throw new Error(
      `File có ${questions.length} câu hỏi; tối đa ${MAX_QUESTIONS} câu cho một quiz.`,
    );
  }

  return questions;
}

// ---------------------------------------------------------------------------
// Step 3: solve questions whose answer is not printed in the PDF.
// ---------------------------------------------------------------------------

const SOLVE_SCHEMA = {
  type: "OBJECT",
  properties: {
    answers: {
      type: "ARRAY",
      items: {
        type: "OBJECT",
        properties: {
          question_order: { type: "INTEGER" },
          correct_key: { type: "STRING", nullable: true },
          reference_answer: { type: "STRING", nullable: true },
          key_points: { type: "STRING", nullable: true },
        },
        required: ["question_order"],
      },
    },
  },
  required: ["answers"],
};

type SolvedRow = {
  question_order?: number;
  correct_key?: string | null;
  reference_answer?: string | null;
  key_points?: string | null;
};

async function solveMissingAnswers(
  geminiUrl: string,
  geminiApiKey: string,
  questions: FinalQuestion[],
): Promise<void> {
  const needsAnswer = questions.filter((q) =>
    (q.question_type === "multiple_choice" && !q.correct_answer) ||
    q.question_type === "essay"
  );

  if (needsAnswer.length === 0) return;

  const payload = needsAnswer.map((q) =>
    q.question_type === "multiple_choice"
      ? {
        question_order: q.question_order,
        type: "multiple_choice",
        question: q.question_text,
        choices: Object.fromEntries(q.options.map((o) => [o.key, o.text])),
      }
      : {
        question_order: q.question_order,
        type: "essay",
        question: q.question_text,
        printed_model_answer: q.reference_answer, // may be null
      }
  );

  const prompt = `
You are an expert teacher. Answer the quiz questions below. They were already
extracted from a PDF — do NOT add, remove or rewrite questions.
Write in the same language as the question (usually Vietnamese).

For every item return one row with the same question_order:
- type "multiple_choice": correct_key = the single correct choice key among
  the given choices (A, B, C or D). Solve carefully step by step in your head
  before choosing. reference_answer and key_points = null.
- type "essay":
  * If printed_model_answer is given, keep it as reference_answer (you may
    fix obvious OCR typos only) and list its key ideas in key_points.
  * Otherwise write a correct, concise model answer in reference_answer
    (what a good student should write, including the final result for
    calculations) and list the essential ideas a correct answer must contain
    in key_points (short bullet lines starting with "- ").
  correct_key = null.

Questions (JSON):
${JSON.stringify(payload)}
`.trim();

  const result = await callGeminiJson(
    geminiUrl,
    geminiApiKey,
    [{ text: prompt }],
    SOLVE_SCHEMA,
    "answer-solving",
  ) as { answers?: SolvedRow[] };

  const byOrder = new Map<number, SolvedRow>();
  for (const row of result?.answers ?? []) {
    const order = Number(row?.question_order);
    if (Number.isInteger(order)) byOrder.set(order, row);
  }

  for (const q of needsAnswer) {
    const row = byOrder.get(q.question_order);

    if (q.question_type === "multiple_choice") {
      const key = normaliseLabel(row?.correct_key ?? "") as OptionKey;
      const valid = q.options.some((o) => o.key === key);
      if (!valid) {
        throw new Error(
          `AI không xác định được đáp án đúng cho câu ${q.source_question_number}.`,
        );
      }
      q.correct_answer = key;
      q.answer_source = "ai_generated";
      continue;
    }

    // essay
    const reference = cleanText(row?.reference_answer ?? "");
    if (!q.reference_answer) {
      if (!reference) {
        throw new Error(
          `AI không tạo được đáp án mẫu cho câu tự luận ${q.source_question_number}.`,
        );
      }
      q.reference_answer = reference;
      q.answer_source = "ai_generated";
    }
    q.key_points = cleanText(row?.key_points ?? "") || null;
  }
}

async function safeDeleteR2Object(
  s3: S3Client,
  bucket: string,
  objectKey: string | null,
) {
  if (!objectKey) return;

  try {
    await s3.send(new DeleteObjectCommand({ Bucket: bucket, Key: objectKey }));
  } catch (error) {
    console.error("[parse-quiz-pdf] Cleanup R2 delete failed:", error);
  }
}

Deno.serve(async (req: Request) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }

  if (req.method !== "POST") {
    return jsonResponse(
      { success: false, error: "Method not allowed. Use POST." },
      405,
    );
  }

  let uploadedR2ObjectKey: string | null = null;
  let insertedLessonAssetId: string | null = null;
  let insertedQuizId: string | null = null;

  let adminSupabase: ReturnType<typeof createClient> | null = null;
  let r2Client: S3Client | null = null;
  let r2Bucket = "";

  try {
    // 1) Authenticate user
    const authHeader = req.headers.get("Authorization");

    if (!authHeader?.startsWith("Bearer ")) {
      return jsonResponse(
        { success: false, error: "Missing Authorization Bearer token." },
        401,
      );
    }

    const userAccessToken = authHeader.replace("Bearer ", "").trim();
    const supabaseUrl = Deno.env.get("SUPABASE_URL");
    if (!supabaseUrl) throw new Error("SUPABASE_URL is missing.");

    const publishableKey = getDefaultKeyFromJsonSecret(
      "SUPABASE_PUBLISHABLE_KEYS",
    );
    const secretKey = getDefaultKeyFromJsonSecret("SUPABASE_SECRET_KEYS");

    const userSupabase = createClient(supabaseUrl, publishableKey, {
      global: { headers: { Authorization: authHeader } },
      auth: { persistSession: false, autoRefreshToken: false },
    });

    const { data: { user }, error: userError } = await userSupabase.auth
      .getUser(userAccessToken);

    if (userError || !user) {
      return jsonResponse(
        { success: false, error: "Invalid or expired Supabase access token." },
        401,
      );
    }

    adminSupabase = createClient(supabaseUrl, secretKey, {
      auth: { persistSession: false, autoRefreshToken: false },
    });

    // Deadline-only updates (JSON body).
    const contentType = req.headers.get("Content-Type") ?? "";
    if (contentType.toLowerCase().includes("application/json")) {
      const body = await req.json();

      if (body?.action !== "update_deadline") {
        return jsonResponse({ success: false, error: "Unsupported action." }, 400);
      }

      const quizId = String(body.quiz_id ?? "").trim();
      const closesAtInput = String(body.closes_at ?? "").trim();

      if (!quizId || !closesAtInput) {
        return jsonResponse(
          { success: false, error: "quiz_id and closes_at are required." },
          400,
        );
      }

      const deadline = new Date(closesAtInput);
      if (Number.isNaN(deadline.getTime())) {
        return jsonResponse(
          { success: false, error: "closes_at must be a valid ISO-8601 date." },
          400,
        );
      }

      if (deadline.getTime() <= Date.now()) {
        return jsonResponse(
          { success: false, error: "Quiz deadline must be in the future." },
          400,
        );
      }

      const { data: existingQuiz, error: lookupError } = await adminSupabase
        .from("quizzes")
        .select("id,teacher_id")
        .eq("id", quizId)
        .maybeSingle();

      if (lookupError) {
        throw new Error("Cannot verify quiz ownership: " + lookupError.message);
      }
      if (!existingQuiz) {
        return jsonResponse({ success: false, error: "Quiz was not found." }, 404);
      }
      if (existingQuiz.teacher_id !== user.id) {
        return jsonResponse(
          { success: false, error: "Only the quiz teacher can update its deadline." },
          403,
        );
      }

      const { data: updatedQuiz, error: updateError } = await adminSupabase
        .from("quizzes")
        .update({ closes_at: deadline.toISOString() })
        .eq("id", quizId)
        .select("id,closes_at")
        .single();

      if (updateError || !updatedQuiz) {
        throw new Error(
          "Could not update quiz deadline: " +
            (updateError?.message ?? "unknown error"),
        );
      }

      return jsonResponse({
        success: true,
        quiz_id: updatedQuiz.id,
        closes_at: updatedQuiz.closes_at,
      });
    }

    // 2) Read PDF
    if (!contentType.toLowerCase().includes("multipart/form-data")) {
      return jsonResponse(
        { success: false, error: "Content-Type must be multipart/form-data." },
        400,
      );
    }

    const formData = await req.formData();
    const fileValue = formData.get("file");

    if (!(fileValue instanceof File)) {
      return jsonResponse(
        { success: false, error: "PDF field 'file' is missing." },
        400,
      );
    }

    const extensionIsPdf = fileValue.name.toLowerCase().endsWith(".pdf");
    if (fileValue.type !== "application/pdf" && !extensionIsPdf) {
      return jsonResponse(
        {
          success: false,
          error: `Only PDF files are accepted. Received MIME: ${fileValue.type}`,
        },
        400,
      );
    }

    if (fileValue.size <= 0) {
      return jsonResponse({ success: false, error: "The uploaded PDF is empty." }, 400);
    }

    if (fileValue.size > MAX_PDF_BYTES) {
      return jsonResponse(
        { success: false, error: "PDF is larger than the current 25 MB limit." },
        413,
      );
    }

    const lessonId = String(formData.get("lesson_id") ?? "").trim();
    if (!lessonId) {
      return jsonResponse({ success: false, error: "lesson_id is required." }, 400);
    }

    const requestedTitle = String(formData.get("quiz_title") ?? "").trim();

    const closesAtInput = String(formData.get("closes_at") ?? "").trim();
    let closesAt: string | null = null;

    if (closesAtInput) {
      const deadline = new Date(closesAtInput);
      if (Number.isNaN(deadline.getTime())) {
        return jsonResponse(
          { success: false, error: "closes_at must be a valid ISO-8601 date." },
          400,
        );
      }
      if (deadline.getTime() <= Date.now()) {
        return jsonResponse(
          { success: false, error: "Quiz deadline must be in the future." },
          400,
        );
      }
      closesAt = deadline.toISOString();
    }

    const { data: lesson, error: lessonError } = await adminSupabase
      .from("lessons")
      .select("id,teacher_id,title")
      .eq("id", lessonId)
      .maybeSingle();

    if (lessonError) throw new Error("Cannot verify lesson: " + lessonError.message);
    if (!lesson) {
      return jsonResponse(
        { success: false, error: "The requested lesson does not exist." },
        404,
      );
    }
    if (lesson.teacher_id !== user.id) {
      return jsonResponse(
        { success: false, error: "You are not allowed to upload a quiz for this lesson." },
        403,
      );
    }

    const pdfBytes = new Uint8Array(await fileValue.arrayBuffer());

    // 3) Gemini: extract -> normalise -> solve missing answers
    const geminiApiKey = Deno.env.get("GEMINI_API_KEY");
    if (!geminiApiKey) throw new Error("GEMINI_API_KEY is missing.");

    const geminiModel = Deno.env.get("GEMINI_MODEL")?.trim() ||
      "gemini-3.6-flash";

    const geminiUrl = "https://generativelanguage.googleapis.com/v1beta/models/" +
      `${encodeURIComponent(geminiModel)}:generateContent`;

    const pdfBase64 = bytesToBase64(pdfBytes);

    let questions: FinalQuestion[];
    let documentTitle: string | null = null;

    try {
      const extracted = await extractQuizFromPdf(
        geminiUrl,
        geminiApiKey,
        pdfBase64,
      );
      documentTitle = cleanText(extracted?.document_title ?? "") || null;
      questions = normaliseExtractedQuiz(extracted);
      await solveMissingAnswers(geminiUrl, geminiApiKey, questions);

      console.log(
        "[parse-quiz-pdf] Parse result:",
        JSON.stringify({
          model: geminiModel,
          document_title: documentTitle,
          total: questions.length,
          multiple_choice: questions.filter((q) =>
            q.question_type === "multiple_choice"
          ).length,
          essay: questions.filter((q) => q.question_type === "essay").length,
          ai_generated_answers: questions.filter((q) =>
            q.answer_source === "ai_generated"
          ).length,
        }),
      );
    } catch (parseError) {
      console.warn("[parse-quiz-pdf] Quiz parsing failed:", parseError);

      const geminiStatus = parseError instanceof GeminiHttpError
        ? parseError.status
        : 0;

      return jsonResponse(
        {
          success: false,
          error: geminiStatus === 429
            ? "Gemini rate limit."
            : "Không đọc được câu hỏi từ file quiz PDF.",
          error_code: geminiStatus === 429 ? "ai_quota_exceeded" : "quiz_parse_failed",
          details: parseError instanceof Error
            ? parseError.message
            : String(parseError),
          gemini_http_status: geminiStatus,
          gemini_model: geminiModel,
        },
        geminiStatus === 429 ? 429 : 422,
      );
    }

    const finalQuizTitle = requestedTitle || documentTitle ||
      fileValue.name.replace(/\.pdf$/i, "");

    // 4) Upload original PDF to R2
    const r2AccountId = Deno.env.get("R2_ACCOUNT_ID")?.trim();
    const r2AccessKeyId = Deno.env.get("R2_ACCESS_KEY_ID")?.trim();
    const r2SecretAccessKey = Deno.env.get("R2_SECRET_ACCESS_KEY")?.trim();
    r2Bucket = Deno.env.get("R2_BUCKET_NAME")?.trim() || "lesson-documents";

    if (!r2AccountId || !r2AccessKeyId || !r2SecretAccessKey || !r2Bucket) {
      throw new Error("One or more R2 secrets are missing.");
    }

    r2Client = new S3Client({
      region: "auto",
      endpoint: `https://${r2AccountId}.r2.cloudflarestorage.com`,
      credentials: {
        accessKeyId: r2AccessKeyId,
        secretAccessKey: r2SecretAccessKey,
      },
    });

    uploadedR2ObjectKey = buildR2ObjectKey(user.id, lessonId, fileValue.name);

    await r2Client.send(
      new PutObjectCommand({
        Bucket: r2Bucket,
        Key: uploadedR2ObjectKey,
        Body: pdfBytes,
        ContentType: "application/pdf",
        Metadata: { lesson_id: lessonId, uploaded_by: user.id },
      }),
    );

    // 5) lesson_assets
    const { data: lessonAsset, error: lessonAssetError } = await adminSupabase
      .from("lesson_assets")
      .insert({
        lesson_id: lessonId,
        uploaded_by: user.id,
        asset_type: "quiz_pdf",
        file_name: sanitizeFileName(fileValue.name),
        storage_bucket: r2Bucket,
        storage_path: uploadedR2ObjectKey,
        mime_type: "application/pdf",
        file_extension: ".pdf",
        file_size_bytes: fileValue.size,
        display_order: 0,
      })
      .select("id")
      .single();

    if (lessonAssetError || !lessonAsset) {
      throw new Error(
        "Could not create lesson_assets record: " +
          (lessonAssetError?.message ?? "unknown error"),
      );
    }
    insertedLessonAssetId = lessonAsset.id;

    // 6) quizzes
    const { data: quiz, error: quizError } = await adminSupabase
      .from("quizzes")
      .insert({
        lesson_id: lessonId,
        teacher_id: user.id,
        title: finalQuizTitle,
        closes_at: closesAt,
        source_asset_id: insertedLessonAssetId,
        source_bucket: r2Bucket,
        source_path: uploadedR2ObjectKey,
        total_questions: questions.length,
        max_score: 10,
        parse_status: "completed",
        parse_error: null,
        parsed_at: new Date().toISOString(),
      })
      .select("id")
      .single();

    if (quizError || !quiz) {
      throw new Error(
        "Could not create quizzes record: " + (quizError?.message ?? "unknown error"),
      );
    }
    insertedQuizId = quiz.id;

    // 7) quiz_questions
    const { data: insertedQuestions, error: questionsError } = await adminSupabase
      .from("quiz_questions")
      .insert(questions.map((q) => ({
        quiz_id: insertedQuizId,
        question_text: q.question_text,
        question_order: q.question_order,
        question_type: q.question_type,
        answer_source: q.answer_source,
        explanation: null,
        image_url: null,
      })))
      .select("id,question_order");

    if (
      questionsError || !insertedQuestions ||
      insertedQuestions.length !== questions.length
    ) {
      throw new Error(
        "Could not insert quiz_questions: " +
          (questionsError?.message ?? "unexpected inserted row count"),
      );
    }

    const questionIdByOrder = new Map<number, string>();
    for (const row of insertedQuestions) {
      questionIdByOrder.set(row.question_order, row.id);
    }

    // 8) quiz_options (multiple choice) + quiz_question_keys (essay)
    const optionRows: Record<string, unknown>[] = [];
    const keyRows: Record<string, unknown>[] = [];

    for (const q of questions) {
      const questionId = questionIdByOrder.get(q.question_order);
      if (!questionId) {
        throw new Error(`Cannot map inserted question ${q.question_order}.`);
      }

      if (q.question_type === "multiple_choice") {
        for (const option of q.options) {
          optionRows.push({
            question_id: questionId,
            option_key: option.key,
            option_text: option.text,
            image_url: null,
            is_correct: option.key === q.correct_answer,
          });
        }
      } else {
        keyRows.push({
          question_id: questionId,
          reference_answer: q.reference_answer,
          key_points: q.key_points,
        });
      }
    }

    if (optionRows.length > 0) {
      const { error: optionsError } = await adminSupabase
        .from("quiz_options")
        .insert(optionRows);
      if (optionsError) {
        throw new Error("Could not insert quiz_options: " + optionsError.message);
      }
    }

    if (keyRows.length > 0) {
      const { error: keysError } = await adminSupabase
        .from("quiz_question_keys")
        .insert(keyRows);
      if (keysError) {
        throw new Error("Could not insert quiz_question_keys: " + keysError.message);
      }
    }

    const optionText = (q: FinalQuestion, key: OptionKey) =>
      q.options.find((o) => o.key === key)?.text ?? "";

    return jsonResponse({
      success: true,
      parser_version: 3,
      user_id: user.id,
      lesson_id: lessonId,
      quiz_id: insertedQuizId,
      lesson_asset_id: insertedLessonAssetId,
      closes_at: closesAt,
      original_file_name: fileValue.name,
      original_file_size: fileValue.size,
      storage: { provider: "cloudflare_r2", bucket: r2Bucket, path: uploadedR2ObjectKey },
      quiz: {
        title: finalQuizTitle,
        total_questions: questions.length,
        multiple_choice_count: questions.filter((q) =>
          q.question_type === "multiple_choice"
        ).length,
        essay_count: questions.filter((q) => q.question_type === "essay").length,
        questions: questions.map((q) => ({
          question_order: q.question_order,
          source_question_number: q.source_question_number,
          question_type: q.question_type,
          question_text: q.question_text,
          option_count: q.options.length,
          // kept for older app builds
          option_a: optionText(q, "A"),
          option_b: optionText(q, "B"),
          option_c: optionText(q, "C"),
          option_d: optionText(q, "D"),
          correct_answer: q.correct_answer ?? "",
          reference_answer: q.reference_answer ?? "",
          answer_source: q.answer_source,
          explanation: "",
        })),
      },
    });
  } catch (error) {
    console.error("[parse-quiz-pdf] Unexpected error:", error);

    if (adminSupabase && insertedQuizId) {
      try {
        await adminSupabase.from("quizzes").delete().eq("id", insertedQuizId);
      } catch (cleanupError) {
        console.error("[parse-quiz-pdf] Quiz cleanup failed:", cleanupError);
      }
    }

    if (adminSupabase && insertedLessonAssetId) {
      try {
        await adminSupabase.from("lesson_assets").delete().eq(
          "id",
          insertedLessonAssetId,
        );
      } catch (cleanupError) {
        console.error("[parse-quiz-pdf] lesson_assets cleanup failed:", cleanupError);
      }
    }

    if (r2Client && r2Bucket && uploadedR2ObjectKey) {
      await safeDeleteR2Object(r2Client, r2Bucket, uploadedR2ObjectKey);
    }

    return jsonResponse(
      {
        success: false,
        error: error instanceof Error ? error.message : "Unexpected server error.",
      },
      500,
    );
  }
});
