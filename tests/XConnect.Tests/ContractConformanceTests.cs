using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit.Abstractions;

namespace XConnect.Tests;

/// <summary>
/// Compares the SDK's request and response contracts with the official OpenAPI schemas (through <c>docs/spec-snapshot.json</c>). Together with the
/// request audit in <see cref="QueueHandler"/> (query parameters, types, enums, ranges, body shape, required fields on every request the unit tests send)
/// this is the automated evidence that the models and parameters match the specification.
/// </summary>
public sealed partial class ContractConformanceTests(ITestOutputHelper output)
{
    private static readonly Assembly Sdk = typeof(XConnectClient).Assembly;

    [GeneratedRegex(@"^\| (GET|POST|PUT|DELETE) \| `([^`]+)` \| Supported \|[^|]*\|[^|]*\| `([^`]+)` \|", RegexOptions.Multiline)]
    private static partial Regex SupportedRow();

    private static IEnumerable<(string Key, string ResponseType)> SupportedOperations() =>
        SupportedRow().Matches(File.ReadAllText(Path.Combine(SpecConformanceTests.RepositoryRoot, "docs/api-coverage.md")))
            .Select(m => ($"{m.Groups[1].Value} {m.Groups[2].Value}", m.Groups[3].Value));

    private static Type? FindModel(string name) =>
        Sdk.GetExportedTypes().FirstOrDefault(t => t.Name == name && t.IsClass && t.Namespace?.StartsWith("XConnect", StringComparison.Ordinal) == true);

    [Fact]
    public void EverySupportedOperation_HasASnapshotContract()
    {
        var contract = SpecContract.Instance;

        SupportedOperations().Where(o => contract.Find(o.Key)?.Contract is null).Should().BeEmpty("regenerate with tools/generate_coverage.py");
    }

    [Fact]
    public void ResponseModels_OnlyDeclareFieldsTheSpecificationDefines_AndModelEveryRequiredField()
    {
        var contract = SpecContract.Instance;
        var problems = new List<string>();
        var checkedOperations = 0;
        var unmodelled = new SortedDictionary<string, int>();
        var gapNames = new SortedDictionary<string, string[]>();

        foreach (var (key, responseType) in SupportedOperations())
        {
            var typeName = responseType.EndsWith("[]", StringComparison.Ordinal) ? responseType[..^2] : responseType;
            if (typeName.Contains('(') || typeName == "-" || FindModel(typeName) is not { } model)
            {
                continue;
            }

            var response = contract.Find(key)!.Contract?["response"];
            if (response is null)
            {
                continue;
            }

            var data = contract.Shape(response).Props.GetValueOrDefault("data");
            if (data is null)
            {
                continue;
            }

            var dataSchema = contract.Resolve(data).AsObject();
            if (dataSchema["items"] is { } items)
            {
                dataSchema = contract.Resolve(items).AsObject();
            }

            checkedOperations++;
            var gaps = new List<string>();
            Compare(contract, model, dataSchema, $"{key} -> {model.Name}", [], problems, gaps);
            unmodelled[$"{key} -> {model.Name}"] = gaps.Count;
            if (gaps.Count > 0)
            {
                gapNames[model.Name] = [.. gaps.Distinct().Order()];
            }
        }

        checkedOperations.Should().BeGreaterThan(60, "most supported operations return a typed model");
        output.WriteLine($"Compared {checkedOperations} response models. Documented fields not modelled (informational): {unmodelled.Sum(kv => kv.Value)} across {unmodelled.Count(kv => kv.Value > 0)} operations.");
        foreach (var (key, count) in unmodelled.Where(kv => kv.Value > 0))
        {
            output.WriteLine($"  {count,3} unmodelled  {key}");
        }

        foreach (var (name, fields) in gapNames)
        {
            output.WriteLine($"  {name}: {string.Join(", ", fields)}");
        }

        problems.RemoveAll(p => SharedModelExtras.Any(e => p.Contains(e.At, StringComparison.Ordinal) && (e.Property == "*" || p.Contains($"property '{e.Property}'", StringComparison.Ordinal))));
        foreach (var problem in problems.Distinct())
        {
            output.WriteLine("PROBLEM " + problem);
        }

        problems.Should().BeEmpty();
    }

