using System.Text.Json;

namespace FocusGuard.NativeMessaging;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                return 2;
            }

            var manifestPath = Path.Combine(AppContext.BaseDirectory, NativeHostConfiguration.LocalManifestFileName);
            var manifest = NativeHostConfiguration.Load(manifestPath);
            if (!NativeHostConfiguration.IsAllowedOrigin(args[0], manifest))
            {
                return 3;
            }

#if FOCUSGUARD_PRODUCTION
            if (!OperatingSystem.IsWindows()) return 4;
            using var authenticatedPipes = new FocusGuard.Ipc.Windows.VerifiedPipeConnectionFactory(AppContext.BaseDirectory,
                FocusGuard.Ipc.Windows.IpcClientRole.NativeMessaging);
            FocusGuard.Contracts.PipeConnectionFactories.Current = authenticatedPipes;
#endif
            await NativeHost.RunAsync(
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(),
                new ServiceHealthNativeMessageHandler(
                    new NamedPipeExtensionHealthClient(
                        FocusGuard.Contracts.ExtensionHealthProtocol.PipeName)),
                CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            return 4;
        }
    }
}
