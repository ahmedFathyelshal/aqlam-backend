namespace AqlamAcademy.Application.Common.Models;

public record AuthTokens(
    string AccessToken, 
    DateTime AccessTokenExpiration,
    string RefreshToken,
    DateTime RefreshTokenExpiration);
