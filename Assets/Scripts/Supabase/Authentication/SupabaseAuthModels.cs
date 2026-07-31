using System;

[Serializable]
public class SignUpUserMetadata
{
    public string full_name;
    public string display_name;
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
public class SignInRequest
{
    public string email;
    public string password;
}

[Serializable]
public class UpdatePasswordRequest
{
    public string password;
}

[Serializable]
public class SupabaseUserMetadata
{
    public string full_name;
    public string display_name;
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
public class SupabaseAuthResponse
{
    public string access_token;
    public string refresh_token;
    public int expires_in;
    public string token_type;
    public SupabaseUser user;
}

[Serializable]
public class SupabaseErrorResponse
{
    public string code;
    public string error_code;
    public string error;
    public string error_description;
    public string msg;
    public string message;
    public string details;
    public string hint;
}
