using System;

namespace GenLauncherGO.UI.Features.Startup.ViewModels;

internal sealed class InitWindowStartupCompletedEventArgs : EventArgs
{
    public InitWindowStartupCompletedEventArgs(bool connected)
    {
        Connected = connected;
    }

    public bool Connected { get; }
}
