using System.Security.Claims;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Interfaces;
using AqlamAcademy.Domain.Organizations;
using AqlamAcademy.Infrastructure.Persistence.MultiTenancy;
using Finbuckle.MultiTenant.AspNetCore.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace AqlamAcademy.Api.Infrastructure.Authorization;

/// <summary>Requires a permission inside the organization resolved for this request.</summary>
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission) => Policy = PolicyPrefix + permission;
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Failure reason meaning "no tenant resolved or not a member": answered with 404 so org ids aren't confirmed.</summary>
public sealed class NotAMemberReason(IAuthorizationHandler handler) : AuthorizationFailureReason(handler, "Not a member of this organization.");

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.PolicyPrefix.Length..]))
            .Build();
    }
}

public sealed class PermissionHandler(IHttpContextAccessor httpContextAccessor, IMembershipService memberships)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var tenant = httpContext?.GetTenantInfo<AqlamTenantInfo>();
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");

        if (tenant is null || !Guid.TryParse(userIdValue, out var userId))
        {
            context.Fail(new NotAMemberReason(this));
            return;
        }

        var roles = await memberships.GetActiveRolesAsync(userId, tenant.OrganizationId, httpContext!.RequestAborted);

        if (roles == OrganizationRole.None)
        {
            context.Fail(new NotAMemberReason(this));
            return;
        }

        if (Permissions.Grants(roles, requirement.Permission))
            context.Succeed(requirement);
        else
            context.Fail(); // member without this permission → 403
    }
}

/// <summary>Turns "not a member" failures into 404 instead of 403.</summary>
public sealed class NotFoundForNonMembersResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden &&
            authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r is NotAMemberReason) == true)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
