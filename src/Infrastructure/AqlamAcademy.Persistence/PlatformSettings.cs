namespace AqlamAcademy.Infrastructure.Persistence;

public class PlatformSettings
{
    public const string SectionName = "Platform";

    /// <summary>
    /// Optional first platform administrator, created at startup if missing (dev, tests, first deployment).
    /// In production the password must come from a secret store and be changed after first sign-in.
    /// </summary>
    public BootstrapAdminSettings? BootstrapAdmin { get; set; }
}

public class BootstrapAdminSettings
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
