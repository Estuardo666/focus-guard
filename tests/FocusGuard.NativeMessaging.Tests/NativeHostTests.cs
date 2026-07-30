using System.Buffers.Binary;
using System.Text;
using FocusGuard.NativeMessaging;

namespace FocusGuard.NativeMessaging.Tests;

public sealed class NativeHostTests
{
    [Fact]
    public async Task RunAsync_DispatchesValidatedJsonAndFramesResponse()
    {
        var request = Encoding.UTF8.GetBytes("""{"type":"status"}""");
        await using var input = new MemoryStream(Frame(request));
        await using var output = new MemoryStream();
        var handler = new RecordingHandler();

        await NativeHost.RunAsync(input, output, handler, CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
        output.Position = 0;
        var response = await NativeMessageFraming.ReadAsync(output, CancellationToken.None);
        Assert.Equal("""{"accepted":true}""", Encoding.UTF8.GetString(response!));
    }

    [Fact]
    public async Task RunAsync_DoesNotDispatchInvalidFrame()
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, NativeMessageFraming.MaxMessageBytes + 1u);
        await using var input = new MemoryStream(bytes);
        await using var output = new MemoryStream();
        var handler = new RecordingHandler();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => NativeHost.RunAsync(input, output, handler, CancellationToken.None));

        Assert.Equal(0, handler.CallCount);
    }

    private static byte[] Frame(byte[] payload)
    {
        var frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private sealed class RecordingHandler : INativeMessageHandler
    {
        public int CallCount { get; private set; }

        public ValueTask<byte[]> HandleAsync(
            ReadOnlyMemory<byte> request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(Encoding.UTF8.GetBytes("""{"accepted":true}"""));
        }
    }
}
