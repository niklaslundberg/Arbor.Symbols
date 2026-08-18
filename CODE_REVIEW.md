# Code Review — Arbor.Symbols

Reviewed at commit `342e10e` (branch `main` / `claude/repo-code-review-5ovmkv`), 2026-08-18.

## 1. Repo purpose

Arbor.Symbols is a small, focused .NET 10 solution with two runnable pieces:

- **`Arbor.Symbols.Server`** — an ASP.NET Core minimal-API symbol server. It
  serves debugger symbol requests (`/{fileName}/{identifier}/{fileName}`),
  falling back through a local disk cache → the official Microsoft symbol
  server → on-the-fly PDB generation via ILSpy decompilation for local
  assemblies that have no upstream PDB.
- **`Arbor.Symbols.ConsoleClient`** — a CLI that scans a directory for
  `.dll`/`.exe`/`.pdb`, derives their debugger identifiers, and prefetches
  matching symbols from a running server into a Visual Studio-compatible
  symbol cache.

Supporting projects: `Arbor.Symbols.Core` (shared identifier/path logic),
`Arbor.Symbols.ServiceDefaults` (Aspire/OpenTelemetry/health-check/resilience
wiring), `Arbor.Symbols.AppHost` (Aspire orchestration for local dev).

The purpose is narrow and clearly stated — this is an internal/dev-tooling
symbol proxy and cache, not a general-purpose product. The scope is well
matched by the code: no unnecessary abstractions, no speculative generality.

## 2. Fulfillment

The implementation matches the documented behavior end-to-end:

- Cache → official download → ILSpy generation → cache-write fallback chain
  is implemented as described (`SymbolRequestHandler.HandleAsync`).
- The console client's scan → identify → concurrent download → VS-cache-layout
  write pipeline matches the README, including exit codes, `--dry-run`,
  `--force`, glob include/exclude, and bounded concurrency.
- HTTP/HTTPS behavior, Windows Service opt-in, and the release script all work
  as documented, and the README explicitly documents non-obvious framework
  behavior (e.g. Kestrel `Endpoints` config beating `ASPNETCORE_URLS`) instead
  of leaving it to be rediscovered.

No gap was found between what the README promises and what the code does —
a genuine strength for a repo this size.

## 3. Code quality

Overall quality is good: modern idiomatic C# (records, primary constructors,
collection expressions, pattern-matching `switch` expressions), consistent
`Nullable`/`ImplicitUsings`/`TreatWarningsAsErrors` enabled solution-wide, and
small, single-purpose classes.

Specific issues found:

- **Race condition on concurrent cache writes (Server).**
  `SymbolStorage.SaveAsync` (`src/Arbor.Symbols.Server/SymbolStorage.cs:29-41`)
  writes directly to the final cache path via `File.Create(path)` with no
  temp-file-then-rename step and no per-key locking. Two concurrent requests
  for the same missing symbol (a very plausible scenario — e.g. two debugger
  clients requesting the same PDB at once) can both reach `SaveAsync`
  simultaneously; interleaved writes can corrupt the cached file, and a third
  reader hitting `TryOpenRead` mid-write can get a partial/zero-byte file back
  as a "cache hit". The `ConsoleClient` already solves this correctly with a
  temp-file + atomic `File.Move` pattern
  (`src/Arbor.Symbols.ConsoleClient/Program.cs:105-120`); the server should use
  the same pattern (and/or a keyed async lock per request path) for
  consistency and correctness.

- **Synchronous CPU-bound work presented as async (Server).**
  `IlSpySymbolGenerator.TryGeneratePdbAsync`
  (`src/Arbor.Symbols.Server/IlSpySymbolGenerator.cs:13-61`) does the entire
  decompilation and PDB write synchronously and returns via
  `Task.FromResult(...)`. It never actually yields, so it runs on and blocks
  whatever thread-pool thread is handling the HTTP request for as long as
  decompilation takes (the code itself warns when this exceeds 5 seconds).
  Under concurrent uncached-PDB requests this can starve the ASP.NET Core
  thread pool. Consider `Task.Run` plus a bounded concurrency limiter
  (`SemaphoreSlim`) so generation load is capped and doesn't block request
  dispatch.

