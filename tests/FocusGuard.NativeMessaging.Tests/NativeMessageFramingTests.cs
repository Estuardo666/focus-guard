using System.Buffers.Binary;
using System.Text;
using FocusGuard.NativeMessaging;

namespace FocusGuard.NativeMessaging.Tests;

public sealed class NativeMessageFramingTests
{
    [Fact]
    public async Task ReadAsync_ReadsFragmentedFrame()
    {
        var payload = Encoding.UTF8.GetBytes("""{"type":"status"}""");
        await using var stream = new FragmentedReadStream(new MemoryStream(Frame(payload)), 2);

        var result = await NativeMessageFraming.ReadAsync(stream, CancellationToken.None);

        Assert.Equal(payload, result);
    }

    [Fact]
    public async Task ReadAsync_ReturnsNullForCleanEndOfStream()
    {
        await using var stream = new MemoryStream();

        var result = await NativeMessageFraming.ReadAsync(stream, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(NativeMessageFraming.MaxMessageBytes + 1)]
    public async Task ReadAsync_RejectsInvalidLength(int length)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)length);
        await using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await NativeMessageFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadAsync_RejectsTruncatedPayload()
    {
        var frame = Frame(Encoding.UTF8.GetBytes("""{"type":"status"}"""));
        await using var stream = new MemoryStream(frame[..^1]);

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await NativeMessageFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"a\":/*comment*/1}")]
    public async Task ReadAsync_RejectsInvalidJson(string json)
    {
        await using var stream = new MemoryStream(Frame(Encoding.UTF8.GetBytes(json)));

        await Assert.ThrowsAsync<InvalidDataException>(
            async () => await NativeMessageFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task WriteAsync_WritesLittleEndianFrame()
    {
        var payload = Encoding.UTF8.GetBytes("""{"ok":true}""");
        await using var stream = new MemoryStream();

        await NativeMessageFraming.WriteAsync(stream, payload, CancellationToken.None);

        Assert.Equal(Frame(payload), stream.ToArray());
    }

    private static byte[] Frame(byte[] payload)
    {
        var frame = new byte[payload.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private sealed class FragmentedReadStream(Stream inner, int fragmentSize) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, fragmentSize));
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer[..Math.Min(buffer.Length, fragmentSize)], cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
