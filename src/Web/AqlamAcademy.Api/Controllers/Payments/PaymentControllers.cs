using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.Authorization;
using AqlamAcademy.Api.Infrastructure.DependencyInjection;
using AqlamAcademy.Application.Common.Authorization;
using AqlamAcademy.Application.Common.Models;
using AqlamAcademy.Application.Features.Payments;
using AqlamAcademy.Infrastructure.Persistence.Payments;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqlamAcademy.Api.Controllers.Payments;

/// <summary>The student's side of a purchase: status while waiting, and the test provider's pay/decline page.</summary>
[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController(IPaymentService payments) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => ToActionResult(await payments.GetAsync(id, cancellationToken));

    public record TestCompletion(bool Succeed);

    /// <summary>Only while the test provider is switched on (development and demos); no money moves.</summary>
    [HttpPost("{id:guid}/test-complete")]
    public async Task<IActionResult> CompleteTest(Guid id, [FromBody] TestCompletion request, CancellationToken cancellationToken) =>
        ToActionResult(await payments.CompleteTestPaymentAsync(id, request.Succeed, cancellationToken));

    private IActionResult ToActionResult(Result result) => result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>Payment provider notifications. The signature is checked by the provider adapter before anything changes.</summary>
[ApiController]
[Route("api/payments/webhooks")]
[AllowAnonymous]
public class PaymentWebhooksController(PaymentProviders providers, IPaymentService payments, ILogger<PaymentWebhooksController> logger) : ControllerBase
{
    [HttpPost("{provider}")]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        var adapter = providers.ByName(provider);
        if (adapter is null)
            return NotFound();

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var providerEvent = adapter.ParseNotification(body, Request.Headers["X-Signature"].FirstOrDefault());
        if (providerEvent is null)
        {
            logger.LogWarning("Rejected a {Provider} notification with a bad signature or body.", provider);
            return Unauthorized();
        }

        logger.LogInformation("Payment notification from {Provider}: {Body}", provider, body);
        var result = await payments.ApplyAsync(providerEvent, cancellationToken);
        // Unknown payments are acknowledged too, so the provider stops retrying; they are in the log.
        return result.Succeeded || result.ErrorType == ResultErrorType.NotFound ? Ok() : this.ToFailureResult(result);
    }
}

/// <summary>Sales, commission, earnings and (independent teachers) the monthly fee statement.</summary>
[ApiController]
[Route("api/orgs/{org}/earnings")]
[Route("api/earnings")]
[HasPermission(Permissions.FinanceView)]
public class EarningsController(IEarningsService earnings) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await earnings.GetAsync(cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }
}

/// <summary>Activation code batches of one course (owners, admins and coordinators; the teacher in a personal academy).</summary>
[ApiController]
[Route("api/orgs/{org}/courses/{id:guid}/code-batches")]
[Route("api/courses/{id:guid}/code-batches")]
[HasPermission(Permissions.CoursesPublish)]
[CourseAccess]
public class CodeBatchesController(IActivationCodeService codes, ISender mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken) => ToActionResult(await codes.ListBatchesAsync(id, cancellationToken));

    public record CreateBatchRequest(string Name, int Quantity, DateTime? ExpiresAtUtc);

    /// <summary>The plain codes are in this response only: they are stored hashed.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(Guid id, [FromBody] CreateBatchRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return ToActionResult(await mediator.Send(new CreateCodeBatchCommand(id, request.Name ?? string.Empty, request.Quantity, request.ExpiresAtUtc), cancellationToken));
    }

    [HttpPost("{batchId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, Guid batchId, CancellationToken cancellationToken) =>
        ToActionResult(await codes.CancelBatchAsync(id, batchId, cancellationToken));

    /// <summary>Is this card used, and by whom?</summary>
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup(Guid id, [FromQuery] string code, CancellationToken cancellationToken) =>
        ToActionResult(await codes.LookupAsync(id, code ?? string.Empty, cancellationToken));

    private IActionResult ToActionResult(Result result) => result.Succeeded ? Ok(result) : this.ToFailureResult(result);
}

/// <summary>A student types the code from a card.</summary>
[ApiController]
[Route("api/codes")]
[Authorize]
public class CodesController(ISender mediator) : ControllerBase
{
    [HttpPost("redeem")]
    [EnableRateLimiting(PresentationDependencyInjection.AuthRateLimitPolicy)]
    public async Task<IActionResult> Redeem([FromBody] RedeemCodeCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);
        return result.Succeeded ? Ok(result) : this.ToFailureResult(result);
    }
}
