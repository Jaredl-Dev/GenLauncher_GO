# GenLauncherGO.Infrastructure Guidance

- Keep concrete disk, network, archive, process, hashing, persistence, and logging implementations here; do not drive
  Avalonia or other UI workflows.
- Bind the external YAML contract with exact transport DTOs, then map it once into normalized concepts. Preserve
  accepted legacy keys, defaults, nesting, and values.
- Before traversing or mutating owned content, reuse the existing containment and path-safety primitives and fail
  closed when safety cannot be proven.
- Preserve atomic writes, staging cleanup, deployment journaling, rollback, recovery, and hard-link-to-copy fallback
  behavior.
- Use structured `ILogger<T>` diagnostics around meaningful side effects and failures. Do not log credentials, tokens,
  or unnecessary full user paths.
