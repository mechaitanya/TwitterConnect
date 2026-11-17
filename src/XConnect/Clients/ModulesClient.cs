using System.Net.Http.Json;
using XConnect.Authentication;
using XConnect.Infrastructure;
using XConnect.Models;
using XConnect.Serialization;

namespace XConnect.Clients;

/// <summary>
/// Batch compliance jobs (<c>/2/compliance/jobs</c>). These require an app-only Bearer token and a plan that includes
/// the Compliance API. The pre-signed upload and download URLs are plain HTTPS endpoints that you call yourself.
/// </summary>
public interface IXComplianceClient
{
    /// <summary>Creates a job. <paramref name="type"/> is <c>tweets</c> or <c>users</c>.</summary>
    Task<XResponse<XComplianceJob>> CreateJobAsync(string type, string? name = null, bool? resumable = null, CancellationToken cancellationToken = default);

    /// <summary>Lists jobs of a type, optionally filtered by status (<c>created</c>, <c>in_progress</c>, <c>failed</c>, <c>complete</c>).</summary>
    Task<XResponse<IReadOnlyList<XComplianceJob>>> GetJobsAsync(string type, string? status = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a job by ID.</summary>
    Task<XResponse<XComplianceJob>> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Cancels a job that is awaiting upload or processing (<c>DELETE /2/compliance/jobs/{id}</c>).</summary>
    Task<XResponse<XComplianceJob>> CancelJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads the ID list to the job's pre-signed <see cref="XComplianceJob.UploadUrl"/> with an HTTP <c>PUT</c> of <c>application/octet-stream</c>.
    /// The pre-signed URL carries its own authorisation, so no <c>Authorization</c> header is sent to it.
    /// </summary>
    /// <param name="uploadUrl">The <c>upload_url</c> of the job.</param>
    /// <param name="ids">The IDs, one per line.</param>
    /// <param name="cancellationToken">A token to cancel the upload.</param>
    /// <exception cref="HttpRequestException">The upload was rejected.</exception>
    Task UploadIdsAsync(Uri uploadUrl, Stream ids, CancellationToken cancellationToken = default);

    /// <summary>Opens the results of a completed job from its pre-signed <see cref="XComplianceJob.DownloadUrl"/>. Dispose the stream when done; it is not buffered.</summary>
    /// <exception cref="HttpRequestException">The download was rejected.</exception>
    Task<Stream> DownloadResultsAsync(Uri downloadUrl, CancellationToken cancellationToken = default);
}

/// <summary>Trending topics.</summary>
public interface IXTrendsClient
{
    /// <summary>Gets trends for a Where On Earth ID (<c>GET /2/trends/by/woeid/{woeid}</c>).</summary>
    /// <param name="woeid">The WOEID, for example 1 for worldwide.</param>
    /// <param name="maxTrends">1 to 50.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<XResponse<IReadOnlyList<XTrend>>> GetByWoeidAsync(int woeid, int? maxTrends = null, CancellationToken cancellationToken = default);

