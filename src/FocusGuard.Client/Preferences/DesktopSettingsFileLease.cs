namespace FocusGuard.Client.Preferences;

/// <summary>One OS-level writer lease and durable atomic replacement shared by local and sync writers.</summary>
public sealed class DesktopSettingsFileLease : IDisposable, IAsyncDisposable
{
    public const int MaxFileBytes = 1_048_576;
    private readonly string path;
    private readonly FileStream lease;
    private DesktopSettingsFileLease(string path, FileStream lease) { this.path = path; this.lease = lease; }

    public static async Task<DesktopSettingsFileLease> AcquireAsync(string path, CancellationToken token = default)
    {
        var fullPath = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new(fullPath, new FileStream(fullPath + ".sync.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
            catch (IOException error) when ((error.HResult & 0xFFFF) is 32 or 33 && attempt < 80)
            { await Task.Delay(25, token).ConfigureAwait(false); }
        }
    }

    public async Task<byte[]?> ReadAsync(CancellationToken token = default)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        if (stream.Length is < 2 or > MaxFileBytes) throw new InvalidDataException("Invalid preferences size.");
        // DesktopSettingsStore also exposes synchronous UI-thread callers. No continuation
        // in this file may require their synchronization context while they wait on the result.
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    public async Task ReplaceAsync(ReadOnlyMemory<byte> bytes, CancellationToken token = default)
    {
        if (bytes.Length is < 2 or > MaxFileBytes) throw new InvalidDataException("Invalid preferences size.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes, token).ConfigureAwait(false); stream.Flush(true); }
            token.ThrowIfCancellationRequested();
            await ReplaceHeldAsync(temporary, path, token).ConfigureAwait(false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task ReplaceHeldAsync(string temporary, string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (File.Exists(path)) File.Replace(temporary, path, null); // Preserve the destination ACL.
        else File.Move(temporary, path);
        return Task.CompletedTask;
    }

    public void Dispose() => lease.Dispose();
    public ValueTask DisposeAsync() => lease.DisposeAsync();
}