- **Double buffering of official downloads.**
  `SymbolRequestHandler.HandleAsync`
  (`src/Arbor.Symbols.Server/SymbolRequestHandler.cs:42-54`) reads the entire
  official-symbol-server response into a `MemoryStream`, converts it to a
  `byte[]`, wraps that in a second `MemoryStream` to persist to disk, and
  returns the same `byte[]` again via `Results.File`. For large PDBs this is
  three copies of the payload resident in memory per concurrent request. Save
  to disk first, then serve via `Results.Stream`/`SendFileAsync` from the
  saved file (mirroring the cache-hit path) to avoid the extra buffering.

- **Unhandled path-validation exceptions surface as bare 500s.**
  `SymbolResourcePathHelper.GetCachePath` throws `InvalidOperationException`
  for path-separator injection or a resolved path that escapes the cache
  root (`src/Arbor.Symbols.Core/SymbolResourcePathHelper.cs:111-133`). This is
  good defense-in-depth, but neither `SymbolRequestHandler.HandleAsync` nor
  `UiEndpoints.DeleteCacheEntry` catches it — a crafted request currently
  produces an unhandled-exception 500 instead of a clean `400 Bad Request`.
  Low severity (the traversal itself is blocked) but worth tightening, and
  it's currently untested (see §5).

- **Concrete-class dependency breaks the interface pattern.**
  `SymbolStorage` is injected as a concrete class everywhere
  (`SymbolRequestHandler`, `UiEndpoints`, `Program.cs`), while its siblings
  (`IOfficialSymbolClient`, `IIlSpySymbolGenerator`) are behind interfaces.
  That inconsistency is why there's no unit test for `SymbolRequestHandler`
  in isolation — only integration tests exercise it through a real
  `WebApplicationFactory`. Extracting an `ISymbolStorage` would make the
  handler's branching logic (cache hit / official / ILSpy / not-found)
  unit-testable without spinning up a host.

None of the above are severe; they're the kind of thing that matters once
the server sees concurrent, adversarial, or high-volume traffic — see §7.

## 4. Documentation

The README is a genuine strength: it documents behavior, endpoints, run
instructions, the HTTP/HTTPS precedence gotcha, Windows Service opt-in, CLI
options/exit codes, build/test/release commands, and even *why* certain
non-obvious framework behaviors exist (Kestrel endpoint precedence, AppHost
launch-profile limitations). `THIRD_PARTY_NOTICES.md` and `LICENSE` (MIT) are
present and appropriate.

Gaps:

- No `CONTRIBUTING.md` or architecture diagram — minor for a repo this size,
  but the multi-fallback request flow in `SymbolRequestHandler` would benefit
  from a one-paragraph "why this order" note (cache → official → ILSpy) either
  in the README or as a code comment, since the ordering encodes a real
  policy decision (prefer real symbols over decompiled ones). The README's
  "Behavior" section lists the steps but doesn't spell out the *why*.
- No `CHANGELOG.md`; not essential given GitHub's PR/release history, but
  worth a mention if this project starts shipping versioned releases via
  `scripts/release.sh` regularly.
- No XML doc comments on public types (`SymbolStorage`, `SymbolRequestHandler`,
  `SymbolResourcePathHelper`, etc.). Acceptable for an application-internal
  API surface; would matter more if `Arbor.Symbols.Core` were ever packaged
  and consumed externally.
- The CLI's own `--help` output (README §"Arbor.Symbols.ConsoleClient") is a
  nice touch — self-documenting for both humans and AI agents, as the README
  itself notes.

## 5. Test coverage

677 lines of test code against 1413 lines of source (~48% by line count) is
a reasonable ratio for a project this size, and the test *mix* is sensible:
unit tests for pure logic (`CliOptions`, `SymbolRequestScanner`,
`SymbolResourcePathHelper`), integration tests against a real `TestServer`
(`SymbolEndpointTests`, `HealthCheckEndpointTests`, `UiEndpointTests`), and a
separate system-test project that exercises the real ILSpy decompilation
pipeline end-to-end and validates the resulting PDB is a well-formed portable
PDB with source documents — a good choice given how hard that path would be
to fake convincingly.

Notable coverage gaps:

- **No test exercises the path-traversal guard.**
  `SymbolResourcePathHelper.GetCachePath`'s separator-rejection and
  root-escape checks (the security-relevant code in §3) have zero direct
  test coverage. Given this is the one piece of code standing between a raw
  route parameter and a filesystem path, it should have explicit tests for:
  a requested/identifier/resource segment containing `..`, one containing a
  path separator, and one that resolves outside the cache root.
- **No test for the concurrent-write race described in §3** — understandably
  hard to hit reliably, but a basic "two concurrent first-requests for the
  same missing symbol don't corrupt the cached file" test would at least
  pin down current (broken) behavior and prove a fix.
