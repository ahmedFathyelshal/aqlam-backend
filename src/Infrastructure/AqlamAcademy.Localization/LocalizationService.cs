using Microsoft.Extensions.Localization;
using AqlamAcademy.Application.Common.Interfaces;

namespace AqlamAcademy.Localization;

/// <summary>
/// Concrete implementation of ILocalizationService using .NET's IStringLocalizer.
/// This lives in Infrastructure (Localization project) and implements the
/// Application-defined interface (Dependency Inversion Principle).
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public LocalizationService(IStringLocalizer<SharedResource> localizer)
    {
        _localizer = localizer;
    }

    public string this[string key] => _localizer[key];

    public string GetLocalizedString(string key, params object[] arguments)
    {
        return _localizer[key, arguments];
    }
}
