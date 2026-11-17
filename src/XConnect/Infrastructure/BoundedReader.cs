using System.Buffers;
using System.Text;

namespace XConnect.Infrastructure;

/// <summary>Reads HTTP bodies with an explicit size limit enforced while reading, so an oversized body is never held in memory.</summary>
internal static class BoundedBody
{
    private const int ChunkSize = 16 * 1024;

    /// <summary>
    /// Reads the body as text. A body longer than <paramref name="maxBytes"/> throws <see cref="XResponseTooLargeException"/>; it is never truncated
    /// and then returned. A declared <c>Content-Length</c> over the limit is refused before any byte is read.
    /// </summary>
    public static async Task<string> ReadStringAsync(HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is { } declared && declared > maxBytes)
        {
            throw new XResponseTooLargeException(maxBytes, "The response declares " + declared.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes");
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var writer = new ArrayBufferWriter<byte>(declared: content.Headers.ContentLength);
            long total = 0;
            while (true)
            {
                // Never ask for more than one byte past the limit, so the check below fires before memory grows further.
                var room = (int)Math.Min(ChunkSize, maxBytes - total + 1);
                var memory = writer.GetMemory(room)[..room];
                var read = await stream.ReadAsync(memory, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    throw new XResponseTooLargeException(maxBytes, "The response body is larger than the limit");
                }

                writer.Advance(read);
            }

            return Decode(writer.WrittenSpan, content.Headers.ContentType?.CharSet);
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes, string? charset)
    {
        var encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // An unknown charset falls back to UTF-8, as HttpContent.ReadAsStringAsync does.
            }
        }

        if (ReferenceEquals(encoding, Encoding.UTF8) || encoding.CodePage == 65001)
        {
            ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
            if (bytes.StartsWith(bom))
            {
                bytes = bytes[3..];
            }
        }

        return encoding.GetString(bytes);
    }

    private sealed class ArrayBufferWriter<T>
    {
        private readonly System.Buffers.ArrayBufferWriter<T> _inner;

        public ArrayBufferWriter(long? declared) =>
            _inner = new System.Buffers.ArrayBufferWriter<T>((int)Math.Clamp(declared ?? 1024, 256, ChunkSize * 4));

        public Memory<T> GetMemory(int hint) => _inner.GetMemory(hint);

        public void Advance(int count) => _inner.Advance(count);

        public ReadOnlySpan<T> WrittenSpan => _inner.WrittenSpan;
    }
}

internal enum LineKind
{
    /// <summary>A complete line within the limit.</summary>
    Line,

    /// <summary>The line exceeded the limit. Its bytes were discarded up to the next newline, without buffering them.</summary>
    Oversized,

    /// <summary>The stream ended.</summary>
    End,
}

/// <summary>
/// Reads newline-delimited UTF-8 lines from a stream with a hard limit on the size of one line. An oversized line is skipped (its remainder is
/// discarded as it arrives) and reading continues with the next line, so one bad line neither exhausts memory nor corrupts the lines after it.
/// </summary>
internal sealed class BoundedLineReader(Stream stream, int maxLineBytes)
{
    private readonly byte[] _buffer = new byte[8192];
    private int _start;
    private int _end;

    public async ValueTask<(LineKind Kind, string? Text)> ReadAsync(CancellationToken cancellationToken)
    {
        var line = new System.Buffers.ArrayBufferWriter<byte>(256);
        var oversized = false;
        while (true)
        {
            if (_start == _end)
            {
                _end = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                _start = 0;
                if (_end == 0)
                {
                    if (oversized)
                    {
                        return (LineKind.Oversized, null);
                    }

                    return line.WrittenCount > 0 ? (LineKind.Line, Decode(line.WrittenSpan)) : (LineKind.End, null);
                }
            }

            var chunk = _buffer.AsSpan(_start, _end - _start);
            var newline = chunk.IndexOf((byte)'\n');
            var take = newline < 0 ? chunk.Length : newline;
            if (!oversized)
            {
                if (line.WrittenCount + take > maxLineBytes)
                {
                    oversized = true;
                    line.ResetWrittenCount(); // free what was buffered; the rest of this line is discarded unread.
                }
                else
                {
                    line.Write(chunk[..take]);
                }
            }

            _start += take + (newline < 0 ? 0 : 1);
            if (newline >= 0)
            {
                return oversized ? (LineKind.Oversized, null) : (LineKind.Line, Decode(line.WrittenSpan));
            }
        }
    }

    private static string Decode(ReadOnlySpan<byte> bytes) =>
        Encoding.UTF8.GetString(bytes.Length > 0 && bytes[^1] == (byte)'\r' ? bytes[..^1] : bytes);
}
