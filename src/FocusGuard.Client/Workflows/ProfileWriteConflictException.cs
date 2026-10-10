using FocusGuard.Storage;

namespace FocusGuard.Client.Workflows;

/// <summary>Known non-commit, not an ambiguous IPC outcome. Reload; never retry a stale edit.</summary>
public sealed class ProfileWriteConflictException(ProfileMutationStatus status) : IOException("The saved profile changed or the operation was rejected.")
{
    public ProfileMutationStatus Status { get; } = status;
}
