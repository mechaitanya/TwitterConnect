using System.Runtime.CompilerServices;
using XConnect.Models;

namespace XConnect;

/// <summary>
/// Walks paged X API endpoints. Every list method takes a pagination token and returns the next one in
/// <see cref="XResponse{T}.NextToken"/>, so one traversal helper serves all of them.
/// </summary>
/// <example>
/// <code>
/// await foreach (var post in XPaginator.ItemsAsync(
///     (token, ct) =&gt; client.Posts.GetUserPostsAsync(userId, new XPostQuery { PaginationToken = token }, ct)))
/// {
///     Console.WriteLine(post.Text);
/// }
/// </code>
/// </example>
public static class XPaginator
{
    /// <summary>Yields each page's response. Stops after the last page, an error page, <see cref="XPaginationOptions.MaxPages"/>, or a repeated token.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="fetchPage">Fetches the page for a token; the token is <see langword="null"/> for the first page.</param>
    /// <param name="options">Traversal bounds. Defaults allow 10 pages.</param>
    /// <param name="cancellationToken">A token to cancel the traversal.</param>
    /// <remarks>
    /// An error page is yielded as the last element so the caller can inspect <see cref="XResponse{T}.Error"/>.
    /// A token that X returns twice would loop forever, so traversal stops when one repeats.
    /// </remarks>
    public static async IAsyncEnumerable<XResponse<IReadOnlyList<T>>> PagesAsync<T>(
        Func<string?, CancellationToken, Task<XResponse<IReadOnlyList<T>>>> fetchPage,
        XPaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetchPage);
        var settings = options ?? new XPaginationOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(settings.MaxPages, 1);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        var pages = 0;
        var items = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await fetchPage(token, cancellationToken).ConfigureAwait(false);
            pages++;
            items += page.Data?.Count ?? 0;
            yield return page;

            var next = page.NextToken;
            if (!page.IsSuccess
                || string.IsNullOrEmpty(next)
                || pages >= settings.MaxPages
                || (settings.MaxItems is { } max && items >= max)
                || !seen.Add(next))
            {
                yield break;
            }

            token = next;
        }
    }

    /// <summary>Yields items across pages.</summary>
    /// <exception cref="XApiException">A page failed.</exception>
    public static async IAsyncEnumerable<T> ItemsAsync<T>(
        Func<string?, CancellationToken, Task<XResponse<IReadOnlyList<T>>>> fetchPage,
        XPaginationOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var yielded = 0;
        await foreach (var page in PagesAsync(fetchPage, options, cancellationToken).ConfigureAwait(false))
        {
            if (page.Error is not null)
            {
                throw new XApiException(page.Error);
            }

            foreach (var item in page.Data ?? [])
            {
                if (options?.MaxItems is { } max && yielded >= max)
                {
                    yield break;
                }

                yielded++;
                yield return item;
            }
        }
    }
}
