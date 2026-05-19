using System;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Core.Settings.Exceptions;
using GenLauncherGO.Core.Settings.Models;
using GenLauncherGO.Infrastructure.Persistence.Services;
using GenLauncherGO.Infrastructure.Settings.Models;
using GenLauncherGO.Infrastructure.Settings.Support;

namespace GenLauncherGO.Infrastructure.Settings.Services;

/// <summary>
///     Persists launcher preferences as a standalone YAML document.
/// </summary>
internal sealed class PreferencesService : ILauncherPreferencesService
{
    private readonly IYamlDocumentStore<LauncherPreferencesDocument> _documentStore;

    private readonly IYamlDocumentStore<LegacyLauncherPreferencesDocument> _legacyDocumentStore;
    private readonly IYamlDocumentStore<LauncherPreferencesSchemaDocument> _schemaDocumentStore;

    public PreferencesService(
        IYamlDocumentStore<LauncherPreferencesSchemaDocument> schemaDocumentStore,
        IYamlDocumentStore<LauncherPreferencesDocument> documentStore,
        IYamlDocumentStore<LegacyLauncherPreferencesDocument> legacyDocumentStore)
    {
        _schemaDocumentStore = schemaDocumentStore ?? throw new ArgumentNullException(nameof(schemaDocumentStore));
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _legacyDocumentStore = legacyDocumentStore ?? throw new ArgumentNullException(nameof(legacyDocumentStore));
        Current = LoadPreferences();
    }

    public event EventHandler<LauncherPreferences>? PreferencesChanged;

    public LauncherPreferences Current { get; private set; }

    public void Update(LauncherPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        LauncherPreferences normalizedPreferences = LauncherPreferencesDocumentMapper.Normalize(preferences);
        if (normalizedPreferences == Current)
        {
            return;
        }

        SavePreferences(normalizedPreferences);

        Current = normalizedPreferences;
        PreferencesChanged?.Invoke(this, Current);
    }

    private LauncherPreferences LoadPreferences()
    {
        if (!_schemaDocumentStore.DocumentExists)
        {
            return new LauncherPreferences();
        }

        LauncherPreferencesSchemaDocument schemaDocument = _schemaDocumentStore.Load(
            new LauncherPreferencesSchemaDocument { SchemaVersion = null });
        if (!schemaDocument.HasSchemaVersion || schemaDocument.SchemaVersion == 0)
        {
            return LoadLegacyPreferences();
        }

        if (schemaDocument.SchemaVersion != LauncherPreferencesDocument.CurrentSchemaVersion)
        {
            return ResetPreferences();
        }

        LauncherPreferencesDocument document = _documentStore.Load(new LauncherPreferencesDocument());
        return document.SchemaVersion == LauncherPreferencesDocument.CurrentSchemaVersion
            ? LauncherPreferencesDocumentMapper.ToPreferences(document)
            : ResetPreferences();
    }

    private LauncherPreferences LoadLegacyPreferences()
    {
        LegacyLauncherPreferencesDocument legacyDocument =
            _legacyDocumentStore.Load(new LegacyLauncherPreferencesDocument());
        if (!legacyDocument.HasKnownValues)
        {
            return ResetPreferences();
        }

        LauncherPreferences migratedPreferences =
            LauncherPreferencesDocumentMapper.MigrateLegacyPreferences(legacyDocument);
        return PersistLoadedPreferences(migratedPreferences);
    }

    private LauncherPreferences ResetPreferences()
    {
        return PersistLoadedPreferences(new LauncherPreferences());
    }

    private LauncherPreferences PersistLoadedPreferences(LauncherPreferences preferences)
    {
        SavePreferences(preferences);
        return preferences;
    }

    private void SavePreferences(LauncherPreferences preferences)
    {
        try
        {
            _documentStore.Save(LauncherPreferencesDocumentMapper.ToDocument(preferences));
        }
        catch (Exception exception)
        {
            throw new LauncherPreferencesPersistenceException(exception);
        }
    }
}
