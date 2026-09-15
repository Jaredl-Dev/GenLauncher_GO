# GenLauncherGO Agent Guidelines

## Workflow

- Preserve existing behavior unless the user explicitly requests a change.
- Preserve user changes; avoid unrelated cleanup.
- Search for the current owner and callers before adding or replacing shared behavior.
- Before changing production code, apply the test gate and red-green workflow in `GenLauncherGO.Tests/AGENTS.md`.
  Announce the qualifying tests and their value, or state why none qualify.
- For Avalonia or WPF-to-Avalonia work, use the repository-configured `avalonia-docs` MCP server and load its expert rules first.
- Use only free Avalonia tooling: the Build MCP documentation, expert-rule, API, mapping, and native-migration tools, the open-source framework, and free legacy tooling. Do not call `migrate_diagnostics` or `recreate-ui`, and skip any Developer Tools setup suggested by `new`. Do not configure the Developer Tools application, DevTools MCP, Avalonia XPF, or another commercial feature unless the owner supplies a license and requests it.
- For code changes, verify compilation with `dotnet build GenLauncherGO.sln`. Prefer the narrowest formatting and test checks that cover the changed behavior: format only the changed files in their affected project, and run the directly affected tests or smallest relevant test group/project. Do not run solution-wide `dotnet format` or `dotnet test` merely because work is ready for handoff.
- Broaden formatting or tests only when narrower checks cannot provide confidence—for example, after shared build/analyzer configuration changes, broad cross-project contract changes or refactors, changes with unclear impact, or when the user explicitly requests full verification. State why a solution-wide check is needed before running it.
- Skip build, format, and test commands for guidance-only or documentation-only changes unless they alter executable examples or another checked artifact.
- Trust a diagnostic count only once the build reports no compiler errors. A project that fails to compile reports nothing of its own and hides every diagnostic in the projects downstream of it.
- Apply a bulk fix only at the sites the tool reported, never file-wide, then rebuild the whole solution. A change that compiles where you made it can still break callers in another project, and `dotnet format` rewrites code rather than only whitespace.

## Project and Feature Ownership

| Project | Owns |
| --- | --- |
| `GenLauncherGO/` | Windows Avalonia application, with behavior, side effects, and presentation together under `Features` |
| `GenLauncherGO.Tests/` | Observable behavior, safety, compatibility, and invariant tests |
| `eng/GenLauncherGO.TestAnalyzers/` | Test-only build tooling enforcing `GLT001`; never an application or publish dependency |

Read the nearest nested `AGENTS.md` before editing a project. There is intentionally no `src/` folder.

## Design Gates

- Optimize for a small launcher: prefer direct calls and concrete `internal sealed` types.
- The application has no external API consumers. Keep types internal and preserve public members required by binding, serialization, and framework contracts. Do not keep unused APIs, old names, adapters, or compatibility shims.
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

- The remote YAML/backend contract is external. Preserve its accepted keys, shapes, defaults, and semantics in the Mods transport documents and their single normalization mapping.
- Launch preparation mutates a user's game folder. Preserve ownership, containment, rollback, and recovery defenses.
- Fix style, naming, quality, and formatting violations in the code. Never make a change compile or a test pass by weakening a gate: no new `.editorconfig` severity downgrade or opt-out, no suppression or `#pragma`, no `NoWarn`, no analyzer or warnings-as-errors property change, no skipped or deleted test. If a gate is genuinely wrong, say so and stop.
- A rule earns removal rather than exceptions when it reports false positives, its fixer corrupts source, or its remedy costs more than the defect it names. Scoping a rule to a project is fine; a growing list of per-site carve-outs is not. Record the reason beside the exclusion in `.editorconfig`, and raise removal as its own decision rather than as a way past the violation in front of you.
- Document external contracts and non-obvious side effects, invariants, compatibility constraints, or platform behavior. Do not document obvious implementation details.
- Use `GenLauncherGO` for new names. Treat `GeneralsOnline` as one word (never "Generals Online") across code, comments, documentation, and user-facing text. Do not add a license or release/deployment automation without an explicit owner decision.

## Completion

- Remove superseded code in the same change; do not leave parallel paths without a current caller.
- Inspect the final diff for duplicate logic, avoidable types, widened visibility, and tests coupled to implementation details.
- In the handoff, list every new production interface/request/result/factory/coordinator/wrapper and the gate that justified it; say explicitly when none were added.
- Report reused or changed canonical authorities and all verification run.
- Use Conventional Commits when committing: `type(scope): Short imperative summary`.
