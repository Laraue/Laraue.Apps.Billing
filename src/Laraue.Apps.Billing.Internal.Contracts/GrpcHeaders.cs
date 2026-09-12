namespace Laraue.Apps.Billing.Internal.Contracts;

/// <summary>
/// Metadata header names used across this contract, shared between clients (attach via an
/// interceptor, not per-call) and a gRPC service (reads via
/// <c>ServerCallContext.RequestHeaders</c>).  gRPC metadata keys must be lowercase.
/// </summary>
public static class GrpcHeaders
{
    /// <summary>
    /// Which calling service this request is from, as the numeric value of <see cref="ServiceId"/>
    /// (e.g. "1" for <see cref="ServiceId.LaraueBoards"/>) - identifies the caller once per
    /// connection/client rather than being repeated on every request message. Same header name as
    /// the Laraue Identity service uses for the same purpose, though each service interprets the
    /// raw value against its own independent <c>ServiceId</c> enum - don't assume a given id means
    /// the same service across services.
    /// </summary>
    public const string ServiceIdHeaderName = "x-laraue-service-id";
}
