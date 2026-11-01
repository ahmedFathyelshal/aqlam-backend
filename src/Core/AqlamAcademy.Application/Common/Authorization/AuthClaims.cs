namespace AqlamAcademy.Application.Common.Authorization;

public static class AuthClaims
{
    /// <summary>
    /// Organization a session is bound to when it was created on that organization's own (sub)domain.
    /// Not "tid": the JWT handler maps an inbound "tid" claim to a Microsoft tenant-id claim type.
    /// </summary>
    public const string OrganizationTenantId = "otid";
}
