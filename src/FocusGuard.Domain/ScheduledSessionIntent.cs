namespace FocusGuard.Domain;

/// <summary>The original, explicit schedule configuration used to start this session.</summary>
public sealed record ScheduledSessionIntent(string ProfileName, int DurationMinutes, bool EnforcementEnabled)
{
    public bool IsValid() => Profile.ValidateName(ProfileName).IsValid && DurationMinutes is >= 1 and <= 720;
}