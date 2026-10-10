using FocusGuard.Client.Journal;
using FocusGuard.Client.Workflows;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeStrictReview(string PreparationId, string ProfileId, string ProfileName, string Mode,
    bool Strict, int DurationSeconds, bool EnforcementEnabled, int ApplicationRuleCount, int WebsiteRuleCount,
    int BlockedApplicationCount, int BlockedWebsiteCount, IReadOnlyList<string> BlockedApplications,
    IReadOnlyList<string> BlockedWebsites, int BreakApplicationRuleCount, int BreakWebsiteRuleCount,
    IReadOnlyList<string> BreakApplications, IReadOnlyList<string> BreakWebsites, bool RulesTruncated,
    string? PresetName, string? PresetId, BridgeCycleConfiguration? Configuration, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    DateTimeOffset ExpiresUtc, IReadOnlyList<BridgeCyclePhase> Phases, bool PartialFinalPhase,
    bool ExtensionReady, int RecoveryDelaySeconds, string RecoveryCode, string ConfirmationText,
    string RecoveryHelperPath, bool RecoveryHelperAvailable);
internal sealed record BridgeStrictStarted(string SessionId, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
internal sealed record BridgeStrictCancelled(bool Cancelled);
internal sealed record BridgeStrictCodeSaved(bool Saved);
internal sealed record BridgeRecoveryInfo(string ExecutablePath, bool Available, int RecoveryDelaySeconds);

internal sealed record StrictReviewOwnership(string DocumentToken, BridgeStrictReview Review,
    RecoveryCredential Credential, Guid ProfileId, Guid? PresetId, FocusCycleConfiguration? Configuration);

internal sealed partial class NativeBridgeDispatcher
{
    private const int StrictRecoveryDelaySeconds = 15 * 60;
    private const int MaximumVisibleStrictRules = 12;
    private const int MaximumStrictRuleLabelLength = 512;
    private StrictReviewOwnership? _strictReview;

    private async ValueTask<(string? Error, object? Result)> HandleStrictSessionCommandAsync(
        BridgeRequest request, CancellationToken token)
    {
        if (request.RecoveryInfo)
        {
            var path = RecoveryHelperPath();
            return (null, new BridgeRecoveryInfo(path, File.Exists(path), StrictRecoveryDelaySeconds));
        }

        if (request.StrictSaveRecoveryCodeId is { } saveId)
            return await SaveStrictRecoveryCodeAsync(request, saveId, token).ConfigureAwait(false);

        if (request.StrictCancelPreparationId is { } cancelId)
        {
            var cancelled = _strictReview is { } current
                && current.DocumentToken == request.DocumentToken
                && current.Review.PreparationId == cancelId.ToString("N");
            if (cancelled) _strictReview = null;
            return (null, new BridgeStrictCancelled(cancelled));
        }

        if (request.StrictSession is { } input)
            return await PrepareStrictSessionAsync(request, input, token).ConfigureAwait(false);

        return await ConfirmStrictSessionAsync(request, request.StrictActivation!, token).ConfigureAwait(false);
    }

    private async ValueTask<(string? Error, object? Result)> SaveStrictRecoveryCodeAsync(
        BridgeRequest request, Guid preparationId, CancellationToken token)
    {
        if (_strictReview is not { } review || review.DocumentToken != request.DocumentToken
            || review.Review.PreparationId != preparationId.ToString("N")) return ("StrictReviewUnavailable", null);
        if (_timeProvider.GetUtcNow().ToUniversalTime() >= review.Review.ExpiresUtc)
        {
            _strictReview = null;
            return ("StrictReviewExpired", null);
        }
        if (recoveryCodeSaver is null) return ("RecoverySaveUnavailable", null);
        var saved = await recoveryCodeSaver(review.Review.RecoveryCode, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
        return (null, new BridgeStrictCodeSaved(saved));
    }

    private async ValueTask<(string? Error, object? Result)> PrepareStrictSessionAsync(
        BridgeRequest request, StrictSessionParameters input, CancellationToken token)
    {
        if (_workflow is null || profileStore is null) return ("AuthorizationUnavailable", null);
        _strictReview = null;
        _normalReview = null;
        _cycleReview = null;

        var status = await client.GetStatusAsync(token).ConfigureAwait(false);
        if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
        if (!CanStart(status)) return ("SessionAlreadyActive", null);

        var profile = await profileStore.LoadProfileAsync(new ProfileId(input.ProfileId), token).ConfigureAwait(false);
        if (profile is null) return ("ProfileMissing", null);
        if (profile.Id.Value != input.ProfileId || !Profile.ValidateName(profile.Name).IsValid)
            return ("InvalidProfile", null);

        var blockedApplications = profile.ApplicationRules.Where(rule => rule.Action == RuleAction.Block)
            .Select(rule => rule.Value).ToArray();
        var blockedWebsites = profile.WebsiteRules.Where(rule => rule.Action == RuleAction.Block)
            .Select(rule => rule.Pattern).ToArray();
        if (input.EnforcementEnabled && blockedApplications.Length + blockedWebsites.Length == 0)
            return ("NoBlockingRules", null);

        NormalTimerReview? timerReview = null;
        PrepareSessionResponse prepared;
        ProfileCycleSettings? breakSettings = null;
        FocusCycleConfiguration? cycleConfiguration = null;
        string? presetName = null;
        if (input.Mode == SessionMode.SimpleTimer)
        {
            timerReview = await _workflow.PrepareStrictTimerAsync(
                new(profile, input.DurationSeconds, StrictRecoveryDelaySeconds, input.EnforcementEnabled), token)
                .ConfigureAwait(false);
            prepared = timerReview.Preparation;
        }
        else
        {
            if (input.PresetId is { } presetId)
            {
                var preset = await profileStore.LoadCyclePresetAsync(presetId, token).ConfigureAwait(false);
                if (preset is null) return ("PresetMissing", null);
                if (preset.Id != presetId) return ("InvalidPreset", null);
                cycleConfiguration = preset.Configuration;
                presetName = preset.Name;
            }
            else cycleConfiguration = input.Configuration;

            if (cycleConfiguration is null) return ("CycleConfigurationUnavailable", null);
            breakSettings = await profileStore.LoadProfileCycleSettingsAsync(profile.Id, token).ConfigureAwait(false);
            if (breakSettings is not null && breakSettings.ProfileId != profile.Id)
                return ("InvalidProfile", null);
            if (breakSettings is not null && (breakSettings.ApplicationRules.Any(rule => rule.Action != RuleAction.Block)
                || breakSettings.WebsiteRules.Any(rule => rule.Action != RuleAction.Block)))
                return ("BreakRulesInvalid", null);

            prepared = await _workflow.PrepareCycleAsync(new(profile, cycleConfiguration, breakSettings,
                StrictMode: true, RecoveryDelaySeconds: StrictRecoveryDelaySeconds,
                EnforcementEnabled: input.EnforcementEnabled), token).ConfigureAwait(false);
            SessionWorkflow.ValidateCycleReview(prepared, cycleConfiguration);
        }

        if (!prepared.Accepted || prepared.PreparationId is not { } preparationId
            || prepared.StartUtc is not { } startUtc || prepared.EndUtc is not { } endUtc
            || prepared.ExpiresUtc is not { } expiresUtc)
            return (prepared.Reason == "ProfileMissing" ? "ProfileMissing" : "PreparationRejected", null);
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);

        var recovery = RecoveryCodeService.Generate();
        IReadOnlyList<string> blockedApplicationLabels = [];
        IReadOnlyList<string> blockedWebsiteLabels = [];
        var appTruncated = false;
        var siteTruncated = false;
        if (input.EnforcementEnabled)
        {
            blockedApplicationLabels = VisibleLabels(blockedApplications, out appTruncated);
            blockedWebsiteLabels = VisibleLabels(blockedWebsites, out siteTruncated);
        }
        var breakApplications = VisibleLabels(breakSettings?.ApplicationRules.Select(rule => rule.Value) ?? [], out var breakAppTruncated);
        var breakWebsites = VisibleLabels(breakSettings?.WebsiteRules.Select(rule => rule.Pattern) ?? [], out var breakSiteTruncated);
        var listsTruncated = (input.EnforcementEnabled && (appTruncated || siteTruncated)) || breakAppTruncated || breakSiteTruncated;
        var displayedExpiryUtc = expiresUtc < endUtc ? expiresUtc : endUtc;
        if (displayedExpiryUtc <= startUtc) return ("InvalidServiceResponse", null);
        var phases = prepared.Phases.Select(phase => new BridgeCyclePhase(phase.Index, phase.Kind.ToString(),
            phase.FocusCycleNumber, phase.StartUtc, phase.EndUtc)).ToArray();
        var partial = false;
        var bridgeConfiguration = cycleConfiguration is null ? null : CycleConfiguration(cycleConfiguration);
        if (cycleConfiguration is not null)
        {
            var plan = FocusCyclePlanBuilder.Build(cycleConfiguration, startUtc);
            if (!plan.IsValid) return ("InvalidServiceResponse", null);
            partial = plan.Value!.HasPartialFinalPhase;
        }

        var durationSeconds = checked((int)(endUtc - startUtc).TotalSeconds);
        var recoveryPath = RecoveryHelperPath();
        var review = new BridgeStrictReview(preparationId.ToString("N"), profile.Id.Value.ToString("D"), profile.Name,
            input.Mode.ToString(), true, durationSeconds, input.EnforcementEnabled, profile.ApplicationRules.Count,
            profile.WebsiteRules.Count, blockedApplications.Length, blockedWebsites.Length, blockedApplicationLabels,
            blockedWebsiteLabels, breakSettings?.ApplicationRules.Count ?? 0, breakSettings?.WebsiteRules.Count ?? 0,
            breakApplications, breakWebsites, listsTruncated,
            presetName, input.PresetId?.ToString("D"), bridgeConfiguration, startUtc, endUtc, displayedExpiryUtc, phases, partial, status.ExtensionReady,
            StrictRecoveryDelaySeconds, recovery.DisplayCode, profile.Name, recoveryPath, File.Exists(recoveryPath));
        _strictReview = new(request.DocumentToken, review, recovery.Credential, profile.Id.Value,
            input.PresetId, cycleConfiguration);
        return (null, review);
    }

    private async ValueTask<(string? Error, object? Result)> ConfirmStrictSessionAsync(
        BridgeRequest request, StrictSessionActivationParameters input, CancellationToken token)
    {
        if (_strictReview is not { } owned || owned.DocumentToken != request.DocumentToken
            || owned.Review.PreparationId != input.PreparationId.ToString("N")) return ("StrictReviewUnavailable", null);
        if (_timeProvider.GetUtcNow().ToUniversalTime() >= owned.Review.ExpiresUtc)
        {
            _strictReview = null;
            return ("StrictReviewExpired", null);
        }
        if (!input.RecoverySaved || !input.UnsavedWorkConfirmed
            || !string.Equals(input.TypedConfirmation.Trim(), owned.Review.ConfirmationText, StringComparison.Ordinal))
            return ("StrictConfirmationMismatch", null);

        var status = await client.GetStatusAsync(token).ConfigureAwait(false);
        if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
        if (!CanStart(status))
        {
            _strictReview = null;
            return ("SessionStateChanged", null);
        }
        if (_usedOperationIds.Count >= 128) return ("OperationLimitReached", null);
        if (!_usedOperationIds.Add(input.OperationId)) return ("DuplicateOperation", null);
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);

        _strictReview = null;
        var activated = await _workflow!.ActivatePreparedAsync(
            new ActivatePreparedSessionRequest(input.PreparationId, owned.Credential), token).ConfigureAwait(false);
        if (!activated.Accepted) return ("StrictActivationRejected", null);
        if (activated.Mode.ToString() != owned.Review.Mode
            || activated.SessionId is not { } sessionId || sessionId == Guid.Empty
            || activated.StartUtc != owned.Review.StartUtc || activated.EndUtc != owned.Review.EndUtc)
            return ("OutcomeUnknown", null);

        var mode = Enum.Parse<SessionMode>(owned.Review.Mode, false);
        var durationMinutes = Math.Clamp((int)Math.Round(owned.Review.DurationSeconds / 60d,
            MidpointRounding.AwayFromZero), 1, 720);
        RememberAcceptedSession(sessionId, owned.ProfileId, owned.Review.ProfileName,
            durationMinutes, mode, owned.PresetId, owned.Configuration,
            owned.Review.EnforcementEnabled, strict: true, objective: input.Objective);
        return (null, new BridgeStrictStarted(sessionId.ToString("D"), activated.StartUtc!.Value, activated.EndUtc!.Value));
    }

    private static IReadOnlyList<string> VisibleLabels(IEnumerable<string> source, out bool truncated)
    {
        var values = source.ToArray();
        truncated = values.Length > MaximumVisibleStrictRules || values.Take(MaximumVisibleStrictRules)
            .Any(value => value.Length > MaximumStrictRuleLabelLength);
        return values.Take(MaximumVisibleStrictRules)
            .Select(value => value[..Math.Min(value.Length, MaximumStrictRuleLabelLength)]).ToArray();
    }

    private static string RecoveryHelperPath() => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FocusGuard", "FocusGuard.Recovery.exe"));
}