    /// <summary>Gets trends personalised for the authenticated user (<c>GET /2/users/personalized_trends</c>). Requires user-context authentication.</summary>
    Task<XResponse<IReadOnlyList<XPersonalizedTrend>>> GetPersonalizedAsync(IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default);
}

/// <summary>News stories (<c>/2/news</c>).</summary>
public interface IXNewsClient
{
    /// <summary>Searches news stories.</summary>
    /// <param name="query">The search query.</param>
    /// <param name="maxResults">Page size.</param>
    /// <param name="maxAgeHours">Only stories updated within this many hours.</param>
    /// <param name="fields">The <c>news.fields</c> to return (the API returns only what you request).</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<XResponse<IReadOnlyList<XNewsStory>>> SearchAsync(string query, int? maxResults = null, int? maxAgeHours = null, IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets a story by ID.</summary>
    Task<XResponse<XNewsStory>> GetAsync(string newsId, IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default);
}

/// <summary>API usage and credit balances.</summary>
public interface IXUsageClient
{
    /// <summary>Gets Post-cap usage for the project (<c>GET /2/usage/tweets</c>). Requires an app-only Bearer token.</summary>
    /// <param name="days">How many days of daily usage to return.</param>
    /// <param name="fields">The <c>usage.fields</c> to return.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    Task<XResponse<XPostUsage>> GetPostUsageAsync(int? days = null, IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Gets credit balances (<c>GET /2/usage/credits</c>). Accepts a Bearer token or OAuth 2.0 user context.</summary>
    Task<XResponse<XCreditBalance>> GetCreditsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Communities.</summary>
public interface IXCommunitiesClient
{
    /// <summary>Gets a Community by ID. Use <see cref="XFieldSelection.CommunityFields"/> to choose fields.</summary>
    Task<XResponse<XCommunity>> GetAsync(string communityId, XFieldSelection? fields = null, CancellationToken cancellationToken = default);

    /// <summary>Searches Communities (user-context authentication).</summary>
    Task<XResponse<IReadOnlyList<XCommunity>>> SearchAsync(string query, XPageQuery? options = null, CancellationToken cancellationToken = default);
}

internal sealed class XComplianceClient(XApiTransport transport) : IXComplianceClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XComplianceJob>> CreateJobAsync(string type, string? name = null, bool? resumable = null, CancellationToken cancellationToken = default)
    {
        if (type is not ("tweets" or "users"))
        {
            throw new ArgumentException("The job type must be 'tweets' or 'users'.", nameof(type));
        }

        var request = XApiRequest.Create(HttpMethod.Post, "/2/compliance/jobs", XAuthMethods.AppOnly)
            .WithContent(() => JsonContent.Create(new JobBody(type, name, resumable), Json.JobBody));
        return transport.SendAsync(request, Json.EnvelopeXComplianceJob, cancellationToken);
    }

    public Task<XResponse<IReadOnlyList<XComplianceJob>>> GetJobsAsync(string type, string? status = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/compliance/jobs", XAuthMethods.AppOnly).Add("type", type).Add("status", status);
        return transport.SendAsync(request, Json.EnvelopeXComplianceJobList, cancellationToken);
    }

    public Task<XResponse<XComplianceJob>> GetJobAsync(string jobId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/compliance/jobs/{id}", XAuthMethods.AppOnly, ("id", jobId)),
            Json.EnvelopeXComplianceJob,
            cancellationToken);

    public Task<XResponse<XComplianceJob>> CancelJobAsync(string jobId, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Delete, "/2/compliance/jobs/{id}", XAuthMethods.AppOnly, ("id", jobId)),
            Json.EnvelopeXComplianceJob,
            cancellationToken);

