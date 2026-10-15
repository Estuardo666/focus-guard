using System.Text;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsReconciliationTests
{
    private static readonly GlobalSettings Initial = new(50, "dark", "es");

    [Fact]
    public void OfflineChangesOnDifferentFieldsConvergeWithoutOverwriting()
    {
        var result = SettingsReconciliation.Merge(new(3, Initial), Initial with { Theme = "light" },
            new(4, Initial with { UiLanguage = "en", DefaultDurationMinutes = 25 }));
        Assert.Equal(new GlobalSettings(25, "light", "en"), result.Proposed);
        Assert.True(result.CanUpload); Assert.Equal(4, result.BaseRevision);
    }

    [Fact]
    public void ConcurrentDifferentDurationsRequireResolution()
    {
        var result = SettingsReconciliation.Merge(new(3, Initial), Initial with { DefaultDurationMinutes = 25 },
            new(4, Initial with { DefaultDurationMinutes = 90 }));
        Assert.Equal(SettingsFields.Duration, result.Conflicts);
        Assert.False(result.CanUpload); Assert.Equal(25, result.Proposed.DefaultDurationMinutes);
    }

    [Fact]
    public void IdenticalConcurrentEditIsNotAConflict()
    {
        var edited = Initial with { Theme = "light" };
        Assert.True(SettingsReconciliation.Merge(new(3, Initial), edited, new(4, edited)).CanUpload);
    }

    [Fact]
    public void RejectsRollbackAndSameRevisionDifferentContent()
    {
        Assert.Throws<InvalidOperationException>(() => SettingsReconciliation.Merge(new(3, Initial), Initial, new(2, Initial)));
        Assert.Throws<InvalidOperationException>(() => SettingsReconciliation.Merge(new(3, Initial), Initial,
            new(3, Initial with { UiLanguage = "en" })));
    }

    [Fact]
    public void RemoteResetDoesNotSilentlyDiscardPendingEdit()
    {
        var baseline = Initial with { DefaultDurationMinutes = 90 };
        var result = SettingsReconciliation.Merge(new(3, baseline), baseline with { DefaultDurationMinutes = 25 },
            new(4, GlobalSettings.Default, Deleted: true));
        Assert.False(result.CanUpload); Assert.Equal(SettingsFields.Duration, result.Conflicts);
    }

    [Fact]
    public void DeletionOfDefaultDocumentCannotBeResurrectedByAnOfflineThemeEdit()
    {
        var result = SettingsReconciliation.Merge(new(3, Initial), Initial with { Theme = "light" },
            new(4, GlobalSettings.Default, Deleted: true));
        Assert.False(result.CanUpload); Assert.Equal(SettingsFields.Theme, result.Conflicts);
    }

    [Fact]
    public void TombstoneResetsUneditedValuesButLocalOverrideRemains()
    {
        var previous = new GlobalSettings(90, "light", "en");
        var result = SettingsReconciliation.Merge(new(3, previous), previous, new(4, GlobalSettings.Default, true));
        Assert.Equal(GlobalSettings.Default, result.Proposed);
        Assert.Equal("light", new LocalSettingsOverrides(Theme: "light").Apply(result.Proposed).Theme);
        Assert.Equal("dark", result.Proposed.Theme);
    }

    [Fact]
    public void AckMustMatchOperationRevisionAndExactPayload()
    {
        var intent = new SettingsWriteIntent(Guid.NewGuid(), 3, Initial);
        Assert.True(intent.MatchesAcknowledgement(intent.OperationId, 4, Initial));
        Assert.False(intent.MatchesAcknowledgement(Guid.NewGuid(), 4, Initial));
        Assert.False(intent.MatchesAcknowledgement(intent.OperationId, 5, Initial));
        Assert.False(intent.MatchesAcknowledgement(intent.OperationId, 4, Initial with { Theme = "light" }));
    }

    [Fact]
    public void FinalSafeRevisionCanBeConfirmedButNeverIncrementedPastProtocolLimit()
    {
        const long maximum = 9_007_199_254_740_991;
        var state = new SettingsOutbox(new(maximum - 1, Initial)).Edit(Initial with { Theme = "light" }).Prepare(_ => [1]);
        var intent = state.InFlight!.Intent;
        var confirmed = state.Acknowledge(intent.OperationId, maximum, intent.Values);
        confirmed.Validate();
        Assert.False(confirmed.Pending); Assert.Equal(maximum, confirmed.Confirmed.Revision);
        Assert.Throws<ArgumentException>(() => confirmed.Edit(Initial).Prepare(_ => [2]));
        Assert.Throws<ArgumentException>(() => new SettingsSnapshot(maximum + 1, Initial).Validate());
    }

    [Fact]
    public void PayloadRoundTripsAndContainsOnlyAllowedSettings()
    {
        var encoded = GlobalSettingsCodec.Encode(Initial);
        Assert.Equal("{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"uiLanguage\":\"es\"}", Encoding.UTF8.GetString(encoded));
        Assert.Equal(Initial, GlobalSettingsCodec.Decode(encoded));
    }

    [Theory]
    [InlineData("{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"uiLanguage\":\"es\",\"permissions\":true}")]
    [InlineData("{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"uiLanguage\":\"es\",\"palette\":\"mist\"}")]
    [InlineData("{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"uiLanguage\":\"es\",\"orbStyle\":\"lava\"}")]
    [InlineData("{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"theme\":\"light\",\"uiLanguage\":\"es\"}")]
    [InlineData("{\"theme\":\"dark\",\"uiLanguage\":\"es\"}")]
    [InlineData("[]")]
    public void RejectsUnknownDuplicateMissingAndNonObjectPayloads(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => GlobalSettingsCodec.Decode(Encoding.UTF8.GetBytes(json)));

    [Theory]
    [InlineData(0, "dark", "es")]
    [InlineData(721, "dark", "es")]
    [InlineData(50, "unknown", "es")]
    [InlineData(50, "dark", "fr")]
    public void RejectsInvalidSettings(int duration, string theme, string language) =>
        Assert.Throws<ArgumentException>(() => new GlobalSettings(duration, theme, language).Validate());
}
