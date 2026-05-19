# GenLauncherGO Agent Guidelines

GenLauncherGO is a small Windows launcher with a native Avalonia UI for Generals and Zero Hour community clients.

## Workflow

- Preserve existing behavior unless the user explicitly requests a change.
- Preserve user changes; avoid unrelated cleanup.
- Search for the current owner and callers before adding or replacing shared behavior.
- For Avalonia or WPF-to-Avalonia work, use the repository-configured `avalonia-docs` MCP server and load its expert rules first.
- Use only free Avalonia tooling: the Build MCP documentation, expert-rule, API, mapping, and native-migration tools, the open-source framework, and free legacy tooling. Do not call `migrate_diagnostics` or `recreate-ui`, and skip any Developer Tools setup suggested by `new`. Do not configure the Developer Tools application, DevTools MCP, Avalonia XPF, or another commercial feature unless the owner supplies a license and requests it.
- Verify with `dotnet build GenLauncherGO.sln`, `dotnet format GenLauncherGO.sln --verify-no-changes`, and `dotnet test GenLauncherGO.sln`. Use narrow commands while iterating, then all three across the solution before handoff.
- Trust a diagnostic count only once the build reports no compiler errors. A project that fails to compile reports nothing of its own and hides every diagnostic in the projects downstream of it.
- Apply a bulk fix only at the sites the tool reported, never file-wide, then rebuild the whole solution. A change that compiles where you made it can still break callers in another project, and `dotnet format` rewrites code rather than only whitespace.

## Project Boundaries

| Project | Owns |
| --- | --- |
| `GenLauncherGO.Core/` | Domain rules, values, validation, intentional cross-project contracts |
| `GenLauncherGO.Infrastructure/` | Disk, network, archives, processes, hashing, persistence, logging adapters |
| `GenLauncherGO.UI/` | Native Avalonia presentation and the composition root |
| `GenLauncherGO.Tests/` | Observable behavior, safety, compatibility, and invariant tests |

Read the nearest nested `AGENTS.md` before editing a project. There is intentionally no `src/` folder.

## Design Gates

- Optimize for a small launcher: prefer direct calls and concrete `internal sealed` types.
- Core has no external consumers. Do not keep unused public APIs, old names, adapters, or compatibility shims.
- Maintain one authority for content identity, executable names, type mapping, owned paths, settings, and other shared rules. Reuse or move it; never copy it.
- Do not add mediator, CQRS, service-locator, or similar frameworks.
- Do not add speculative extension points or edge cases; require current behavior, an external contract, a reproduced defect, or a safety invariant.
- Keep production code feature-first. Do not add a folder or layer for file count, symmetry, or anticipated growth.
- Fixed arguments, localization keys, or one forwarded call do not justify a type.

| New artifact | Allowed only when |
| --- | --- |
| Interface | It is an external or side-effect boundary, or has multiple production implementations. Testing convenience alone is insufficient. |
| Request | It validates a stable operation boundary or is genuinely shared; never just bundle arguments for one internal call. |
| Result | Callers branch on named outcomes or need structured failure data; never just mirror returned properties. |
| Factory | It selects implementations or owns meaningful construction or lifetime policy; never merely call `new`. |
| Coordinator | It owns sequencing, state, rollback, or lifecycle; never merely forward calls or group dependencies. |
| Mapper or DTO | It crosses an external or persistence boundary. Map once; do not add an intermediate mirror model. |
| Wrapper | It adds an invariant, ownership, or policy. Otherwise call the existing type directly. |

## Non-Negotiable Constraints

- The remote YAML/backend contract is external. Preserve its accepted keys, shapes, defaults, and semantics at the Infrastructure boundary.
- Launch preparation mutates a user's game folder. Preserve ownership, containment, rollback, and recovery defenses.
- Fix style, naming, quality, and formatting violations in the code. Never make a change compile or a test pass by weakening a gate: no new `.editorconfig` severity downgrade or opt-out, no suppression or `#pragma`, no `NoWarn`, no analyzer or warnings-as-errors property change, no skipped or deleted test. If a gate is genuinely wrong, say so and stop.
- A rule earns removal rather than exceptions when it reports false positives, its fixer corrupts source, or its remedy costs more than the defect it names. Scoping a rule to a project is fine; a growing list of per-site carve-outs is not. Record the reason beside the exclusion in `.editorconfig`, and raise removal as its own decision rather than as a way past the violation in front of you.
- Document cross-project contracts and non-obvious side effects, invariants, compatibility constraints, or platform behavior. Do not document obvious implementation details.
- Use `GenLauncherGO` for new names. Do not add a license or release/deployment automation without an explicit owner decision.

## Completion

- Remove superseded code in the same change; do not leave parallel paths without a current caller.
- Inspect the final diff for duplicate logic, avoidable types, widened visibility, and tests coupled to implementation details.
- In the handoff, list every new production interface/request/result/factory/coordinator/wrapper and the gate that justified it; say explicitly when none were added.
- Report reused or changed canonical authorities and all verification run.
- Use Conventional Commits when committing: `type(scope): short imperative summary`.
