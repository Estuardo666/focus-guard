using System.Text;
using FocusGuard.Sync;
using Xunit;

public sealed class SettingsExportTests
{
    [Fact]
    public void ExportRequiresConfirmationAndContainsOnlyPortableSettings()
    {
        Assert.Throws<InvalidOperationException>(() => SettingsExport.Create(GlobalSettings.Default, false));
        var json = Encoding.UTF8.GetString(SettingsExport.Create(GlobalSettings.Default, true));
        Assert.Equal("{\"format\":\"focusguard.settings.export\",\"version\":1,\"settings\":{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"uiLanguage\":\"es\"}}", json);
    }

    [Fact]
    public void ImportPreviewPreservesUnselectedFieldsAndDoesNotChangeOutbox()
    {
        var exported = SettingsExport.Create(new(25, "light", "en"), true);
        var state = new SettingsOutbox(new(3, GlobalSettings.Default));
        var preview = SettingsExport.Preview(exported, state.Confirmed.Values, SettingsFields.Theme);
        Assert.Equal(new GlobalSettings(50, "light", "es"), preview.Proposed);
        Assert.False(state.Pending); Assert.Null(state.InFlight);
        Assert.Throws<InvalidOperationException>(() => preview.Confirm(GlobalSettings.Default, false));
        Assert.Equal(preview.Proposed, preview.Confirm(GlobalSettings.Default, true));
    }

    [Fact]
    public void StalePreviewCannotOverwriteANewerLocalEdit()
    {
        var preview = SettingsExport.Preview(SettingsExport.Create(new(25, "light", "en"), true), GlobalSettings.Default, SettingsFields.Language);
        Assert.Throws<InvalidOperationException>(() => preview.Confirm(GlobalSettings.Default with { Theme = "light" }, true));
    }

    [Theory]
    [InlineData("{\"format\":\"focusguard.settings.export\",\"version\":1,\"settings\":{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"uiLanguage\":\"es\",\"enforcement\":true}}")]
    [InlineData("{\"format\":\"focusguard.settings.export\",\"version\":1,\"settings\":{\"defaultDurationMinutes\":50,\"theme\":\"dark\",\"theme\":\"light\",\"uiLanguage\":\"es\"}}")]
    [InlineData("{\"format\":\"focusguard.settings.export\",\"version\":1,\"version\":1,\"settings\":{}}")]
    public void MaliciousOrDuplicateFieldsAreRejected(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => SettingsExport.Preview(Encoding.UTF8.GetBytes(json), GlobalSettings.Default, SettingsFields.Theme));

    [Fact]
    public void ForgedPreviewCannotApplyUnselectedChanges()
    {
        var baseline = GlobalSettings.Default;
        var forged = new SettingsImportPreview(baseline, baseline with { Theme = "light", UiLanguage = "en" }, SettingsFields.Theme);
        Assert.Throws<InvalidOperationException>(() => forged.Confirm(baseline, true));
        var unknownSelection = new SettingsImportPreview(baseline, baseline, (SettingsFields)128);
        Assert.Throws<InvalidOperationException>(() => unknownSelection.Confirm(baseline, true));
    }

    [Fact]
    public void ExportCannotBeUsedToImportATombstoneOrAnAccountIdentifier()
    {
        var deletion = "{\"format\":\"focusguard.settings.export\",\"version\":1,\"settings\":{\"deleted\":true}}";
        Assert.Throws<System.Text.Json.JsonException>(() => SettingsExport.Preview(Encoding.UTF8.GetBytes(deletion), GlobalSettings.Default, SettingsFields.Theme));
        var original = Encoding.UTF8.GetString(SettingsExport.Create(GlobalSettings.Default, true));
        var injected = "{\"accountId\":\"11111111-1111-4111-8111-111111111111\"," + original[1..];
        Assert.Throws<System.Text.Json.JsonException>(() => SettingsExport.Preview(Encoding.UTF8.GetBytes(injected), GlobalSettings.Default, SettingsFields.Theme));
    }

    [Fact]
    public void InvalidPreviewObjectsFailClosedBeforeApplyingAnything()
    {
        var baseline = GlobalSettings.Default;
        Assert.Throws<ArgumentNullException>(() => new SettingsImportPreview(baseline, null!, SettingsFields.Theme).Confirm(baseline, true));
        Assert.Throws<ArgumentNullException>(() => new SettingsImportPreview(null!, baseline, SettingsFields.Theme).Confirm(baseline, true));
        Assert.Throws<ArgumentException>(() => new SettingsImportPreview(baseline,
            baseline with { DefaultDurationMinutes = 999 }, SettingsFields.Duration).Confirm(baseline, true));
    }

    [Fact]
    public void UnknownVersionAndEmptySelectionAreRejected()
    {
        var json = Encoding.UTF8.GetString(SettingsExport.Create(GlobalSettings.Default, true)).Replace("\"version\":1", "\"version\":2");
        Assert.Throws<InvalidDataException>(() => SettingsExport.Preview(Encoding.UTF8.GetBytes(json), GlobalSettings.Default, SettingsFields.Theme));
        Assert.Throws<ArgumentException>(() => SettingsExport.Preview(SettingsExport.Create(GlobalSettings.Default, true), GlobalSettings.Default, SettingsFields.None));
    }
}
