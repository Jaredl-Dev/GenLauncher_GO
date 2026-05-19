# GenLauncherGO.UI Guidance

- Keep UI as the native Avalonia executable and dependency-injection composition root.
- Use concise code-behind for focus, layout, animation, window chrome, visual-tree mechanics, and event forwarding. Keep
  domain decisions, persistence, and launch/update workflows out of views.
- Use `.axaml`, compiled bindings with `x:DataType`, `IsVisible` booleans, Avalonia resources, and `avares://` asset
  URIs. Do not carry WPF compatibility types or parallel WPF views.
- Name markup elements purpose first and control type last (`AddModButton`, `ModsList`), never type first.
- Put user-visible text in the existing localization resources. Keep every `Resources/Strings*.resx` file structurally
  aligned and provide locale-specific text; do not edit generated designers manually.
- Language changes persist the selection and request restart. Do not add live culture switching.
- Consume canonical Core/Infrastructure identities, paths, settings, and side-effect services instead of creating UI
  copies.
- Respect Avalonia UI-thread affinity.
- Use `ILogger<T>` where startup and user-flow failures need diagnostic context.
