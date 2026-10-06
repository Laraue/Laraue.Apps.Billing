using Laraue.Apps.Billing.WebApiServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Laraue.Apps.Billing.WebApiHost;

/// <summary>
/// Binds a <see cref="PaymentCallback"/> from a provider's request: the provider from the route, the
/// query string and form values merged into the parameters, the headers and a raw body (only when the
/// request is not a form).
/// </summary>
public sealed class PaymentCallbackModelBinder : IModelBinder
{
    private const string ProviderRouteKey = "provider";

    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var request = bindingContext.HttpContext.Request;
        var cancellationToken = bindingContext.HttpContext.RequestAborted;

        var parameters = request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());

        string? body = null;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            foreach (var (name, value) in form)
            {
                parameters[name] = value.ToString();
            }
        }
        else if (request.ContentLength is > 0)
        {
            using var reader = new StreamReader(request.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        bindingContext.Result = ModelBindingResult.Success(new PaymentCallback
        {
            Provider = bindingContext.ActionContext.RouteData.Values[ProviderRouteKey]?.ToString() ?? string.Empty,
            Parameters = parameters,
            Headers = request.Headers.ToDictionary(x => x.Key, x => x.Value.ToString()),
            Body = body,
        });
    }
}

/// <summary>
/// Marks an action parameter as the <see cref="PaymentCallback"/> of the request.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromPaymentCallbackAttribute() : ModelBinderAttribute(typeof(PaymentCallbackModelBinder));
