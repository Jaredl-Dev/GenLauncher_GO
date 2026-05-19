# GenLauncherGO

GenLauncherGO is a Windows mod management utility and launcher for **Command & Conquer: Generals** and **Zero Hour** using the [SuperHackers](https://github.com/TheSuperHackers/GeneralsGameCode) and [GeneralsOnline](https://www.playgenerals.online/) community clients.

It downloads, installs, updates, and launches supported mods, patches, and add-ons while keeping the supported game installations as clean as possible. This codebase is a complete rewrite of [x64-dev/GenLauncher_GO](https://github.com/x64-dev/GenLauncher_GO). That predecessor was derived from the original [GenLauncher project](https://github.com/p0ls3r/GenLauncher).

## What It Does

* Manages mods, patches, add-ons, and manually added content.
* Allows using multiple different mods with the same game installation(s).
* Runs without installation and manages Generals and Zero Hour from one launcher.
* Keeps launcher data contained within its own folder making it fully portable.
* Deploys content using hard links with copy fallback.

## Requirements

* Windows 10 or 11; administrator approval is required.
* A clean Generals or Zero Hour installation with no other game modifications in the game directories.
* Keep game installations outside Windows' `Program Files` directories when possible; User Account Control (UAC) can interfere with modding tools.
* For best performance, keep GenLauncherGO and each game installation on the same NTFS volume. Otherwise, deployment uses file copies.

Built-in clients are detected in each game root:

| Built-in client           | Executable             |
| ------------------------- | ---------------------- |
| TheSuperHackers Generals  | `generalsv.exe`        |
| TheSuperHackers Zero Hour | `generalszh.exe`       |
| GeneralsOnline            | `generalsonlinezh.exe` |

World Builder support is optional.

| Built-in World Builder    | Executable           |
| ------------------------- | -------------------- |
| Original World Builder    | `WorldBuilder.exe`   |
| TheSuperHackers Generals  | `worldbuilderv.exe`  |
| TheSuperHackers Zero Hour | `worldbuilderzh.exe` |

Custom root-level game client and World Builder executables can be registered under **Settings > Custom executables**.

## Quick Start

1. Place a supported client executable in a clean game installation.
2. Place `GenLauncherGO.exe` outside every game installation, preferably on the same NTFS volume.
3. Run it and approve the Windows UAC prompt.

GenLauncherGO temporarily deploys the required mod files into the game directory, launches the game or World Builder, and cleans up deployed files after the launched process closes.

## Launcher Folder Layout

GenLauncherGO creates and uses one `GenLauncherGO Data` folder beside the launcher executable.

`<game>` is `C&C Generals Data` or `C&C Zero Hour Data`; `<name>` is a content entry.

| Path                                                          | Purpose                                                                                 |
| ------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `GenLauncherGO Data\LauncherPreferences.yaml`              | Launcher settings                                                                       |
| `GenLauncherGO Data\<game>\Mods`                            | Installed mods, patches, and add-ons                                                    |
| `GenLauncherGO Data\<game>\Runtime\Cache\Images\<name>`     | Downloaded or user-selected images                                                      |
| `GenLauncherGO Data\<game>\Runtime\Deployment`              | Active deployment manifests, journals, locks, and backups used for cleanup and recovery |
| `GenLauncherGO Data\<game>\Runtime\Integrity`               | Managed-content integrity snapshots                                                     |
| `GenLauncherGO Data\<game>\Runtime\State\LauncherData.yaml` | Local catalog state and installed-content metadata                                      |
| `GenLauncherGO Data\<game>\Runtime\Temp`                    | Temporary download and install staging, cleared on startup                              |
| `GenLauncherGO Data\Logs`                                  | Rolling diagnostic logs                                                                 |

## Development

The repository is pinned to the .NET 10.0.3xx SDK feature band through `global.json`.

Build the solution from the repository root:

```powershell
dotnet build GenLauncherGO.sln
```

Run the Avalonia project:

```powershell
dotnet run --project ./GenLauncherGO.UI/GenLauncherGO.UI.csproj
```

For a full launcher UI session, run the executable outside all supported game installations. Startup validation blocks the launcher when it is placed inside one.

Run tests:

```powershell
dotnet test GenLauncherGO.sln
```

The minimal Windows CI workflow restores, builds, runs the complete test suite, and verifies the supported single-file publish profile. Its symbolic-link tests are required in CI and fail the workflow if the runner cannot execute them; unsupported local accounts report those tests as explicit skips.

To run the same fail-closed symbolic-link coverage locally, enable Windows Developer Mode or use an elevated terminal, then run:

```powershell
$env:GENLAUNCHERGO_REQUIRE_SYMBOLIC_LINK_TESTS = "true"
dotnet test GenLauncherGO.sln
Remove-Item Env:GENLAUNCHERGO_REQUIRE_SYMBOLIC_LINK_TESTS
```

### Architecture

The solution deliberately uses four projects and no `src` folder:

| Project | Responsibility |
| --- | --- |
| `GenLauncherGO.Core` | Dependency-light contracts, launcher rules, models, and path identities |
| `GenLauncherGO.Infrastructure` | Disk, network, archive, process, persistence, integrity, and package-provider implementations |
| `GenLauncherGO.UI` | Native Avalonia presentation, user workflows, localization, and the dependency-injection composition root |
| `GenLauncherGO.Tests` | Observable behavior, compatibility, recovery, and file-system safety tests |

Dependencies point inward: UI can reference Core and Infrastructure, Infrastructure can reference Core, and Core does not reference Avalonia or implementation packages. Interfaces represent intentional project or side-effect boundaries; feature-internal code normally uses sealed concrete types.

Mutable paths carry their owning root so file operations can reject traversal and reparse-point escapes.

### Backend Compatibility

GenLauncherGO consumes an external backend tied to [p0ls3r](https://github.com/p0ls3r) and the original GenLauncher project. This repository does not control that backend, so its legacy remote YAML names and structure are preserved exactly at the Infrastructure boundary and mapped into the application's internal models. Do not rename or reshape that manifest contract without a deliberate compatibility plan coordinated with the backend maintainers.

Generate local test coverage:

```shell
dotnet msbuild ./eng/coverage.proj -target:Coverage
```

The coverage workflow uses Coverlet's VSTest collector and ReportGenerator to write an HTML report to
`artifacts/coverage/index.html`. The runsettings file excludes test assemblies, generated Avalonia XAML files, resource wrappers,
and build output so the report focuses on application code in `GenLauncherGO.Core`, `GenLauncherGO.Infrastructure`, and
`GenLauncherGO.UI`.

Publish the Release single-file Windows executable:

```powershell
dotnet publish ./GenLauncherGO.UI/GenLauncherGO.UI.csproj -p:PublishProfile=WinX64SelfContained -o ./publish
```

Ordinary Debug and Release builds are framework-dependent and do not select a runtime. The explicit
`WinX64SelfContained` profile produces the supported self-contained, single-file Windows x64 distributable. Its output
is intended to contain `GenLauncherGO.exe` as the launcher executable.

## Support

* [GeneralsOnline Website](https://www.playgenerals.online/)
* [GeneralsOnline Discord](https://discord.playgenerals.online)

Please use the GeneralsOnline Discord for bug reports, feature requests, support, and community discussion.

## Contributing

Contributions are welcome. Fork the repository, create a branch, make your updates, test them, and open a pull request with a clear description.

## Credits

* **p0ls3r** - Original creator of GenLauncher, the project from which the predecessor launcher was derived, who also allowed GenLauncherGO to use the existing backend.
* **x64-dev** - Originator of the predecessor GenLauncherGO project and creator of GeneralsOnline.
* **Jaredl-Dev** - Key contributor to the predecessor GenLauncherGO project.

## Donation

Want to support the original creator of GenLauncher? You can donate through [Boosty](https://boosty.to/genlauncher/single-payment/donation/157147?share=target_link).

## Disclaimer

GenLauncherGO is a community-developed tool intended for use with TheSuperHackers community fork and GeneralsOnline. It is not created by, endorsed by, or affiliated with Electronic Arts, or any other rights holder unless explicitly stated.

This rewrite began as a personal project and is being shared as a temporary solution for the community while [GenHub](https://github.com/community-outpost/GenHub) continues to mature and work toward becoming the stable, community-standard launcher. GenLauncherGO is therefore not presented as a perfect, permanent, or definitive solution.
