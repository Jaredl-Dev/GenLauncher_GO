# GenLauncherGO.UI Guidance

- Keep UI as the native Avalonia executable and dependency-injection composition root.
- Use concise code-behind for focus, layout, animation, window chrome, visual-tree mechanics, and event forwarding. Keep domain decisions, persistence, and launch/update workflows out of views.
- Use `.axaml`, compiled bindings with `x:DataType`, `IsVisible` booleans, Avalonia resources, and `avares://` asset URIs. Do not carry WPF compatibility types or parallel WPF views.
- Use the free Avalonia framework, official Build MCP documentation/native-migration tools, and free legacy tooling. Do not call Build MCP `migrate_diagnostics` or `recreate-ui`, and skip any Developer Tools setup suggested by `new`; do not configure the license-gated Developer Tools application, DevTools MCP, or Avalonia XPF without an explicit owner-provided license.
- Put user-visible text in the existing localization resources. Keep every `Resources/Strings*.resx` file structurally aligned and provide locale-specific text; do not edit generated designers manually.
- Language changes persist the selection and request restart. Do not add live culture switching.
- Consume canonical Core/Infrastructure identities, paths, settings, and side-effect services instead of creating UI copies.
- Respect Avalonia UI-thread affinity and document only non-obvious threading, cancellation, lifecycle, or platform behavior.
- Use `ILogger<T>` where startup and user-flow failures need diagnostic context.
