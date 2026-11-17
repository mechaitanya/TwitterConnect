using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect;

/// <summary>Inspects and terminates the application's streaming connections. App-only. Terminating connections is immediate and affects every consumer of the stream.</summary>
public interface IXConnectionsClient
{
    /// <summary>Gets active and historical connections (<c>GET /2/connections</c>). <paramref name="status"/> and <paramref name="endpoints"/> narrow the list; pass the values X documents.</summary>
    Task<XResponse<IReadOnlyList<XConnection>>> GetHistoryAsync(string? status = null, IReadOnlyList<string>? endpoints = null, IReadOnlyList<string>? connectionFields = null, int? maxResults = null, string? paginationToken = null, CancellationToken cancellationToken = default);

    /// <summary>Terminates the given connections by UUID (<c>DELETE /2/connections</c>).</summary>
    Task<XResponse<XConnectionKillSummary>> TerminateAsync(IReadOnlyList<string> connectionIds, CancellationToken cancellationToken = default);

    /// <summary>Terminates every connection of one streaming endpoint; use a constant from <see cref="XStreamEndpoints"/>.</summary>
    Task<XResponse<XConnectionKillSummary>> TerminateByEndpointAsync(string endpointId, CancellationToken cancellationToken = default);

    /// <summary>Terminates all of the application's streaming connections (<c>DELETE /2/connections/all</c>).</summary>
    Task<XResponse<XConnectionKillSummary>> TerminateAllAsync(CancellationToken cancellationToken = default);
}

internal sealed class XConnectionsClient(XApiTransport transport) : IXConnectionsClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<IReadOnlyList<XConnection>>> GetHistoryAsync(string? status = null, IReadOnlyList<string>? endpoints = null, IReadOnlyList<string>? connectionFields = null, int? maxResults = null, string? paginationToken = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/connections", XAuthMethods.AppOnly)
                .Add("status", status).Add("endpoints", endpoints).Add("connection.fields", connectionFields).Add("max_results", maxResults).Add("pagination_token", paginationToken),
            Json.EnvelopeXConnectionList,
            cancellationToken);

    public Task<XResponse<XConnectionKillSummary>> TerminateAsync(IReadOnlyList<string> connectionIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionIds);
        if (connectionIds.Count == 0 || connectionIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Provide at least one connection ID, none blank.", nameof(connectionIds));
        }

        var ids = connectionIds.ToArray();
        var request = XApiRequest.Create(HttpMethod.Delete, "/2/connections", XAuthMethods.AppOnly)
            .WithContent(() => JsonContent.Create(new KillBody(ids), Json.KillBody));
        return transport.SendAsync(request, Json.EnvelopeXConnectionKillSummary, cancellationToken);
    }

    public Task<XResponse<XConnectionKillSummary>> TerminateByEndpointAsync(string endpointId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        if (!XStreamEndpoints.All.Contains(endpointId))
        {
            throw new ArgumentException($"'{endpointId}' is not a streaming endpoint ID X documents; see XStreamEndpoints.", nameof(endpointId));
        }

        return transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/connections/{endpoint_id}", XAuthMethods.AppOnly, ("endpoint_id", endpointId)),
            Json.EnvelopeXConnectionKillSummary,
            cancellationToken);
    }

    public Task<XResponse<XConnectionKillSummary>> TerminateAllAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync(XApiRequest.Create(HttpMethod.Delete, "/2/connections/all", XAuthMethods.AppOnly), Json.EnvelopeXConnectionKillSummary, cancellationToken);

    internal sealed record KillBody(string[] Uuids);
}
