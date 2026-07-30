using System.Security.Cryptography;

namespace FocusGuard.Service.Tests;

public sealed class ExecutableSha256CacheTests
{
    [Fact]
    public void UnchangedExecutableIsHashedOnlyOnceAcrossPolls()
    {
        using var file = TemporaryExecutable.Create("first");
        var calls = 0;
        var cache = new ExecutableSha256Cache(path =>
        {
            calls++;
            return Hash(path);
        });

        var first = cache.GetHash(file.Path);
        var second = cache.GetHash(file.Path);

        Assert.Equal(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ChangedExecutableInvalidatesCachedHash()
    {
        using var file = TemporaryExecutable.Create("first");
        var calls = 0;
        var cache = new ExecutableSha256Cache(path =>
        {
            calls++;
            return Hash(path);
        });
        var first = cache.GetHash(file.Path);

        File.WriteAllText(file.Path, "second-version");
        File.SetLastWriteTimeUtc(file.Path, DateTime.UtcNow.AddSeconds(2));
        var second = cache.GetHash(file.Path);

        Assert.NotEqual(first, second);
        Assert.Equal(2, calls);
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class TemporaryExecutable : IDisposable
    {
        private TemporaryExecutable(string path) => Path = path;
        public string Path { get; }

        public static TemporaryExecutable Create(string content)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"FocusGuard.Hash.{Guid.NewGuid():N}.exe");
            File.WriteAllText(path, content);
            return new TemporaryExecutable(path);
        }

        public void Dispose() => File.Delete(Path);
    }
}