- **`SymbolStorage` has no dedicated unit tests** — `GetCachedSymbols`,
  `GetDiskUsageBytes`, and `TryDelete` (including its empty-directory cleanup)
  are only exercised indirectly through the UI integration tests, and several
  branches (I/O exceptions during enumeration, nested-directory cleanup) are
  never hit.
- **`OfficialSymbolClient` has no tests** — the `HttpRequestException` catch
  and non-success-status-code path in
  `src/Arbor.Symbols.Server/OfficialSymbolClient.cs:9-26` are untested.
- **The `/ui` loopback restriction is untested.** `Program.cs` adds an
  endpoint filter that 403s non-loopback remote addresses
  (`src/Arbor.Symbols.Server/Program.cs:50-60`) — this is a real access
  control boundary and has no test proving it actually rejects a non-loopback
  caller (admittedly awkward to simulate via `WebApplicationFactory`, but
  worth a `TestServer`-level attempt or at least a unit test around the
  filter logic extracted into a testable function).
- **`ConsoleClient.Program.cs`'s download loop is untested end-to-end.**
  The scanner and CLI parsing are well covered, but the actual
  download/skip/force/dry-run/atomic-rename/exit-code logic lives in
  top-level statements and is only reachable manually — it can't be unit
  tested as written. Extracting it into a testable class (e.g.
  `SymbolPrefetcher.RunAsync(...)`) would let exit-code and atomic-write
  behavior be verified directly instead of relying on manual/system testing.

## 6. Architecture

The project layout is appropriately small: `Core` (shared, dependency-free
domain logic) → `Server`/`ConsoleClient` (the two entry points) →
`ServiceDefaults`/`AppHost` (cross-cutting Aspire/OTel wiring, cleanly
separated from business logic). Tests mirror this with Unit/Integration/System
tiers rather than one undifferentiated test project. This is the right amount
of structure for the problem — no unnecessary layering, mediator/CQRS, or
repository abstractions over what is fundamentally file I/O plus two HTTP
calls.

Minor architectural notes:

- `SymbolRequestHandler` mixes orchestration, logging, and statistics
  recording in one 139-line class. It's still readable, but the ILSpy
  matching loop (`TryGenerateAndStorePdbAsync`, lines 72-131) is doing a
  three-level nested-directory/extension scan with a lot of debug logging;
  it would read more clearly extracted as its own small class/method group,
  separate from the top-level cache/official/generate decision tree.
- The symbol cache is purely local-disk, per-instance. That's fine for the
  documented single-instance use case, but if this is ever run as multiple
  replicas behind a load balancer, there's no shared cache/consistency story
  — each instance would redundantly hit the official server and redundantly
  run ILSpy generation. Worth a README note if horizontal scaling is ever a
  goal, since it isn't today.
- `UiEndpoints.Dashboard` recomputes `GetCachedSymbols()` (a full recursive
  directory walk stat-ing every cached file) and `GetDiskUsageBytes()`
  (a second full walk) on every `/ui` load. Fine at small cache sizes; will
  degrade linearly with cache size since there's no caching of the listing
  itself. Not a concern today given `/ui` is loopback-only and low-traffic
  by nature.

## 7. Production readiness

This is the area with the most to flag, since the code otherwise reads as
solid dev-tooling.

- **Health checks are Development-only.**
  `Extensions.MapDefaultEndpoints` only registers `/health` and `/alive`
  when `app.Environment.IsDevelopment()`
  (`src/Arbor.Symbols.ServiceDefaults/Extensions.cs:79-87`). This is
  backwards from what most production deployments need — container
  orchestrators (Kubernetes, etc.) rely on liveness/readiness probes that are
  normally *most* needed in Production, not Development. If this server is
  ever deployed behind an orchestrator or load balancer, there is currently
  no way to health-check it there.
