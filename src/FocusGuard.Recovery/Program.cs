using System.Security.Cryptography;
using System.Security.Principal;
using FocusGuard.Service;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("FocusGuard recovery is available only on Windows.");
    return 2;
}

if (args.Length != 0)
{
    Console.Error.WriteLine("Recovery codes are never accepted as command-line arguments.");
    return 2;
}

using var identity = WindowsIdentity.GetCurrent();
var principal = new WindowsPrincipal(identity);
if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.Error.WriteLine("Run FocusGuard Recovery from an elevated administrator console.");
    return 3;
}

var directory = StrictPolicySnapshotStore.DefaultDirectory;
var keyPath = Path.Combine(directory, StrictPolicyPersistenceWorker.IntegrityKeyFileName);
if (!File.Exists(keyPath))
{
    Console.Error.WriteLine("No provisioned FocusGuard recovery state was found.");
    return 4;
}

byte[] integrityKey;
try
{
    integrityKey = ProtectedData.Unprotect(
        await File.ReadAllBytesAsync(keyPath),
        optionalEntropy: null,
        DataProtectionScope.LocalMachine);
}
catch (Exception exception) when (
    exception is IOException
    or UnauthorizedAccessException
    or CryptographicException)
{
    Console.Error.WriteLine("The protected FocusGuard recovery state could not be opened.");
    return 4;
}

try
{
    var policyStore = new StrictPolicySnapshotStore(integrityKey, directory);
    var load = await policyStore.LoadAsync(DateTimeOffset.UtcNow, CancellationToken.None);
    if (load.Status != StrictPolicyLoadStatus.Active || load.Snapshot is null)
    {
        Console.Error.WriteLine("No active strict session is eligible for recovery.");
        return 5;
    }

    Console.Write("Recovery code: ");
    var code = ReadSecret();
    var requestStore = new DelayedRecoveryRequestStore(integrityKey, directory);
    DelayedRecoveryRequest request;
    try
    {
        request = await requestStore.RequestAsync(
            load.Snapshot,
            code,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
    }
    catch (UnauthorizedAccessException)
    {
        await new RecoveryAuditLog(directory).WriteAsync(
            load.Snapshot.Policy.SessionId,
            DateTimeOffset.UtcNow,
            "RecoveryRejected",
            "InvalidCode",
            CancellationToken.None);
        Console.Error.WriteLine("The recovery code is invalid.");
        return 6;
    }

    await new RecoveryAuditLog(directory).WriteAsync(
        load.Snapshot.Policy.SessionId,
        DateTimeOffset.UtcNow,
        "RecoveryRequested",
        "CodeValidated",
        CancellationToken.None);
    Console.WriteLine(
        $"Recovery is pending. Restrictions will release no earlier than {request.EligibleUtc.ToLocalTime():F}.");
    Console.WriteLine("You may close this utility; the Windows service owns the delay.");
    return 0;
}
finally
{
    CryptographicOperations.ZeroMemory(integrityKey);
}

static string ReadSecret()
{
    var characters = new List<char>(48);
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            return new string([.. characters]);
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
            }

            continue;
        }

        if (!char.IsControl(key.KeyChar) && characters.Count < 64)
        {
            characters.Add(key.KeyChar);
        }
    }
}
