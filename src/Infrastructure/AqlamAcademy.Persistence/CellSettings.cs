namespace AqlamAcademy.Infrastructure.Persistence;

/// <summary>
/// Settings for this regional deployment ("cell"), e.g. KSA hosted in-Kingdom or EG.
/// The same code runs in every cell; only configuration differs.
/// </summary>
public class CellSettings
{
    public const string SectionName = "Cell";

    public string Code { get; set; } = "KSA";
    /// <summary>Base domain for organization subdomains, e.g. "aqlam.app" → "nile.aqlam.app".</summary>
    public string BaseDomain { get; set; } = "localhost";
    public string DefaultCountryCode { get; set; } = "SA";
    public string DefaultCurrency { get; set; } = "SAR";
    public string DefaultTimeZone { get; set; } = "Asia/Riyadh";
}
