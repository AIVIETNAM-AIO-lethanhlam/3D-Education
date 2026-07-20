using System;

[Serializable]
public class SignUpUserMetadata
{
    public string full_name;
    public string role;
    public string avatar_url;
}

[Serializable]
public class SignUpRequest
{
    public string email;
    public string password;
    public SignUpUserMetadata data;
}

[Serializable]
public class SupabaseUserMetadata
{
    public string full_name;
    public string role;
    public string avatar_url;
}

[Serializable]
public class SupabaseUser
{
    public string id;
    public string email;
    public SupabaseUserMetadata user_metadata;
}

[Serializable]
public class SupabaseSignUpResponse
{
    public string access_token;
    public string refresh_token;
    public int expires_in;
    public SupabaseUser user;
}

[Serializable]
public class SupabaseErrorResponse
{
    public string code;
    public string error_code;
    public string msg;
    public string message;
}