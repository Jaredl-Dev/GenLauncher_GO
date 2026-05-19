using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using GenLauncherGO.UI.Features.Launcher.Models;

namespace GenLauncherGO.UI.Features.Launcher.Services;

/// <summary>
/// Applies launcher package progress to the Windows taskbar button.
/// </summary>
internal sealed class WindowsTaskbarProgress
{
    private const ulong ProgressScale = 10_000;

    private ITaskbarList3? _taskbarList;
    private nint _windowHandle;
    private bool _isUnavailable;

    /// <summary>
    /// Captures the platform handle after the Avalonia window is opened.
    /// </summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _windowHandle = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (_windowHandle == 0 ||
            _taskbarList != null ||
            _isUnavailable ||
            !OperatingSystem.IsWindowsVersionAtLeast(6, 1))
        {
            return;
        }

        try
        {
            object taskbarList = new TaskbarList();
            _taskbarList = (ITaskbarList3)taskbarList;
            _taskbarList.HrInit();
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException)
        {
            _taskbarList = null;
            _isUnavailable = true;
        }
    }

    /// <summary>
    /// Updates the Windows shell progress state for the attached taskbar button.
    /// </summary>
    public void Update(LauncherTaskbarProgressState state, double value)
    {
        if (_windowHandle == 0 || _taskbarList == null || _isUnavailable)
        {
            return;
        }

        try
        {
            TaskbarProgressFlag taskbarState = state switch
            {
                LauncherTaskbarProgressState.Indeterminate => TaskbarProgressFlag.Indeterminate,
                LauncherTaskbarProgressState.Normal => TaskbarProgressFlag.Normal,
                _ => TaskbarProgressFlag.NoProgress,
            };

            _taskbarList.SetProgressState(_windowHandle, taskbarState);
            if (taskbarState == TaskbarProgressFlag.Normal)
            {
                ulong completed = (ulong)Math.Round(
                    Math.Clamp(value, 0D, 1D) * ProgressScale,
                    MidpointRounding.AwayFromZero);
                _taskbarList.SetProgressValue(_windowHandle, completed, ProgressScale);
            }
        }
        catch (COMException)
        {
            _isUnavailable = true;
        }
    }

    [Flags]
    private enum TaskbarProgressFlag : uint
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11D0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class TaskbarList
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();

        void AddTab(nint windowHandle);

        void DeleteTab(nint windowHandle);

        void ActivateTab(nint windowHandle);

        void SetActiveAlt(nint windowHandle);

        void MarkFullscreenWindow(
            nint windowHandle,
            [MarshalAs(UnmanagedType.Bool)] bool isFullscreen);

        void SetProgressValue(nint windowHandle, ulong completed, ulong total);

        void SetProgressState(nint windowHandle, TaskbarProgressFlag flags);
    }
}
