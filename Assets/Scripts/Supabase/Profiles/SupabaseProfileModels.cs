using System;

[Serializable]
public class SupabaseProfile
{
    public string id;
    public string full_name;
    public string email;
    public string role;
    public string avatar_url;
    public string date_of_birth;
    public string created_at;
    public string updated_at;
}

[Serializable]
public class UpdateProfileRequest
{
    public string full_name;
    public string date_of_birth;
}

[Serializable]
public class SupabaseProfileArrayWrapper
{
    public SupabaseProfile[] items;
}
