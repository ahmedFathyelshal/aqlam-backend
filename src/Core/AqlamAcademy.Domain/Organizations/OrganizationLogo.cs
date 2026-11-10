namespace AqlamAcademy.Domain.Organizations;

/// <summary>
/// An uploaded school logo. Kept in its own table so the image never loads with the organization row, and in the
/// database because the hosts we use have no persistent disk. Small raster images only (no SVG: it can carry script).
/// </summary>
public class OrganizationLogo
{
    public const int MaxBytes = 256 * 1024;

    public Guid OrganizationId { get; set; }
    public byte[] Content { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>PNG, JPEG or WebP by their first bytes, whatever the file name or browser claims; null otherwise.</summary>
    public static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    /// <summary>Where the app serves an uploaded logo; the version changes on every upload so caches refresh.</summary>
    public static string PublicPath(string slug, DateTime updatedAtUtc) => $"/api/organizations/{slug}/logo?v={updatedAtUtc.Ticks}";

    public static bool IsPublicPath(string? url) => url?.StartsWith("/api/organizations/", StringComparison.Ordinal) == true;
}
