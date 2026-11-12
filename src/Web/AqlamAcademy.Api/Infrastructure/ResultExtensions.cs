using AqlamAcademy.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace AqlamAcademy.Api.Infrastructure;

public static class ResultExtensions
{
    /// <summary>
    /// Maps a failed <see cref="Result"/> to the HTTP status matching its error type.
    /// </summary>
    public static ActionResult ToFailureResult(this ControllerBase controller, Result result) => result.ErrorType switch
    {
        ResultErrorType.NotFound => controller.NotFound(result),
        ResultErrorType.Forbidden => controller.StatusCode(StatusCodes.Status403Forbidden, result),
        ResultErrorType.Unauthorized => controller.Unauthorized(result),
        ResultErrorType.Conflict => controller.Conflict(result),
        ResultErrorType.TooManyRequests => controller.StatusCode(StatusCodes.Status429TooManyRequests, result),
        _ => controller.BadRequest(result)
    };
}
