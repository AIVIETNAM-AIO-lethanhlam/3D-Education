using System;

[Serializable]
public class ChapterRecord
{
    public string id;
    public string class_id;
    public string title;
    public int chapter_order;
}

[Serializable]
public class CreateChapterRequest
{
    public string class_id;
    public string title;
    public int chapter_order;
}

[Serializable]
public class CreateLessonRequest
{
    public string chapter_id;
    public string teacher_id;
    public string title;
    public string description;
    public string youtube_url;
    public bool has_video;
    public string status;
}

[Serializable]
public class LessonRecord
{
    public string id;
    public string chapter_id;
    public string teacher_id;
    public string title;
    public string description;
    public string youtube_url;
    public bool has_video;
    public string status;
    public string created_at;
    public string updated_at;
}

[Serializable]
public class LessonAssetInsert
{
    public string lesson_id;
    public string uploaded_by;
    public string asset_type;
    public string file_name;
    public string storage_bucket;
    public string storage_path;
    public string mime_type;
    public string file_extension;
    public long file_size_bytes;
    public int display_order;
}

[Serializable]
public class LessonObjectiveInsert
{
    public string lesson_id;
    public string objective_text;
    public int objective_order;
}

[Serializable]
public class LessonStatusUpdate
{
    public string status;
}

[Serializable]
public class ChapterRecordList
{
    public ChapterRecord[] items;
}

[Serializable]
public class LessonRecordList
{
    public LessonRecord[] items;
}