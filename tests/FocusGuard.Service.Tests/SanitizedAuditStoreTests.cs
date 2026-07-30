using FocusGuard.Contracts;

namespace FocusGuard.Service.Tests;

public sealed class SanitizedAuditStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"FocusGuard.Audit.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task PersistsAcrossInstancesAndRemovesUrlSecrets()
    {
        var path = Path.Combine(_directory, "events.jsonl");
        using (var first = new JsonLinesFocusAuditStore(path))
        {
            await first.WriteAsync(
                new FocusAuditEvent(
                    Guid.NewGuid(),
                    new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero),
                    FocusAuditEventKind.WebsiteBlocked,
                    "https://user:password@EXAMPLE.com/private/path?token=secret#fragment",
                    "Blocked"),
                CancellationToken.None);
        }

        using var reopened = new JsonLinesFocusAuditStore(path);
        var persisted = Assert.Single(await reopened.ReadRecentAsync(20, CancellationToken.None));
        var raw = await File.ReadAllTextAsync(path);

        Assert.Equal("https://example.com/private/path", persisted.Target);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fragment", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplicationEventStoresOnlyFileNameAndSkipsTornRecord()
    {
        var path = Path.Combine(_directory, "events.jsonl");
        using var store = new JsonLinesFocusAuditStore(path);
        await store.WriteAsync(
            new FocusAuditEvent(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                FocusAuditEventKind.ApplicationBlocked,
                @"C:\Users\Person\Secret\chat.exe",
                "Terminated"),
            CancellationToken.None);
        await File.AppendAllTextAsync(path, "{torn-record`r`n");

        var persisted = Assert.Single(await store.ReadRecentAsync(20, CancellationToken.None));

        Assert.Equal("chat.exe", persisted.Target);
        Assert.DoesNotContain("Person", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
