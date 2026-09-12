using Grpc.Core;
using ContractsServiceId = Laraue.Apps.Billing.Internal.Contracts.ServiceId;
using DomainServiceId = Laraue.Apps.Billing.DataAccess.Entities.ServiceId;

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
}
