namespace AqlamAcademy.Application.Common.Constants;

/// <summary>
/// Validation message key constants used by FluentValidation validators.
/// These keys are resolved to localized strings at runtime by the Presentation layer.
/// </summary>
public static class ValidationMessages
{
    public const string UserNameRequired = "UserNameRequired";
    public const string InvalidEmail = "InvalidEmail";
    public const string UserNameAlreadyInUse = "UserNameAlreadyInUse";
    public const string PasswordRequired = "PasswordRequired";
    public const string PasswordTooShort = "PasswordTooShort";
    public const string FullNameRequired = "FullNameRequired";
    public const string FullNameTooLong = "FullNameTooLong";
    public const string InvalidUserType = "InvalidUserType";
    public const string PasswordsDoNotMatch = "PasswordsDoNotMatch";
}
