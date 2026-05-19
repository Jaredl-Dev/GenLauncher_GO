using System;
using GenLauncherGO.Core.Settings.Contracts;
using GenLauncherGO.Infrastructure.Persistence.Services;
using GenLauncherGO.Infrastructure.Settings.Models;
using GenLauncherGO.Infrastructure.Settings.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GenLauncherGO.Infrastructure.Settings.Composition;

public static class SettingsInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddGenLauncherGoSettingsInfrastructure(
        this IServiceCollection services,
        string preferencesFilePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(preferencesFilePath);

        services.TryAddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        services.AddSingleton<IYamlDocumentStore<LauncherPreferencesSchemaDocument>>(serviceProvider =>
            new YamlDocumentStore<LauncherPreferencesSchemaDocument>(
                preferencesFilePath,
                serviceProvider.GetRequiredService<IAtomicFileWriter>(),
                serviceProvider.GetRequiredService<ILogger<YamlDocumentStore<LauncherPreferencesSchemaDocument>>>()));
        services.AddSingleton<IYamlDocumentStore<LauncherPreferencesDocument>>(serviceProvider =>
            new YamlDocumentStore<LauncherPreferencesDocument>(
                preferencesFilePath,
                serviceProvider.GetRequiredService<IAtomicFileWriter>(),
                serviceProvider.GetRequiredService<ILogger<YamlDocumentStore<LauncherPreferencesDocument>>>()));
        services.AddSingleton<IYamlDocumentStore<LegacyLauncherPreferencesDocument>>(serviceProvider =>
            new YamlDocumentStore<LegacyLauncherPreferencesDocument>(
                preferencesFilePath,
                serviceProvider.GetRequiredService<IAtomicFileWriter>(),
                serviceProvider.GetRequiredService<ILogger<YamlDocumentStore<LegacyLauncherPreferencesDocument>>>()));
        services.AddSingleton<ILauncherPreferencesService, PreferencesService>();
        return services;
    }
}
