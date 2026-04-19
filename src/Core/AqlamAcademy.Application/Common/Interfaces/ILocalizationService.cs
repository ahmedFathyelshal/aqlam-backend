namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>
/// Abstraction for localization services. Defined in Application (Core) layer,
/// implemented in Infrastructure (Localization project).
/// This follows the Dependency Inversion Principle — Application depends on the
/// abstraction, not on the concrete Localization infrastructure.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets a localized string by its key.
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// Gets a localized string by its key with format arguments.
    /// </summary>
    string GetLocalizedString(string key, params object[] arguments);
}
