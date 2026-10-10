using System.ComponentModel;

namespace FocusGuard.Ipc.Windows;

internal sealed class IpcPolicyDeniedException(string policyCode, string message) : UnauthorizedAccessException(message)
{
    internal string PolicyCode { get; } = policyCode;
}

/// <summary>Fixed diagnostic codes only; never retain exception messages or request data.</summary>
public sealed record IpcFailureDiagnostic(string Code, string ExceptionType, int HResult, int? NativeError)
{
    public static IpcFailureDiagnostic FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        int? nativeError = null;
        var code = "Unclassified";
        Exception? current = exception;
        for (var depth = 0; current is not null && depth < 8; depth++, current = current.InnerException)
        {
            if (current is Win32Exception native) nativeError ??= native.NativeErrorCode;
            var known = current.Message switch
            {
                "Only the provisioned owner can use IPC." => "OwnerDenied",
                "Client deployment/role proof denied." => "ClientDeploymentDenied",
                "Current-process metadata consent required." => "ConsentMissing",
                "Native caller identity unavailable." => "CallerIdentityUnavailable",
                "Pipe caller token unavailable." => "CallerTokenUnavailable",
                "Interactive logon SID unavailable." => "LogonSidUnavailable",
                "IPC owner denied." => "ServerOwnerDenied",
                "IPC caller or operation denied." => "RoleOrOperationDenied",
                "Pipe image is outside the installed root." => "ClientRootDenied",
                "Untrusted deployment owner." => "DeploymentOwnerDenied",
                "Deployment has an untrusted write grant." => "DeploymentWriteGrantDenied",
                "Pipe server is not the running own-process service." => "ServiceIdentityDenied",
                "Service image proof denied." => "ServiceImageDenied",
                "Service changed during verification." => "ServiceChanged",
                "IPC service deployment denied." => "ManifestDenied",
                _ => null
            };
            if (known is not null) code = known;
            if (current is IpcPolicyDeniedException denied)
                code = denied.PolicyCode switch
                {
                    "processUnavailable" => "PolicyProcessUnavailable",
                    "ownerScopeDenied" => "PolicyOwnerScopeDenied",
                    "untrustedDeployment" => "PolicyUntrustedDeployment",
                    "imageDenied" => "PolicyImageDenied",
                    "operationDenied" => "PolicyOperationDenied",
                    _ => "Unclassified"
                };
        }
        return new(code, exception.GetType().Name, exception.HResult, nativeError);
    }
}
