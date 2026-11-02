namespace AqlamAcademy.Application.Common.Interfaces;

/// <summary>The organization (tenant) resolved for the current request, if any.</summary>
public interface ICurrentOrganization
{
    Guid? OrganizationId { get; }
}
