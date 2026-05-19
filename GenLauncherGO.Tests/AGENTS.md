# GenLauncherGO.Tests Guidance

- Use xUnit, FluentAssertions, and NSubstitute. Prefer a small handwritten fake when it makes stateful behavior clearer.
- Test observable behavior, safety, compatibility mappings, and important invariants. Do not test auto-properties, standard guards, framework behavior, private helpers, or DI descriptors individually.
- Keep headless Avalonia tests semantic: verify compiled AXAML loads and that meaningful user states expose the expected content, actions, accessibility, and theme resources. Protect exact appearance with the smallest practical rendered or golden-image coverage, not assertions over coordinates, margins, grid positions, control dimensions, template-part structure, or internal visual-tree shape.
- Do not use real-time animation midpoint assertions, no-throw framework smoke tests, or one test per obvious property, factory, or guard. Test application-owned state transitions and outcomes instead.
- Keep one focused composition test; do not mirror every registration.
- Use isolated temporary directories for file-system tests. Never require a real game installation, live network service, or production credential.
- Keep hard-link/copy behavior distinct from symbolic-link and unsafe-reparse rejection. Local capability detection may skip when necessary; the complete Windows CI run must fail closed.
- Protect exact remote YAML binding and its single mapping into normalized concepts with representative fixtures.
- Reuse shared builders, fakes, the Avalonia headless UI runner, and canonical authorities instead of copying setup or expected constants.
