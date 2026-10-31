using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Infrastructure.Persistence.Identity;

namespace AqlamAcademy.Infrastructure.Persistence.Services;

public class TokenService : ITokenService
{
    private readonly ApplicationDbContext _context;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<TokenService> _logger;
    private readonly UserManager<ApplicationUser> _userManager;
    private const int MaxDevicesAllowed = 2;
    private const int ConcurrentRefreshGraceSeconds = 30;

    public TokenService(
        ApplicationDbContext context, 
        IOptions<JwtSettings> jwtSettings,
        ILogger<TokenService> logger,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
        _userManager = userManager;
    }

    public async Task<AuthTokens> GenerateAccessAndRefreshTokensAsync(
        ApplicationUser user, 
        IList<string> roles, 
        string? ipAddress, 
        string? userAgent,
        string? deviceId)
    {
        var accessTokenResult = GenerateAccessToken(user, roles);
        
        var refreshTokenString = GenerateSecureRandomString();
        var refreshTokenHash = HashToken(refreshTokenString);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Enforce Device Id uniqueness and 2-Device Limit natively
            await EnforceDeviceLimitAsync(user.Id, ipAddress, deviceId);

            var refreshToken = new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = refreshTokenHash,
                Status = TokenStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
                CreatedByIp = ipAddress,
                UserAgent = userAgent,
                DeviceId = deviceId
            };

