using System;
using System.Collections.Generic;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Infrastructure.Settings.Support;

namespace GenLauncherGO.Tests.Testing;

/// <summary>
///     Records preference updates and applies the same no-op policy as <c>PreferencesService.Update</c>: a value that
///     normalizes to what is already current is neither recorded nor announced.
/// </summary>
/// <remarks>
///     Normalization decides only whether the update is a change. What is recorded is the value the caller passed,
///     because a test seeds <see cref="Current" /> directly and a fake that silently repaired those values would hide
///     the state the caller is being tested against.
/// </remarks>
internal sealed class RecordingLauncherPreferencesService : ILauncherPreferencesService
{
    public RecordingLauncherPreferencesService(LauncherPreferences current)
    {
        Current = current;
    }

    public LauncherPreferences Current { get; private set; }

    public List<LauncherPreferences> Updates { get; } = [];

    public int UpdateCount => Updates.Count;

    public LauncherPreferencesPersistenceException? UpdateFailure { get; init; }

    public event EventHandler<LauncherPreferences>? PreferencesChanged;

    public void Update(LauncherPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (LauncherPreferencesDocumentMapper.Normalize(preferences) ==
            LauncherPreferencesDocumentMapper.Normalize(Current))
        {
            return;
        }

        if (UpdateFailure is not null)
        {
            throw UpdateFailure;
        }

        Current = preferences;
        Updates.Add(preferences);
        PreferencesChanged?.Invoke(this, preferences);
    }
}
