# GenLauncherGO.Tests Guidance

## Test gate

- Before editing production or test code, tell the user which tests you will add or change and what material behavior or
  risk each protects, or state why none qualify. Continue without waiting unless the user requests an approval gate.
- Admit a test only if it would catch a realistic regression in important observable behavior, an external contract, or
  a safety invariant, or prevent a costly failure. Reject all others.
- Use the suite's deployment-recovery, owned-path and reparse-point safety, remote-YAML compatibility, atomic-persistence,
  cancellation-and-race, and user-workflow scenarios as the quality bar.
- Never admit tests for coverage, code existence, implementation mirroring, routine properties or guards, framework
  behavior, private helpers, DI descriptors, or exhaustive low-value permutations. Prefer the fewest representative
  scenarios; overlap only where destructive file-system or security boundaries require defense in depth.

## Red-green workflow

- Write and narrowly run each test before changing production behavior. Red must be the intended behavioral failure;
  compilation, setup, unrelated, environmental, and flaky failures do not count.
- Red may be skipped only for a characterization test required before a behavior-preserving refactor. Explain it before
  editing and in the handoff; never use this exception for a feature or defect fix.
- After red, make the smallest production change, rerun the target to green, then run only the smallest additional test
  scope needed to cover the affected behavior. Do not default to the entire test project or solution; broaden only under
  the root guidance's explicit criteria. Report the red and green commands and outcomes, or why no test qualified, in
  the handoff.

## Test design

- Prefer a small handwritten fake over a substitute when it makes stateful behavior clearer.
- Keep headless Avalonia tests semantic: verify compiled AXAML loads and that meaningful user states expose the expected
  content, actions, accessibility, and theme resources. Protect exact appearance with the smallest practical rendered or
  golden-image coverage, not assertions over coordinates, margins, grid positions, control dimensions, template-part
  structure, or internal visual-tree shape.
- Do not use real-time animation midpoint assertions or no-throw framework smoke tests. Test application-owned state
  transitions and outcomes instead.
- Keep one focused composition test; do not mirror every registration.
- Use isolated temporary directories for file-system tests. Never require a real game installation, live network
  service, or production credential.
- Protect exact remote YAML binding and its single mapping into normalized concepts with representative fixtures.
- Reuse shared builders, fakes, the Avalonia headless UI runner, and canonical authorities instead of copying setup or
  expected constants.
- Structure tests as arrange, act, and assert separated by blank lines. Add phase comments only when a boundary is
  genuinely ambiguous; repeated act phases or branching assertions normally mean the behavior should be split.
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

A helper whose own name says more than the prefix keeps that name instead: `CompletedGameProcessLaunchOperation`,
`QueueHttpMessageHandler`, `ManualTimeProvider`. Scopes that restore state on dispose end in `Scope`.
