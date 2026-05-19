# GenLauncherGO.Core Guidance

- Keep Core dependency-light and side-effect free: no Avalonia or other UI frameworks/resources, Infrastructure, Windows
  APIs, disk, network, processes, archives, hashing implementations, remote DTOs, or logging packages.
- Make a type `public` only when another production project consumes it. Public means intra-solution contract, not
  external compatibility.
- Model durable identity, configuration, and domain facts as immutable values when practical; mutable workflow and UI
  state do not belong here.
- Keep remote YAML names and serialization shapes in Infrastructure; Core receives normalized concepts.
- Pass `CancellationToken` through new asynchronous contracts.
- Keep expected failures explicit only when callers must act on distinct outcomes; otherwise use the simplest normal
  .NET mechanism.
