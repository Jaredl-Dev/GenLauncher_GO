# GenLauncherGO Application Guidance

## Feature ownership

- Keep behavior, its side-effect implementations, and presentation together under `Features`. Use purposeful `Views`
  and resource directories; do not recreate layers through `Contracts`, `Models`, `Services`, or `Support` folders.
- `Startup` owns the host, setup, installation discovery, runtime contexts, and canonical launcher paths. `Settings`
  owns preferences, persistence mapping, and settings/executable-management screens. `Mods` owns content identities,
  catalogs, remote YAML binding, reconciliation, import, images, and mod presentation. `Launching` owns executable
  selection, arguments, launch coordination, process tracking, deployment, and recovery. `Integrity` owns scanning,
  snapshots, targets, resolution, and review. `Updating` owns downloads, package activity/admission, providers, and
  application updates. `Launcher` owns the main window, lists, and coordination across user workflows.
- Keep genuinely shared file safety, HTTP, archives, persistence, logging, localization, themes, controls, and dialogs
  under `Shared`. Reuse canonical identities, executable layouts, paths, preferences, and mappings; never copy them.
- `LauncherApplicationHost.CreateServiceCollection` is the runtime composition authority. Bootstrap must work before
  storage is approved; file logging begins only after setup selects runtime paths. Preserve provider disposal,
  singleton identities, and deferred window creation. Preferences registration has one helper shared by both stages.
- Model durable identity and configuration as immutable values when practical. `LauncherRuntimePathContext` validates
  ownership and atomically publishes immutable path snapshots. Capture operation paths before scheduling work.
- Pass `CancellationToken` through asynchronous boundaries. Use named failure outcomes only when callers act on them.

## Side effects and compatibility

- Bind the external YAML contract with exact transport documents, then map it once into normalized concepts. Preserve
  accepted legacy keys, defaults, nesting, and values, as well as existing persistence, journal, and recovery formats.
- Before traversing or mutating owned content, reuse containment and path-safety primitives and fail closed when safety
  cannot be proven. Preserve reparse-point rejection, ownership checks, atomic writes, staging cleanup, durable
  deployment journaling, rollback, recovery, and hard-link-to-copy fallback.
- Preserve catalog mutation serialization, package leases/admission, process-family tracking, and operation snapshots.
- Use structured `ILogger<T>` diagnostics around meaningful side effects and failures. Do not log credentials, tokens,
  or unnecessary full user paths. Preserve the shared HTTP/MinIO construction and logging/redaction policies.
- Preserve elevation checks, single-instance behavior, startup ordering, culture selection, process cleanup, and
  portable-update behavior. Keep platform, COM, and System32 DLL-search attributes in the application assembly.

## Avalonia presentation

- Use concise code-behind for focus, layout, animation, window chrome, visual-tree mechanics, and event forwarding.
  Keep domain decisions, persistence, and launch/update workflows out of views.
- Use `.axaml`, compiled bindings with `x:DataType`, `IsVisible` booleans, Avalonia resources, and `avares://` asset URIs.
  Do not carry WPF compatibility types or parallel WPF views. Respect Avalonia UI-thread affinity.
- Name markup elements purpose first and control type last (`AddModButton`, `ModsList`), never type first.
- Put user-visible text in the existing localization resources. Keep every `Resources/Strings*.resx` structurally
  aligned and provide locale-specific text; do not edit generated designers manually.
- Language changes persist the selection and request restart. Do not add live culture switching.
- Keep raw published theme values separate from resolved Avalonia palettes; they preserve distinct representations
  and fallback behavior. Preserve bitmap caching and its lifetime policy.
