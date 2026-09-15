# Contributing to GenLauncherGO

Contributions are welcome. Fork the repository, create a branch, make and test your changes, then open a pull request
with a clear description.

## Development setup

The repository selects the .NET 10 SDK through `global.json`, starting at version `10.0.300` and allowing later
feature bands.

Run the Avalonia project from the repository root:

```powershell
dotnet run --project ./GenLauncherGO/GenLauncherGO.csproj
```

For a full launcher UI session, run the executable outside all supported game installations. Startup validation
blocks the launcher when it is placed inside one.

## Required quality gates

Always build the solution after code changes. Run the affected feature tests and verify formatting for the changed
files. Use the full checks below for shared build/analyzer changes, broad refactors, or changes whose impact cannot be
covered narrowly:

```powershell
dotnet build GenLauncherGO.sln
dotnet format GenLauncherGO.sln --verify-no-changes
dotnet test GenLauncherGO.Tests/GenLauncherGO.Tests.csproj -c Release
```

The Windows CI workflow builds Release, verifies formatting, runs the critical-behavior test suite, and builds and
inspects the supported Velopack portable archive. A separate weekly workflow audits vulnerable and deprecated
dependencies.

## Testing policy

Admit a test only if it would catch a realistic regression in important observable behavior, an external contract, or a
safety invariant, or prevent a costly failure. The existing recovery, path-safety, compatibility, persistence, race,
and workflow tests set the bar.

Never admit tests for coverage, code existence, implementation mirroring, routine properties or guards, framework
behavior, private helpers, DI details, or exhaustive low-value permutations. Prefer the fewest representative scenarios;
overlap only where destructive file-system or security boundaries require defense in depth.

Before implementation, identify the qualifying tests and their value, or state why none qualify. Write and narrowly run
each test first; red must be the intended behavioral failure, not a compilation, setup, unrelated, environmental, or
flaky failure. Only a characterization test required for a behavior-preserving refactor may bypass red, and that
exception must be explained before editing. After red, rerun the target to green and the smallest additional scope
needed to cover the affected behavior. Record
the red and green commands and outcomes, or the no-test rationale, in the pull request.

## Symbolic-link safety tests

Symbolic-link tests are required in CI and fail the workflow if the runner cannot execute them. Local accounts that
cannot create symbolic links report those tests as explicit skips. To enforce the same fail-closed behavior locally,
enable Windows Developer Mode or use an elevated terminal, then run:

```powershell
$env:GENLAUNCHERGO_REQUIRE_SYMBOLIC_LINK_TESTS = "true"
dotnet test GenLauncherGO.Tests/GenLauncherGO.Tests.csproj -c Release
Remove-Item Env:GENLAUNCHERGO_REQUIRE_SYMBOLIC_LINK_TESTS
```

## Coverage

Optionally generate a local coverage report:

```powershell
dotnet msbuild ./eng/coverage.proj -target:Coverage
```

The HTML report is written to `artifacts/coverage/index.html`. Coverage is informational: CI does not enforce a
percentage because executing a line does not establish that its behavior is meaningfully protected.

## Publishing

`VersionPrefix` in `GenLauncherGO/GenLauncherGO.csproj` is the single source of truth for the release version.
Update it through a pull request and merge to `master`. After CI passes, run **Actions** > **Publish release** on
`master`.

The workflow builds the Windows x64 portable package and creates a draft GitHub release with the matching
`v<VersionPrefix>` tag. Review its notes and assets, test the archive when appropriate, then publish the draft. Drafts
are not offered to update clients. Do not create the tag or release manually, or reuse a published version.

The packaging tool is a C# file-based app under `eng`. It uses the same .NET 10 SDK as the launcher and needs no
PowerShell version or additional scripting runtime. Use `--no-cache` so the SDK checks and rebuilds the runner rather
than relying on its temporary build cache. From the repository root, run:

```powershell
dotnet run --file ./eng/package-release.cs --no-cache
```

The script downloads the previous release so Velopack can create a delta when appropriate. Use bootstrap mode only
for a feed with no previous release:

```powershell
dotnet run --file ./eng/package-release.cs --no-cache -- --bootstrap
```

To bridge releases from another GitHub repository, specify it for that run:

```powershell
dotnet run --file ./eng/package-release.cs --no-cache -- --previous-release-repository-url https://github.com/OWNER/REPOSITORY
```

Local output goes to `artifacts/release`; the script does not upload anything. Releases must remain portable-only, and
the GitHub workflow is the only publishing path. Before publishing updater or release-process changes, follow the
[two-version local-feed smoke test](docs/portable-update-smoke.md).

## Architecture

The solution uses one application project, one test project, and test-only analyzer tooling, with no `src` folder:

| Project | Responsibility |
| --- | --- |
| `GenLauncherGO` | Windows Avalonia application with feature-local rules, side effects, workflows, and presentation |
| `GenLauncherGO.Tests` | Observable behavior, compatibility, recovery, and file-system safety tests |
| `eng/GenLauncherGO.TestAnalyzers` | Test-only analyzer enforcing the repository's test-method naming convention |

Production code lives under `Features/Startup`, `Settings`, `Mods`, `Launching`, `Integrity`, `Updating`, and `Launcher`.
Rules, implementations, and UI services live beside the workflows they serve; views and resources retain purposeful
directories. `Shared` holds file safety, HTTP, archives, persistence, logging, localization, themes, controls, and dialogs.
Tests follow the same feature groups, with reusable test helpers under `Testing` and naming checks under `Conventions`.

The application and analyzer reference no projects. Tests reference the application and the analyzer as analyzer-only
tooling (`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`). The linked test-name convention source remains
the single authority for `GLT001`. The build enforces this graph and keeps single-file analysis on the application.
Interfaces represent actual side-effect or external boundaries; other code normally uses internal sealed types.

`LauncherApplicationHost.CreateServiceCollection` owns runtime composition. Bootstrap has a separate provider so it can
validate storage before runtime file logging starts; both stages use one preferences-registration helper.

Mutable paths carry their owning root so file operations can reject traversal and reparse-point escapes.

## Backend compatibility

GenLauncherGO consumes an external backend tied to [p0ls3r](https://github.com/p0ls3r) and the original GenLauncher
project. This repository does not control that backend, so its legacy remote YAML names and structure are preserved
exactly in the Mods transport documents and mapped once into normalized application concepts. Do not rename or reshape
that manifest contract without a deliberate compatibility plan coordinated with the backend maintainers.

## Submitting changes

Keep changes focused, preserve existing behavior unless the change deliberately updates it, and include tests for
observable behavior or safety invariants.

### Commit format

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```text
type(scope): Short imperative summary
```

- **Types**: `feat`, `fix`, `test`, `refactor`, `docs`, `build`, `ci`, `chore`.
- **Scope**: Optional, matching the affected feature or shared area (e.g., `launcher`, `launching`, `mods`, `startup`, `settings`, `shared`).
- Use the imperative mood starting with a capital letter (e.g., `fix(mods): Resolve manifest destination collision`). Keep commits atomic and focused.

### Pull requests

Open pull requests against `master`. In the pull request description:

- Use a Conventional Commits title matching your changes.
- **Summary**: Explain what changed and why.
- **Validation**: Describe the verification performed (automated tests run, build checks, or manual testing).
