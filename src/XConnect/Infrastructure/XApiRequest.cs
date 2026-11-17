using System.Globalization;
using System.Text;
using XConnect.Authentication;

namespace XConnect.Infrastructure;

/// <summary>Describes one X API call: where it goes, which authentication it accepts, and how to build its body.</summary>
internal sealed class XApiRequest
{
    private readonly List<KeyValuePair<string, string>> _query = [];

    private XApiRequest(HttpMethod method, string route, string path, XAuthMethods auth)
    {
        Method = method;
        Route = route;
        Path = path;
        Auth = auth;
    }

    public HttpMethod Method { get; }

    /// <summary>The route template, used as the rate-limit key.</summary>
    public string Route { get; }

    /// <summary>The concrete path with path parameters filled in.</summary>
    public string Path { get; }

    public XAuthMethods Auth { get; }

    public IReadOnlyList<KeyValuePair<string, string>> Query => _query;

    /// <summary>Builds the body. A factory, so retries get a fresh, unconsumed content.</summary>
    public Func<HttpContent>? Content { get; private set; }

    public static XApiRequest Create(HttpMethod method, string route, XAuthMethods auth, params (string Name, string Value)[] pathParameters)
    {
        var path = route;
        foreach (var (name, value) in pathParameters)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
            if (value is "." or "..")
            {
                // EscapeDataString leaves dots alone and Uri would collapse them, sending the request to a different route.
                throw new ArgumentException($"'{value}' is not a valid value for '{name}'.", name);
            }

            path = path.Replace("{" + name + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
        }

        return new XApiRequest(method, route, path, auth);
    }

    public XApiRequest WithContent(Func<HttpContent> content)
    {
        Content = content;
        return this;
    }

    public XApiRequest Add(string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _query.Add(new(name, value));
        }

        return this;
    }

    public XApiRequest Add(string name, IEnumerable<string>? values)
    {
        if (values is not null)
        {
            var joined = string.Join(',', values);
            if (joined.Length > 0)
            {
                _query.Add(new(name, joined));
            }
        }

        return this;
    }

    public XApiRequest Add(string name, int? value) =>
        value is null ? this : Add(name, value.Value.ToString(CultureInfo.InvariantCulture));

    public XApiRequest Add(string name, bool? value) =>
        value is null ? this : Add(name, value.Value ? "true" : "false");

    public XApiRequest Add(string name, DateTimeOffset? value) =>
        value is null ? this : Add(name, value.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    /// <summary>Adds the standard field and expansion selections.</summary>
    /// <param name="fields">The selection.</param>
    /// <param name="postFieldsName">
    /// The wire name for Post fields. The OpenAPI document calls it <c>post.fields</c> everywhere except the Post streams, which document only
    /// <c>tweet.fields</c>; those pass that name.
    /// </param>
    public XApiRequest Add(XFieldSelection? fields, string postFieldsName = "post.fields")
    {
        if (fields is null)
        {
            return this;
        }

        return Add(postFieldsName, fields.PostFields)
            .Add("user.fields", fields.UserFields)
            .Add("media.fields", fields.MediaFields)
            .Add("poll.fields", fields.PollFields)
            .Add("place.fields", fields.PlaceFields)
            .Add("list.fields", fields.ListFields)
            .Add("space.fields", fields.SpaceFields)
            .Add("dm_event.fields", fields.DmEventFields)
            .Add("community.fields", fields.CommunityFields)
            .Add("expansions", fields.Expansions);
    }

    public Uri BuildUri(Uri baseAddress)
    {
        var builder = new StringBuilder(Path);
        for (var i = 0; i < _query.Count; i++)
        {
            builder.Append(i == 0 ? '?' : '&')
                   .Append(Uri.EscapeDataString(_query[i].Key))
                   .Append('=')
                   .Append(Uri.EscapeDataString(_query[i].Value));
        }

        return new Uri(baseAddress, builder.ToString().TrimStart('/'));
    }
}
