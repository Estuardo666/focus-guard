using FocusGuard.Client;
using FocusGuard.Client.Preferences;
using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Desktop;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Desktop.Tests;

public sealed class ProfileConflictTests
{
    [Theory]
    [InlineData("rename")]
    [InlineData("color")]
    [InlineData("delete")]
    [InlineData("copy")]
    [InlineData("removeApp")]
    [InlineData("removeSite")]
    public async Task ActualClassicViewModelReloadsConflictsAndPreservesNewerProfile(string operation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fg191-vm-" + Guid.NewGuid());
        try
        {
            var store = new SqliteProfileStore(Path.Combine(directory, "profiles.db"));
            var profile = new StoredProfile(ProfileId.New(), "Work",
                [new(Guid.NewGuid(), RuleAction.Block, ApplicationMatchKind.FileName, "work.exe")],
                [new(Guid.NewGuid(), RuleAction.Allow, "work.example")]);
            await store.SaveProfileAsync(profile); await store.SaveProfileAsync(new(ProfileId.New(), "Other", [], []));
            var model = new FocusViewModel(new Client(), store, settingsStore: new DesktopSettingsStore(persist: false));
            await model.RefreshAsync(); model.SelectedProfile = model.Profiles.Single(p => p.Id == profile.Id);
            model.ProfileNameInput = "Classic attempt";
            if (operation == "delete") await model.RequestProfileDeleteAsync();
            var newer = (await new ProfileEditorWorkflow(new SqliteProfileStore(Path.Combine(directory, "profiles.db"))).RenameAsync(
                profile.Id, ProfileRevision.Compute(profile), "React saved")).Profile!;
            switch (operation)
            {
                case "rename": await model.RenameSelectedProfileAsync(); break;
                case "color": await model.ApplyProfileColorAsync("#AABBCC"); break;
                case "delete": await model.ConfirmProfileDeleteAsync(); break;
                case "copy": await model.DuplicateSelectedProfileAsync(); break;
                case "removeApp": await model.RemoveApplicationRuleAsync(profile.ApplicationRules[0].Id); break;
                case "removeSite": await model.RemoveWebsiteRuleAsync(profile.WebsiteRules[0].Id); break;
            }
            Assert.Equal(UiStrings.Get("ProfileChangedReload"), model.StatusMessage);
            Assert.Equal("React saved", model.SelectedProfile!.Name); Assert.True(model.CanEditProfileRules);
            Assert.False(model.IsDeleteProfileConfirmationVisible);
            Assert.Equal(ProfileRevision.Compute(newer), ProfileRevision.Compute((await store.LoadProfileAsync(profile.Id))!));
            Assert.Equal(2, (await store.ListProfilesAsync()).Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Client : IFocusControlClient
    {
        public ValueTask<StatusResponse> GetStatusAsync(CancellationToken token) => ValueTask.FromResult(new StatusResponse(true, true, "Inactive", 0, null, null, null, null, false, false));
        public ValueTask<ActivityResponse> GetActivityAsync(int count, CancellationToken token) => ValueTask.FromResult(new ActivityResponse([]));
        public ValueTask<PrepareSessionResponse> PrepareAsync(PrepareSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(ActivatePreparedSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ActivateSessionResponse> ActivateAsync(ActivateSessionRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SkipBreakResponse> SkipBreakAsync(SkipBreakRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<StopSessionResponse> StopAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<CaptureResponse> CaptureAsync(CaptureRequest request, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(Guid? id, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(ResolveCapturesRequest request, CancellationToken token) => throw new NotSupportedException();
    }
}
