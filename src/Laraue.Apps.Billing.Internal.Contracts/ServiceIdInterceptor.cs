using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Laraue.Apps.Billing.Internal.Contracts;

/// <summary>
/// Attaches the calling service's id as the <see cref="GrpcHeaders.ServiceIdHeaderName"/> metadata
/// header on every outgoing call - register once per client (e.g.
/// <c>AddGrpcClient&lt;TokenService.TokenServiceClient&gt;(...).AddInterceptor(() =>
/// new ServiceIdInterceptor(ServiceId.LaraueBoards))</c>) instead of setting it on every request.
/// </summary>
public sealed class ServiceIdInterceptor(ServiceId serviceId) : Interceptor
{
    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithServiceIdHeader(context));
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return continuation(request, WithServiceIdHeader(context));
    }

    private ClientInterceptorContext<TRequest, TResponse> WithServiceIdHeader<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var headers = context.Options.Headers ?? new Metadata();
        headers.Add(GrpcHeaders.ServiceIdHeaderName, ((int)serviceId).ToString());

        return new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(headers));
    }
}
