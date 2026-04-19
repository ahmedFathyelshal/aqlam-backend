using Microsoft.Extensions.Localization;

namespace AqlamAcademy.Localization;

/// <summary>
/// A strongly-typed resource class for localized messages.
/// Enables the "dot operator" syntax (e.g., SharedResource.EmailRequired).
/// </summary>
public class SharedResource
{
    // These strings match the keys in our .resx files
    public const string EmailRequired = "EmailRequired";
    public const string InvalidEmail = "InvalidEmail";
    public const string EmailAlreadyInUse = "EmailAlreadyInUse";
    public const string PasswordRequired = "PasswordRequired";
    public const string PasswordTooShort = "PasswordTooShort";
    public const string FullNameRequired = "FullNameRequired";
    public const string FullNameTooLong = "FullNameTooLong";
    public const string InvalidUserType = "InvalidUserType";
}
