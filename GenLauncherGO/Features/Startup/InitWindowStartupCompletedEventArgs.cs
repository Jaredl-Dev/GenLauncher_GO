using System;

namespace GenLauncherGO.Features.Startup;

internal sealed class InitWindowStartupCompletedEventArgs : EventArgs
{
    public InitWindowStartupCompletedEventArgs(bool connected)
    {
        Connected = connected;
    }

    public bool Connected { get; }
}
