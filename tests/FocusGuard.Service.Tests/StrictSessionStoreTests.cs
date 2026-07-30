using FocusGuard.Contracts;
using FocusGuard.Domain;

namespace FocusGuard.Service.Tests;

public sealed class StrictSessionStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "FocusGuard.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ActivationPersistsAuthenticatedImmutablePolicyWithoutPlainCode()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var generated = RecoveryCodeService.Generate();
        var start = DateTimeOffset.UtcNow;
        var session = Session.Create(
                ProfileId.New(),
                start,
                TimeSpan.FromMinutes(5),
                new StrictModeOptions(true, TimeSpan.FromMinutes(15))).Value!
            .TransitionTo(SessionState.Activating)
            .TransitionTo(SessionState.Active);
        var store = new StrictSessionStore(_directory, () => "test-boot");
        var applicationRule = new ApplicationRule(
            Guid.NewGuid(),
            RuleAction.Block,
            ApplicationMatchKind.FileName,
            "chat.exe");
        var policy = EnforcementPolicy.Create(session, [applicationRule], []);

        await store.ActivateAsync(session, policy, generated.Credential, CancellationToken.None);
        var loaded = await store.LoadAsync(start, CancellationToken.None);
        var persistedText = string.Join(
            Environment.NewLine,
            Directory.GetFiles(_directory).Select(File.ReadAllText));

        Assert.Equal(StrictPolicyLoadStatus.Active, loaded.Status);
        Assert.Equal(applicationRule, Assert.Single(loaded.Snapshot!.Policy.ApplicationRules));
        Assert.Empty(loaded.Snapshot.Policy.WebsiteRules);
        Assert.DoesNotContain(generated.DisplayCode, persistedText, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(
            _directory,
            StrictPolicyPersistenceWorker.IntegrityKeyFileName)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
