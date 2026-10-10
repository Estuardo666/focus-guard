using FocusGuard.Contracts;

namespace FocusGuard.ProcessAgent;

public static class ProcessAgentStartupConfiguration
{
    public const string SingleInstanceName = "Local\\" + ProcessAgentProtocol.PipeName;
}
