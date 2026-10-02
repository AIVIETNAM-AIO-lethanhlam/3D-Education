-- 2026-10-03: teacher view / edit of quiz questions and answer keys.
-- Applied to project nfribubvehdzjyguxejq as migration "teacher_quiz_edit".
-- * teacher_get_quiz_for_edit(quiz_id): questions + options (with is_correct) + essay keys, quiz teacher/admin only.
-- * teacher_update_quiz_question(...): updates question text, A–D options, correct option or essay
--   reference answer / key points; re-grades submitted multiple-choice answers when the key changes.
-- Students keep using get_quiz_options_for_student (no is_correct) and only see answers in review.

alter table public.quiz_questions add column if not exists teacher_edited_at timestamptz;

-- (function bodies identical to the applied migration; see Supabase: pg_get_functiondef)
