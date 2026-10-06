using Laraue.Apps.Billing.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Billing.WebApiHost.Controllers;

[AllowAnonymous]
[ApiController]
[Route("/api/token-packs")]
public class TokenPacksController(ITokenPackService tokenPackService) : ControllerBase
{
    [HttpGet]
    public Task<GetTokenPacksResponse> GetTokenPacks(
        [FromQuery] GetTokenPacksRequest request,
        CancellationToken cancellationToken)
    {
        return tokenPackService.GetTokenPacks(request, cancellationToken);
    }
}