            _context.UserRefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new AuthTokens(
                accessTokenResult.Token,
                accessTokenResult.Expiration,
                refreshTokenString, // Send raw token down to controller
                refreshToken.ExpiresAt,
                ToAuthUser(user, roles));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to generate and store tokens for User {UserId}", user.Id);
            throw;
        }
    }

    public async Task<Result<AuthTokens>> RefreshSessionAsync(string refreshToken, string? ipAddress, string? userAgent, string? deviceId)
    {
        var tokenHash = HashToken(refreshToken);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var storedToken = await _context.UserRefreshTokens
                .Include(r => r.User)
                .ThenInclude(u => u.RefreshTokens) // Needed for cascade revoking
                .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

            if (storedToken == null)
            {
                _logger.LogWarning("Refresh attempted with unknown token from IP {IP}", ipAddress);
                return Result<AuthTokens>.Unauthorized("Invalid token.");
            }

            // --- CONCURRENT REFRESH GRACE WINDOW ---
            // Two browser tabs can send the same refresh cookie at the same moment. If this token was
            // rotated only seconds ago, the other tab already holds the new cookie: tell the caller to
            // retry its request instead of treating it as a replay attack and killing the session.
            if (storedToken.Status == TokenStatus.Consumed)
            {
                var graceStart = DateTime.UtcNow.AddSeconds(-ConcurrentRefreshGraceSeconds);
                var rotatedJustNow = await _context.UserRefreshTokens
                    .AnyAsync(t => t.ReplacedByTokenId == storedToken.Id && t.CreatedAt >= graceStart);

                if (rotatedJustNow)
                {
                    _logger.LogInformation("Concurrent refresh for token {TokenId} within grace window.", storedToken.Id);
                    return Result<AuthTokens>.Conflict("Session was refreshed by another request. Retry.");
                }
            }

            // --- REPLAY ATTACK DETECTION ---
            if (storedToken.Status == TokenStatus.Consumed)
            {
                _logger.LogCritical("REPLAY ATTACK DETECTED! Token {TokenId} for User {UserId} was already consumed.", storedToken.Id, storedToken.UserId);
                
                var allActiveTokens = storedToken.User.RefreshTokens
                    .Where(t => t.Status == TokenStatus.Active || t.Status == TokenStatus.Consumed).ToList();

                foreach (var token in allActiveTokens)
                {
                    token.Status = TokenStatus.Invalidated;
                    token.ReasonRevoked = "Security Incident: Replay attack cascade termination.";
                    _context.Entry(token).State = EntityState.Modified;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                return Result<AuthTokens>.Failure(ResultErrorType.Forbidden, "Critical security vulnerability intercepted. Session chain terminated.");
            }

            if (storedToken.Status != TokenStatus.Active || storedToken.ExpiresAt < DateTime.UtcNow)
            {
                return Result<AuthTokens>.Unauthorized("Token is expired or no longer active.");
            }

            // Token is good. Rotate it (RTR).
            storedToken.Status = TokenStatus.Consumed;
            _context.Entry(storedToken).State = EntityState.Modified;

            // Generate new token pair
            string newRefreshTokenString = GenerateSecureRandomString();
            var newRefreshTokenHash = HashToken(newRefreshTokenString);

            var newRefreshToken = new UserRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = storedToken.UserId,
                TokenHash = newRefreshTokenHash,
                Status = TokenStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
                CreatedByIp = ipAddress,
                UserAgent = userAgent,
                DeviceId = deviceId,
                ReplacedByTokenId = storedToken.Id
            };

            _context.UserRefreshTokens.Add(newRefreshToken);

            var roles = await _userManager.GetRolesAsync(storedToken.User);
            var accessResult = GenerateAccessToken(storedToken.User, roles);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var finalTokens = new AuthTokens(
                accessResult.Token,
                accessResult.Expiration,
                newRefreshTokenString,
                newRefreshToken.ExpiresAt,
                ToAuthUser(storedToken.User, roles));

            return Result<AuthTokens>.Success(finalTokens);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error refreshing token for IP {IP}", ipAddress);
            throw;
        }
    }

    public async Task<Result<bool>> RevokeTokenAsync(string refreshToken, string? ipAddress, string? deviceId)
    {
        var tokenHash = HashToken(refreshToken);

        var storedToken = await _context.UserRefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

        if (storedToken == null)
        {
            _logger.LogWarning("Revoke attempted with unknown token from IP {IP}. DeviceId: {DeviceId}", ipAddress, deviceId);
            return Result<bool>.Failure(new[] { "Invalid token." });
        }

        if (storedToken.Status == TokenStatus.Active)
        {
            storedToken.Status = TokenStatus.Revoked;
            storedToken.ReasonRevoked = "Logged out by user.";
            _context.Entry(storedToken).State = EntityState.Modified;
            
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Token {TokenId} successfully revoked from IP {IP}. DeviceId: {DeviceId}", storedToken.Id, ipAddress, deviceId);
        }

        return Result<bool>.Success(true);
    }

    private async Task EnforceDeviceLimitAsync(Guid userId, string? ipAddress, string? newDeviceId)
    {
        var activeTokens = await _context.UserRefreshTokens
            .Where(t => t.UserId == userId && t.Status == TokenStatus.Active && t.ExpiresAt > DateTime.UtcNow)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

        // 1. If explicit DeviceId is provided, revoke any existing token for THIS device specifically
        if (!string.IsNullOrEmpty(newDeviceId))
        {
            var existingDeviceTokens = activeTokens.Where(t => t.DeviceId == newDeviceId).ToList();
            foreach (var existingToken in existingDeviceTokens)
            {
                existingToken.Status = TokenStatus.Revoked;
                existingToken.ReasonRevoked = "Replaced by new login from same explicit device.";
                _context.Entry(existingToken).State = EntityState.Modified;
                activeTokens.Remove(existingToken);
                
                _logger.LogInformation("Revoked token {TokenId} for User {UserId}. Replaced by same DeviceId: {DeviceId}.", existingToken.Id, userId, newDeviceId);
            }
        }

        // 2. Enforce global threshold: If at or above the threshold BEFORE adding the new one, revoke the oldest.
        // We allow MaxDevicesAllowed - 1 so the newly created token meets the exact cap.
        while (activeTokens.Count >= MaxDevicesAllowed)
        {
            var oldestToken = activeTokens.First();
            oldestToken.Status = TokenStatus.Revoked;
            oldestToken.ReasonRevoked = "Device Limit Exceeded";
            
            _context.Entry(oldestToken).State = EntityState.Modified;
            
            activeTokens.Remove(oldestToken);
            _logger.LogInformation("Revoked token {TokenId} for User {UserId}. Device limit exceeded from new connection on {IP}.", oldestToken.Id, userId, ipAddress);
        }
    }

    private (string Token, DateTime Expiration) GenerateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Name, user.UserName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        using var rsa = RSA.Create();
        rsa.FromXmlString(_jwtSettings.Rsa.PrivateKeyXml);
        var privateKey = new RsaSecurityKey(rsa);
                // IMPORTANT: Prevent the framework from caching the SignatureProvider globally.
        // If it caches it, subsequent requests will try to use this RSA instance after it has been disposed.
        var creds = new SigningCredentials(privateKey, SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        DateTime expires = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    private static AuthUser ToAuthUser(ApplicationUser user, IList<string> roles) =>
        new(user.Id, user.UserName ?? string.Empty, user.Email, user.FullName, roles.ToList());

    private string GenerateSecureRandomString(int length = 64)
    {
        var randomNumber = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    private string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}

