namespace AqlamAcademy.Infrastructure.Persistence;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;

    public RsaSigningSettings Rsa { get; set; } = new();
}

public class RsaSigningSettings
{
    public string PrivateKeyXml { get; set; } = string.Empty;
    public string PublicKeyXml { get; set; } = string.Empty;
}
