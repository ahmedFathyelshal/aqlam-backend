namespace AqlamAcademy.Api.Contracts.Auth;

/// <summary>
/// Profile returned by login, refresh and /me. Tokens are never included: they travel only
/// in HttpOnly cookies so JavaScript cannot read them.
/// </summary>
public class LoginResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}
