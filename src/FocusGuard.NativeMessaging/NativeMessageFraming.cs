using System.Buffers.Binary;
using System.Text.Json;

namespace FocusGuard.NativeMessaging;

public static class NativeMessageFraming
{
    public const int HeaderSize = sizeof(uint);
    public const int MaxMessageBytes = 64 * 1024;
    public const int MaxJsonDepth = 16;

    public static async ValueTask<byte[]?> ReadAsync(Stream input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var header = new byte[HeaderSize];
        var headerBytes = await ReadUpToAsync(input, header, cancellationToken).ConfigureAwait(false);
        if (headerBytes == 0)
        {
            return null;
        }

        if (headerBytes != HeaderSize)
        {
            throw new InvalidDataException("Native message length prefix is truncated.");
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length is 0 or > MaxMessageBytes)
        {
            throw new InvalidDataException("Native message length is outside the supported range.");
        }

        var payload = new byte[(int)length];
        var payloadBytes = await ReadUpToAsync(input, payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes != payload.Length)
        {
            throw new InvalidDataException("Native message payload is truncated.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                payload,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = MaxJsonDepth
                });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Native message root must be a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Native message must be valid bounded JSON.", exception);
        }

        return payload;
    }

    public static async ValueTask WriteAsync(
        Stream output,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (payload.Length is 0 or > MaxMessageBytes)
        {
            throw new InvalidDataException("Native message length is outside the supported range.");
        }

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<int> ReadUpToAsync(
        Stream input,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await input.ReadAsync(buffer[total..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
