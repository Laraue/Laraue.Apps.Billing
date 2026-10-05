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
    public async Task<ContentResult> Notify(string provider, CancellationToken cancellationToken = default)
    {
        var parameters = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());

        string? body = null;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            foreach (var (name, value) in form)
            {
                parameters[name] = value.ToString();
            }
        }
        else if (Request.ContentLength is > 0)
        {
            using var reader = new StreamReader(Request.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var acknowledgement = await paymentsService.HandleNotification(
            new HandlePaymentNotificationRequest
            {
                Provider = provider,
                Parameters = parameters,
                Headers = Request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString()),
                Body = body,
            },
            cancellationToken);

        return Content(acknowledgement, "text/plain");
    }

    /// <summary>
    /// Where the provider sends the customer after a successful payment.
    /// </summary>
    [HttpGet("success")]
    [HttpPost("success")]
    public IActionResult Success(string provider)
    {
        return Redirect(paymentsService.GetSuccessUrl(provider));
    }

    /// <summary>
    /// Where the provider sends the customer after a failed or cancelled payment.
    /// </summary>
    [HttpGet("fail")]
    [HttpPost("fail")]
    public IActionResult Fail(string provider)
    {
        return Redirect(paymentsService.GetFailUrl(provider));
    }
}
