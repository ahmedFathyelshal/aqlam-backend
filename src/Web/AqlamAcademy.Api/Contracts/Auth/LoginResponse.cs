namespace AqlamAcademy.Api.Contracts.Auth;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime Expiration { get; set; }
}
