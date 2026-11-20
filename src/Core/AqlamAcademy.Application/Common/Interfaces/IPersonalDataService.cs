using AqlamAcademy.Application.Common.Models;

namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>Personal data kept by the platform: a downloadable copy, and anonymising an account when deletion is requested.</summary>
public interface IPersonalDataService
{
    /// <summary>A copy of what the platform holds about the user. The holder's own download also completes their open export request.</summary>
    Task<Result<object>> ExportAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Removes the person's identity from the account (name, contact details, sign-in) and ends their access. Payment and audit records stay, without the identity.</summary>
    Task<Result> AnonymizeAsync(Guid actorUserId, Guid userId, CancellationToken cancellationToken);
}
