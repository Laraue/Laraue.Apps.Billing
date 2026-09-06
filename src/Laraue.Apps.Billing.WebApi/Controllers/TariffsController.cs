using Laraue.Apps.Billing.WebApiServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Laraue.Apps.Billing.WebApi.Controllers;

[AllowAnonymous]
[ApiController]
[Route("/api/tariffs")]
public class TariffsController(ITariffService tariffService) : ControllerBase
{
    [HttpGet]
    public Task<GetServiceTariffsResponse> GetServiceTariffs(
        [FromQuery] GetServiceTariffsRequest request,
        CancellationToken cancellationToken)
    {
        return tariffService.GetServiceTariffs(request, cancellationToken);
    }
}
