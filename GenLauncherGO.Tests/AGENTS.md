# GenLauncherGO.Tests Guidance

- Prefer a small handwritten fake over a substitute when it makes stateful behavior clearer.
- Test observable behavior, safety, compatibility mappings, and important invariants. Do not test auto-properties,
  standard guards, framework behavior, private helpers, or DI descriptors individually.
- Keep headless Avalonia tests semantic: verify compiled AXAML loads and that meaningful user states expose the expected
  content, actions, accessibility, and theme resources. Protect exact appearance with the smallest practical rendered or
  golden-image coverage, not assertions over coordinates, margins, grid positions, control dimensions, template-part
  structure, or internal visual-tree shape.
- Do not use real-time animation midpoint assertions, no-throw framework smoke tests, or one test per obvious property,
  factory, or guard. Test application-owned state transitions and outcomes instead.
- Keep one focused composition test; do not mirror every registration.
- Use isolated temporary directories for file-system tests. Never require a real game installation, live network
  service, or production credential.
- Protect exact remote YAML binding and its single mapping into normalized concepts with representative fixtures.
- Reuse shared builders, fakes, the Avalonia headless UI runner, and canonical authorities instead of copying setup or
  expected constants.
- Structure tests as arrange, act, and assert separated by blank lines. Add phase comments only when a boundary is
  genuinely ambiguous; repeated act phases or branching assertions normally mean the behavior should be split.
- Use surviving mutants to find missing behavior assertions; do not add assertions whose only purpose is raising a score.
- Reach for a shared helper in `Testing/` before writing setup. `GlobalUsings.cs` already imports that namespace, so
  no `using` is needed. Add a helper there only once a second caller exists.

## Naming helpers in `Testing/`

The prefix states what the helper does, so a reader knows from the call site whether it holds state, answers fixed
values, or is there to assert on.

| Prefix | Means |
| --- | --- |
| `Fake` | A hand-written working implementation, simplified but with real behavior and state. |
| `Recording` | Captures the calls a test asserts on, exposed as `List<>` properties. |
| `Stub` | Answers with fixed values and records nothing. |
| `Controllable` | The test decides when the operation completes, usually through a `TaskCompletionSource`. |
| `Test` | Builds inputs — paths, content, view models. Not a test double. |

A helper whose own name says more than the prefix would keeps that name instead: `CompletedGameProcessLaunchOperation`,
`QueueHttpMessageHandler`, `ManualTimeProvider`. Scopes that restore state on dispose end in `Scope`.

## Mutation testing

`eng/mutation.proj` runs one Stryker configuration per production area, each with its own break threshold, so a
weakly covered area cannot hide behind a strong one. Together they cover every behavior-bearing file in Core and
Infrastructure.

`GenLauncherGO.UI` has no configuration and cannot have one: Avalonia emits `InitializeComponent` and the `x:Name`
backing fields from a Roslyn source generator, and Stryker recompiles from parsed syntax trees without running
generators, so every `.axaml.cs` fails with CS0103 and the run aborts. Do not add a UI configuration expecting it to
work. UI quality rests on the coverage backstop and on behavioral tests.

Some mutants stay alive on purpose. Before adding an assertion to kill one, classify it:

- **Equivalent** — the mutated program is genuinely indistinguishable, e.g. `new UTF8Encoding(false)` versus `true`,
  because `GetBytes` never emits a preamble. Leave it.
- **Unobservable** — real but undetectable from a behavior test: durability flags such as `Flush(true)`, buffer sizes,
  `FileOptions` bit combinations, `File.Replace` metadata flags, and exception message text. Asserting message strings
  is barred above, so these stay. Leave them.
- **Needs a production change** — an unreachable branch or a missing seam. Raise it as its own decision; do not
  contort a test around it.
- **Killable** — the mutation changes something a caller or user observes. This is the only kind worth work.

`ignore-methods` in each configuration already filters logging, argument guards, and `ConfigureAwait`; the thresholds
account for the residue that cannot be filtered.
