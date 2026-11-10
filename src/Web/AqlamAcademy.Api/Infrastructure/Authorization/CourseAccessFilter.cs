using AqlamAcademy.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AqlamAcademy.Api.Infrastructure.Authorization;

/// <summary>
/// Course endpoints: creating a course needs the organization's course-creation rule, and any endpoint with a
/// course {id} needs that course to be one the user may work on (see <see cref="ICourseAccess"/>). Runs after
/// the permission check, so the organization and membership are already known.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CourseAccessAttribute : TypeFilterAttribute
{
    public CourseAccessAttribute() : base(typeof(CourseAccessFilter)) { }
}

public sealed class CourseAccessFilter(ICourseAccess access) : IAsyncActionFilter
{
    public const string CreateAction = "InitializeDraft";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var token = context.HttpContext.RequestAborted;
        var allowed = context.ActionDescriptor.RouteValues.TryGetValue("action", out var action) && action == CreateAction
            ? await access.CanCreateAsync(token)
            : !context.RouteData.Values.TryGetValue("id", out var raw) || !Guid.TryParse(raw?.ToString(), out var id)
              || await access.CanWorkOnAsync(id, token);

        if (!allowed)
        {
            context.Result = new ObjectResult(new { succeeded = false, errors = new[] { "This course is managed by the school." } }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}
