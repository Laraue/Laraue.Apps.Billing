using Laraue.Apps.Billing.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Billing.WebApiHost.Controllers;

/// <summary>
/// Public addresses a payment provider calls. The provider is part of the route, so another provider
/// gets its own addresses without any change here.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("/api/payments/{provider}")]
public class PaymentsController(IPaymentsService paymentsService) : ControllerBase
{
    /// <summary>
    /// The provider's server-to-server notification about a payment ("ResultURL" for Robokassa).
    /// The response body is the acknowledgement the provider expects.
    /// </summary>
    [HttpGet("notify")]
    [HttpPost("notify")]
    public async Task<ContentResult> Notify(
        [FromPaymentCallback] PaymentCallback callback,
        CancellationToken cancellationToken = default)
    {
        return Content(await paymentsService.HandleNotification(callback, cancellationToken), "text/plain");
    }

    /// <summary>
    /// Where the provider sends the customer after a successful payment.
    /// </summary>
    [HttpGet("success")]
    [HttpPost("success")]
    public async Task<IActionResult> Success(
        [FromPaymentCallback] PaymentCallback callback,
        CancellationToken cancellationToken = default)
    {
        return Redirect(await paymentsService.GetSuccessUrl(callback, cancellationToken));
    }

    /// <summary>
    /// Where the provider sends the customer after a failed or cancelled payment.
    /// </summary>
    [HttpGet("fail")]
    [HttpPost("fail")]
    public async Task<IActionResult> Fail(
        [FromPaymentCallback] PaymentCallback callback,
        CancellationToken cancellationToken = default)
    {
        return Redirect(await paymentsService.GetFailUrl(callback, cancellationToken));
    }
}