- **No authentication/authorization on the symbol endpoints.**
  The two download endpoints and the ILSpy-generation fallback are
  unauthenticated by design, and the Production default binds to
  `0.0.0.0:5000` (`appsettings.Production.json`). The `/ui` endpoints
  are usefully loopback-restricted, but the main endpoints are not — anyone
  who can reach the port can trigger ILSpy decompilation (CPU-bound, up to
  multi-second per the code's own slow-generation warning) for any local
  assembly whose identifier they can guess or brute-force. There's no rate
  limiting to bound this. If this is deployed anywhere less trusted than a
  local dev network, that's a real DoS surface worth at least documenting as
  an assumption ("deploy only on a trusted internal network") if not
  mitigating with rate limiting.
- **No cache eviction / disk quota.** The disk cache grows forever; nothing
  prunes old or unused entries. The only deletion path is the loopback-only
  UI's manual per-entry delete. For a long-running production instance this
  is a slow disk-exhaustion risk with no automated mitigation or documented
  operational procedure.
- **Windows Service logging gap.** `Program.cs` configures Serilog with only
  a Console sink. Under `UseWindowsService()`, the process typically has no
  attached console session, so operators running this as a Windows Service
  (a mode the README explicitly documents and supports via
  `scripts/windows-service.ps1`) may get no visible logs at all. Consider a
  file sink or Windows Event Log sink at minimum for the service-hosted case.
- **No containerization.** The solution pulls in Aspire (which is normally
  paired with container-based deployment) but there's no `Dockerfile`/
  `docker-compose`, and CI never builds a container image. If container
  deployment is a goal, it's currently unaddressed; if it isn't, the Aspire
  dependency for local orchestration only is a reasonable, lighter-weight
  choice but worth confirming that's the intent.
- **CI is Linux-only despite Windows-specific code paths.**
  `.github/workflows/ci.yml` runs only on `ubuntu-latest`. The Windows
  Service hosting path (`UseWindowsService`), the Windows branch of
  `SymbolCacheLocator.GetDefaultVisualStudioSymbolCacheDirectory`, and
  `scripts/windows-service.ps1` are never exercised by CI. Given the README
  treats Windows Service hosting as a first-class, documented feature, at
  least a `windows-latest` build+test leg would catch regressions there.
- **No dependency/vulnerability scanning in CI** (e.g. `dotnet list package
  --vulnerable`, Dependabot, or CodeQL) and no `dotnet format`/analyzer
  enforcement step beyond the compiler's own `TreatWarningsAsErrors`. Low
  effort to add given the CI pipeline already exists.
- **`scripts/release.sh` is entirely manual/local** — there's no CI job that
  produces or publishes release artifacts on tag/release, so release
  reproducibility depends on whoever runs the script locally having the same
  environment. Not a blocker, but worth automating if releases become
  routine.

## Summary

Arbor.Symbols is a well-scoped, cleanly written internal tool that does what
its README says it does, with good documentation and a sensible test mix for
its size. The main risks are concentrated in §7: it is currently shaped as a
**trusted-network dev-tool**, not a hardened production service — no auth,
no rate limiting, no cache eviction, and health checks that are (likely
inadvertently) disabled outside Development. If the intent is to keep this
as an internal/dev-time tool, most of §7 is acceptable as-is and should just
be stated as an assumption in the README; if it's meant to run as a
longer-lived production service, those items are the priority before that
happens. The concurrent cache-write race (§3) is the one correctness issue
worth fixing regardless of deployment target, since it can silently corrupt
cached symbols even in the trusted, single-user, local case the project
targets today.

## 8. Suggested remediation tasks

Each task below is scoped to be a single, self-contained PR: one concern, a
concrete set of files, and a clear "done" condition. Ordered by priority
within each group; groups are independent of each other.

### P0 — Correctness (fix regardless of deployment target)

1. **Make `SymbolStorage.SaveAsync` write atomically.**
   *Files:* `src/Arbor.Symbols.Server/SymbolStorage.cs`.
   Write to a temp file in the same directory (e.g.
   `{destination}.{Guid}.tmp`) and `File.Move(..., overwrite: true)` into
   place, mirroring the pattern already used in
   `src/Arbor.Symbols.ConsoleClient/Program.cs:102-120`. Delete the temp file
   in a `finally` on failure.
   *Done when:* two concurrent `SaveAsync` calls for the same request never
   leave a truncated/interleaved file at the destination, and a new
   regression test (see task 6) passes.

2. **Catch path-validation failures and return 400 instead of 500.**
   *Files:* `src/Arbor.Symbols.Server/SymbolRequestHandler.cs`,
   `src/Arbor.Symbols.Server/UiEndpoints.cs`.
   Wrap the call sites that invoke `SymbolStorage`/`SymbolResourcePathHelper`
   (or catch inside `SymbolStorage`) and translate the existing
   `InvalidOperationException` from `GetCachePath` into
   `Results.BadRequest()`. Don't change `GetCachePath`'s own validation logic.
   *Done when:* a request with a `..`-style segment gets a `400`, not an
   unhandled-exception `500`.

### P1 — Server robustness under load

3. **Bound and offload ILSpy PDB generation.**
   *Files:* `src/Arbor.Symbols.Server/IlSpySymbolGenerator.cs`,
   `src/Arbor.Symbols.Server/Program.cs` (DI registration).
   Run the synchronous decompile/write body on a dedicated worker via
   `Task.Run`, gated by a shared `SemaphoreSlim` (configurable max concurrent
   generations, default e.g. 2) so it can no longer consume unbounded
   thread-pool capacity or run unboundedly in parallel.
   *Done when:* `TryGeneratePdbAsync` no longer runs synchronously on the
   calling (request) thread, and concurrent generation requests beyond the
   configured limit queue rather than all running at once.

4. **Stream official downloads to disk instead of triple-buffering.**
   *Files:* `src/Arbor.Symbols.Server/SymbolRequestHandler.cs`.
   Save the official response stream directly via the existing (now-atomic,
   per task 1) `SymbolStorage.SaveAsync`, then re-open and serve from disk
   via `Results.Stream`/`SendFileAsync` — the same path already used for
   cache hits — instead of materializing the payload into two `MemoryStream`s
   and a `byte[]`.
   *Done when:* no full-payload `byte[]`/extra `MemoryStream` remains in the
   official-download path; existing integration tests
   (`SymbolEndpointTests`) still pass unchanged.

5. **Extract `ISymbolStorage` and unit-test `SymbolRequestHandler`.**
   *Files:* new `src/Arbor.Symbols.Server/ISymbolStorage.cs`,
   `SymbolStorage.cs` (implement it), `SymbolRequestHandler.cs`, `Program.cs`
   (DI registration), new
   `tests/Arbor.Symbols.UnitTests/SymbolRequestHandlerTests.cs`.
   Add unit tests for the four branches (cache hit, official download, ILSpy
   generation, not-found) using fakes for `ISymbolStorage`,
   `IOfficialSymbolClient`, `IIlSpySymbolGenerator` — no `WebApplicationFactory`
   needed.
   *Done when:* `SymbolRequestHandler`'s branching logic has direct unit-test
   coverage independent of the existing integration tests.

### P2 — Test coverage gaps

6. **Add tests for the cache-path traversal guard.**
   *Files:* `tests/Arbor.Symbols.UnitTests/SymbolResourcePathHelperTests.cs`.
   Add cases: a segment containing a path separator throws
   `InvalidOperationException`; a segment of `".."` (no separator) still
   throws because the resolved path escapes the root; a normal request
   resolves under the root as expected (already covered).
   *Done when:* all three cases are asserted; this also backs task 2's `400`
   behavior with a lower-level guarantee.

7. **Add unit tests for `SymbolStorage`.**
   *Files:* new `tests/Arbor.Symbols.UnitTests/SymbolStorageTests.cs`.
   Cover `GetCachedSymbols` (empty cache, populated cache, nested structure),
   `GetDiskUsageBytes`, and `TryDelete` (missing entry, present entry, and
   that emptied `identifier`/`fileName` directories are cleaned up but
   non-empty ones are left alone).
   *Done when:* these three methods have direct unit coverage instead of
   only being exercised incidentally through `UiEndpointTests`.

8. **Add unit tests for `OfficialSymbolClient`.**
   *Files:* new `tests/Arbor.Symbols.UnitTests/OfficialSymbolClientTests.cs`
   (using a mocked `HttpMessageHandler` or `Microsoft.Extensions.Http`
   test helpers).
   Cover: success response returns a stream, non-success status returns
   `null`, and `HttpRequestException` is swallowed and returns `null`.
   *Done when:* all three branches of `TryDownloadAsync` are covered.

9. **Add a test proving the `/ui` loopback restriction actually rejects
   non-loopback callers.**
   *Files:* `tests/Arbor.Symbols.IntegrationTests/UiEndpointTests.cs` or a
   new focused test file.
   If simulating a non-loopback `RemoteIpAddress` through
   `WebApplicationFactory`/`TestServer` proves impractical, extract the
   filter's decision (`IPAddress -> IResult?`) into a small testable function
   and unit-test that directly instead.
   *Done when:* there is an automated test that fails if the loopback check
   is ever accidentally removed or weakened.

10. **Extract the ConsoleClient download loop into a testable class.**
    *Files:* `src/Arbor.Symbols.ConsoleClient/Program.cs` (extract to new
    `SymbolPrefetcher.cs`), new
    `tests/Arbor.Symbols.UnitTests/SymbolPrefetcherTests.cs`.
    Move the `Parallel.ForEachAsync` download/skip/force/dry-run/atomic-write
    body into a class with an injectable `HttpMessageHandler`/`HttpClient`,
    returning a result with downloaded/skipped/failed counts (i.e. today's
    exit-code inputs). Keep `Program.cs` as thin argument-parsing +
    exit-code glue.
    *Done when:* `--force`, `--dry-run`, skip-if-cached, and the atomic
    temp-file-then-rename behavior are each covered by a unit test that
    doesn't require a running server.

### P3 — Production readiness (do based on deployment intent)

11. **Make health checks available outside Development, or document why not.**
    *Files:* `src/Arbor.Symbols.ServiceDefaults/Extensions.cs`.
    Either drop the `IsDevelopment()` guard around `MapHealthChecks` (typical
    for orchestrator liveness/readiness probes), or — if the guard is
    intentional — add a one-line comment explaining why health endpoints are
    deliberately Development-only, plus a README note for anyone deploying
    behind an orchestrator.
    *Done when:* either the endpoints are reachable in Production, or the
    intentional-Development-only design is documented in both the code and
    the README.

12. **Decide and document the trust boundary for the download endpoints.**
    *Files:* `README.md`, optionally
    `src/Arbor.Symbols.Server/Program.cs`.
    At minimum, add a README statement that the server is intended to run on
    a trusted internal network with no built-in auth/rate limiting on the
    download/generation endpoints. If broader exposure is a real scenario,
    follow up with rate limiting (ASP.NET Core's built-in
    `Microsoft.AspNetCore.RateLimiting` middleware is a low-effort fit) on
    the `.pdb`-generation path specifically, since that's the CPU-expensive
    one.
    *Done when:* the trust assumption is explicit in the README, or rate
    limiting is in place if the assumption doesn't hold.

13. **Add a cache eviction policy, or document that cleanup is manual-only.**
    *Files:* `src/Arbor.Symbols.Server/SymbolServerOptions.cs`,
    `SymbolStorage.cs`, `README.md`.
    Either add an opt-in size/age-based eviction (e.g. a `MaxCacheSizeBytes`
    option checked on `SaveAsync`, evicting oldest-by-`LastWriteTimeUtc`
    first), or explicitly document in the README that disk usage is
    unbounded and the `/ui` delete action is currently the only cleanup
    mechanism.
    *Done when:* either eviction exists and is covered by a test, or the
    README carries an explicit "unbounded cache, manual cleanup only"
    statement under Server behavior.

14. **Give the Windows Service hosting mode a durable log sink.**
    *Files:* `src/Arbor.Symbols.Server/Program.cs`,
    `appsettings.Production.json`.
    Add a file sink (e.g. `Serilog.Sinks.File`) or Windows Event Log sink,
    at least when running under `UseWindowsService()`, so service-hosted logs
    aren't silently dropped by the console-only sink.
    *Done when:* running the published app as a Windows Service (per the
    README's existing instructions) produces log output somewhere durable
    on disk or in Event Viewer.

### P4 — CI / tooling

15. **Add a `windows-latest` leg to CI.**
    *Files:* `.github/workflows/ci.yml`.
    Build and test on `windows-latest` alongside the existing
    `ubuntu-latest` job (matrix build), so `UseWindowsService`,
    `SymbolCacheLocator`'s Windows branch, and `scripts/windows-service.ps1`
    are exercised by CI.
    *Done when:* CI runs the full test suite on both operating systems and
    is green on both.

16. **Add dependency vulnerability scanning to CI.**
    *Files:* `.github/workflows/ci.yml`.
    Add a step running `dotnet list package --vulnerable --include-transitive`
    (failing the build on results) or enable Dependabot/CodeQL for the repo.
    *Done when:* CI fails if a known-vulnerable package version is
    introduced.

17. **Enforce formatting in CI.**
    *Files:* `.github/workflows/ci.yml`.
    Add a `dotnet format --verify-no-changes` step. Given
    `TreatWarningsAsErrors` is already solution-wide, this closes the
    remaining style-consistency gap cheaply.
    *Done when:* CI fails on unformatted code.

Tasks 1–10 are the ones worth doing regardless of how this project is used
going forward; 11–14 depend on whether it's meant to run as a longer-lived
service beyond a single trusted dev machine, and 15–17 are cheap CI
hardening with no design decisions attached.
