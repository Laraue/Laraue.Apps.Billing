using Grpc.Core;
using ContractsServiceId = Laraue.Apps.Billing.Internal.Contracts.ServiceId;
using DomainServiceId = Laraue.Apps.Billing.DataAccess.Entities.ServiceId;
using GrpcHeaders = Laraue.Apps.Billing.Internal.Contracts.GrpcHeaders;

namespace Laraue.Apps.Billing.InternalApiServices;

/// <summary>
/// Shared parsing helpers for gRPC adapters in this project - protobuf has no native UUID type,
/// so every id crosses the wire as a <see langword="string"/> and gets parsed back to a
/// <see cref="Guid"/> here, consistently, instead of each adapter re-implementing it. Also maps
/// the wire <see cref="ContractsServiceId"/> enum onto its domain equivalent, for the same reason.
/// </summary>
internal static class GrpcParsing
{
    public static Guid ParseGuid(string value, string fieldName) =>
        Guid.TryParse(value, out var guid)
            ? guid
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"'{fieldName}' is not a valid GUID."));

    public static DomainServiceId ToDomainServiceId(ContractsServiceId serviceId) => serviceId switch
    {
        ContractsServiceId.LaraueBoards => DomainServiceId.LaraueBoards,
        ContractsServiceId.MarkdownTranslator => DomainServiceId.MarkdownTranslator,
        _ => throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown service '{serviceId}'.")),
    };

    /// <summary>
    /// Reads the calling service's id from the <see cref="GrpcHeaders.ServiceIdHeaderName"/>
    /// metadata header - for contracts (like <c>token.proto</c>) that identify the caller once per
    /// client channel via a header/interceptor rather than a request field. See the note atop
    /// <c>token.proto</c>.
    /// </summary>
    public static DomainServiceId ReadDomainServiceId(ServerCallContext context)
    {
        var header = context.RequestHeaders.Get(GrpcHeaders.ServiceIdHeaderName)?.Value;

        if (header is null || !int.TryParse(header, out var rawServiceId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Missing or invalid '{GrpcHeaders.ServiceIdHeaderName}' header."));
        }

        return ToDomainServiceId((ContractsServiceId)rawServiceId);
    }
}
