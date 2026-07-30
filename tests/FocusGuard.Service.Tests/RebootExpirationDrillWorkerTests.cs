using System.Text.Json;
using FocusGuard.Service;

namespace FocusGuard.Service.Tests;

public sealed class RebootExpirationDrillWorkerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"focusguard-fg104-{Guid.NewGuid():N}");

    [Fact]
    public async Task Worker_RetainsValidMarkerBeforeExpiration()
    {
        var now = DateTimeOffset.UtcNow;
        await WriteStateAsync(new RebootExpirationDrillState(
            1,
            Guid.NewGuid(),
            now,
            now.AddMinutes(1),
            "boot-before"));
        using var worker = new RebootExpirationDrillWorker(
            TimeProvider.System,
            _directory,
            () => "boot-before");

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);

        Assert.True(File.Exists(worker.StatePath));
        Assert.False(File.Exists(worker.ResultPath));
    }

    [Fact]
    public async Task Worker_ReleasesExpiredMarkerAndRecordsRestart()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new RebootExpirationDrillState(
            1,
            Guid.NewGuid(),
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            "boot-before");
        await WriteStateAsync(state);
        using var worker = new RebootExpirationDrillWorker(
            TimeProvider.System,
            _directory,
            () => "boot-after");

        await worker.StartAsync(CancellationToken.None);
        await WaitForFileAsync(worker.ResultPath);
        await worker.StopAsync(CancellationToken.None);

        var result = JsonSerializer.Deserialize<RebootExpirationDrillResult>(
            await File.ReadAllTextAsync(worker.ResultPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(result);
        Assert.Equal(RebootExpirationDrillWorker.ReleasedOutcome, result.Outcome);
        Assert.Equal("Expired", result.Reason);
        Assert.True(result.RestartObserved);
        Assert.True(result.ReleasedUtc >= state.ExpiresUtc);
    }

    [Fact]
    public async Task Worker_FailsOpenForMalformedState()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "state.json"), "{not-json");
        using var worker = new RebootExpirationDrillWorker(
            TimeProvider.System,
            _directory,
            () => "boot-current");

        await worker.StartAsync(CancellationToken.None);
        await WaitForFileAsync(worker.ResultPath);
        await worker.StopAsync(CancellationToken.None);

        var json = await File.ReadAllTextAsync(worker.ResultPath);
        Assert.Contains("\"outcome\":\"Released\"", json, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"InvalidStateFailOpen\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentBootId_IsStableWithinOneBoot()
    {
        var first = RebootExpirationDrillWorker.GetCurrentBootId();
        var second = RebootExpirationDrillWorker.GetCurrentBootId();

        Assert.Equal(first, second);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private async Task WriteStateAsync(RebootExpirationDrillState state)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "state.json"),
            JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!File.Exists(path))
        {
            await Task.Delay(10, cancellation.Token);
        }
    }
}