    /// <summary>
    /// Models shared by operations whose documented responses differ. The model carries the union of the fields; the listed operation simply never returns
    /// the extra one. Each entry needs a reason; anything else the model declares but the specification lacks fails the test.
    /// </summary>
    private static readonly (string At, string Property, string Reason)[] SharedModelExtras =
    [
        ("POST /2/lists -> XList", "*", "Creating a List returns only id and name; XList is shared with the read endpoints, which return the rest."),
        ("GET /2/users/{id}/bookmarks/folders/{folder_id} -> XPost", "*", "The folder listing returns only Post IDs; XPost is the closest typed model and its other fields stay null."),
        ("DELETE /2/users/{id}/bookmarks/{tweet_id} -> XBookmarkResult", "tweet_id", "Only the create response echoes the Post ID."),
        ("DELETE /2/users/{id}/retweets/{source_tweet_id} -> XRepostResult", "rest_id", "Only the create response returns the new repost's ID."),
        ("POST /2/media/upload/initialize -> XMediaUploadResult", "*", "INIT returns id, media_key, expires_after_secs only; XMediaUploadResult is shared with the later steps."),
        ("XPost.note_post.entities.urls", "*", "Note Post URL entities carry fewer fields than Post URL entities; XUrlEntity is shared."),
        ("XUser.withheld", "copyright", "User withholding has no copyright flag; XWithheld is shared with Posts."),
        ("XDmEvent.entities.urls", "title", "DM URL entities carry fewer fields than Post URL entities; XUrlEntity is shared."),
        ("XDmEvent.entities.urls", "description", "As above."),
        ("XDmEvent.entities.urls", "media_key", "As above."),
        ("XDmEvent.entities.urls", "images", "As above."),
        ("XDmEvent.entities.urls", "status", "As above."),
        ("XDmEvent.entities.urls", "unwound_url", "As above."),
    ];

    private static void Compare(SpecContract contract, Type model, JsonNode schema, string at, HashSet<(Type, string)> path, List<string> problems, List<string> gaps)
    {
        var (props, required, open, hasShape) = contract.Shape(schema);
        if (!hasShape || !path.Add((model, at)))
        {
            return;
        }

        var modelled = new HashSet<string>();
        foreach (var property in model.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name);
            if (!props.TryGetValue(name, out var childSchema))
            {
                if (!open)
                {
                    problems.Add($"{at}: property '{name}' ({model.Name}.{property.Name}) is not in the specification");
                }

                continue;
            }

            modelled.Add(name);
            if (Element(property.PropertyType) is { } child && FindModel(child.Name) == child)
            {
                var childNode = contract.Resolve(childSchema).AsObject();
                if (childNode["items"] is { } items)
                {
                    childNode = contract.Resolve(items).AsObject();
                }

                Compare(contract, child, childNode, $"{at}.{name}", path, problems, gaps);
            }
        }

        foreach (var name in props.Keys.Where(k => !modelled.Contains(k)))
        {
            gaps.Add(name);
            if (required.Contains(name))
            {
                problems.Add($"{at}: the specification requires '{name}' but {model.Name} does not model it");
            }
        }
    }

    private static Type? Element(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
        {
            return Element(type.GetElementType()!);
        }

        if (type.IsGenericType && type.GetGenericArguments() is [var only] && typeof(IEnumerable).IsAssignableFrom(type))
        {
            return Element(only);
        }

        return type.IsEnum || type.IsPrimitive || type == typeof(string) || type == typeof(JsonElement) || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true ? null : type;
    }

    [Fact]
    public void EveryDocumentedScope_OfASupportedOperation_HasAnXScopesConstant()
    {
        var known = typeof(Authentication.XScopes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToHashSet();
        var supported = SupportedOperations().Select(o => o.Key).ToHashSet();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(SpecConformanceTests.RepositoryRoot, "docs/spec-snapshot.json")));

        var missing = doc.RootElement.GetProperty("operations").EnumerateArray()
            .Where(o => supported.Contains($"{o.GetProperty("method").GetString()} {o.GetProperty("path").GetString()}"))
            .SelectMany(o => o.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()!))
            .Where(s => !known.Contains(s))
            .Distinct()
            .Order()
            .ToList();

        missing.Should().BeEmpty("users must be able to request every scope a supported endpoint documents");
    }

    [Fact]
    public void EverySupportedPagedOperation_AcceptsATokenParameter_TheSdkKnowsHowToSend()
    {
        var contract = SpecContract.Instance;
        var problems = new List<string>();
        var paged = 0;

        foreach (var (key, _) in SupportedOperations())
        {
            var op = contract.Find(key)!;
            var response = op.Contract?["response"];
            if (response is null || !contract.Shape(response).Props.TryGetValue("meta", out var meta) || !contract.Shape(meta).Props.ContainsKey("next_token"))
            {
                continue;
            }

            paged++;
            var names = op.Contract!["parameters"]!.AsArray().Select(p => p!["name"]!.GetValue<string>()).ToHashSet();
            if (!names.Contains("pagination_token") && !names.Contains("next_token"))
            {
                problems.Add($"{key} returns meta.next_token but has neither pagination_token nor next_token to send it back");
            }
        }

        paged.Should().BeGreaterThan(15);
        problems.Should().BeEmpty();
    }
}
