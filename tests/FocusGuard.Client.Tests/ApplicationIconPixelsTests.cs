using FocusGuard.Client.Platform;
using Xunit;

namespace FocusGuard.Client.Tests;

public sealed class ApplicationIconPixelsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(257, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 257)]
    public void RejectsUnboundedDimensions(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationIconPixels(width, height, []));

    [Fact]
    public void RejectsTruncatedBuffer() =>
        Assert.Throws<ArgumentException>(() => new ApplicationIconPixels(2, 2, new byte[15]));

    [Fact]
    public void CopiesNativeBufferBeforeItCanBeReused()
    {
        var buffer = new byte[] { 1, 2, 3, 255 };
        var icon = new ApplicationIconPixels(1, 1, buffer);
        buffer[0] = 99;
        Assert.Equal(1, icon.Pixels.Span[0]);
    }
}
