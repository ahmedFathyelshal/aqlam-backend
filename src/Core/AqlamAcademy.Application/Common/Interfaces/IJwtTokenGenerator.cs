namespace AqlamAcademy.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid userId, string userName, string email, IList<string> roles, out DateTime expiration);
}