    public async Task UploadIdsAsync(Uri uploadUrl, Stream ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uploadUrl);
        ArgumentNullException.ThrowIfNull(ids);
        RequireHttps(uploadUrl);
        using var content = new StreamContent(ids);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = content };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var limit = transport.Options.TransferTimeout;
        if (limit != Timeout.InfiniteTimeSpan)
        {
            deadline.CancelAfter(limit);
        }

        try
        {
            // Only the status matters, so do not buffer the (unused) response body.
            using var response = await transport.SendUnauthenticatedAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The compliance upload did not finish within {limit}.", ex);
        }
    }

    public async Task<Stream> DownloadResultsAsync(Uri downloadUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(downloadUrl);
        RequireHttps(downloadUrl);
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        HttpResponseMessage? response = null;
        var limit = transport.Options.TransferTimeout;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (limit != Timeout.InfiniteTimeSpan)
            {
                deadline.CancelAfter(limit);
            }

            try
            {
                response = await transport.SendUnauthenticatedAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
                return new ResponseOwningStream(stream, response, request, limit);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The compliance download did not start within {limit}.", ex);
            }
        }
        catch
        {
            response?.Dispose();
            request.Dispose();
            throw;
        }
    }

    private static void RequireHttps(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The pre-signed URL must be an absolute HTTPS URL.", nameof(url));
        }
    }

    internal sealed record JobBody(string Type, string? Name, bool? Resumable);

    /// <summary>Disposes the HTTP response together with the stream, so a streaming download never leaks a connection.</summary>
    private sealed class ResponseOwningStream(Stream inner, HttpResponseMessage response, HttpRequestMessage request, TimeSpan stallLimit) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stallLimit == Timeout.InfiniteTimeSpan)
            {
                return await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            // A stall limit between reads, not a total limit: a large download that keeps making progress is never cut off.
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(stallLimit);
            try
            {
                return await inner.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The compliance download stalled for {stallLimit}.", ex);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
                request.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

internal sealed class XTrendsClient(XApiTransport transport) : IXTrendsClient
{
    public Task<XResponse<IReadOnlyList<XPersonalizedTrend>>> GetPersonalizedAsync(IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/users/personalized_trends", XAuthMethods.UserContext).Add("personalized_trend.fields", fields),
            XConnectJsonContext.Default.EnvelopeXPersonalizedTrendList,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XTrend>>> GetByWoeidAsync(int woeid, int? maxTrends = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(woeid);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/trends/by/woeid/{woeid}", XAuthMethods.OAuth2User | XAuthMethods.AppOnly,
            ("woeid", woeid.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Add("max_trends", maxTrends);
        return transport.SendAsync(request, XConnectJsonContext.Default.EnvelopeXTrendList, cancellationToken);
    }
}

internal sealed class XNewsClient(XApiTransport transport) : IXNewsClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<IReadOnlyList<XNewsStory>>> SearchAsync(string query, int? maxResults = null, int? maxAgeHours = null, IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/news/search", XAuthMethods.Any)
            .Add("query", query).Add("max_results", maxResults).Add("max_age_hours", maxAgeHours).Add("news.fields", fields);
        return transport.SendAsync(request, Json.EnvelopeXNewsStoryList, cancellationToken);
    }

    public Task<XResponse<XNewsStory>> GetAsync(string newsId, IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/news/{id}", XAuthMethods.Any, ("id", newsId)).Add("news.fields", fields),
            Json.EnvelopeXNewsStory,
            cancellationToken);
}

internal sealed class XUsageClient(XApiTransport transport) : IXUsageClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XPostUsage>> GetPostUsageAsync(int? days = null, IReadOnlyList<string>? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/usage/tweets", XAuthMethods.AppOnly).Add("days", days).Add("usage.fields", fields),
            Json.EnvelopeXPostUsage,
            cancellationToken);

    public Task<XResponse<XCreditBalance>> GetCreditsAsync(CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/usage/credits", XAuthMethods.OAuth2User | XAuthMethods.AppOnly),
            Json.EnvelopeXCreditBalance,
            cancellationToken);
}

internal sealed class XCommunitiesClient(XApiTransport transport) : IXCommunitiesClient
{
    private static readonly XConnectJsonContext Json = XConnectJsonContext.Default;

    public Task<XResponse<XCommunity>> GetAsync(string communityId, XFieldSelection? fields = null, CancellationToken cancellationToken = default) =>
        transport.SendAsync(
            XApiRequest.Create(HttpMethod.Get, "/2/communities/{id}", XAuthMethods.Any, ("id", communityId)).Add(fields),
            Json.EnvelopeXCommunity,
            cancellationToken);

    public Task<XResponse<IReadOnlyList<XCommunity>>> SearchAsync(string query, XPageQuery? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var request = XApiRequest.Create(HttpMethod.Get, "/2/communities/search", XAuthMethods.UserContext).Add("query", query);
        if (options is not null)
        {
            request.Add("max_results", options.MaxResults).Add("pagination_token", options.PaginationToken);
        }

        return transport.SendAsync(request, Json.EnvelopeXCommunityList, cancellationToken);
    }
}
