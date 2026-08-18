# Arbor.Symbols

Arbor.Symbols contains:

- `Arbor.Symbols.Server`: ASP.NET Core (.NET 10) minimal API symbol server.
- `Arbor.Symbols.ConsoleClient`: .NET 10 console client for symbol preloading.

## Symbol protocol overview

Debuggers (Visual Studio, WinDbg, etc.) typically request symbol artifacts with this path shape:

`/{fileName}/{identifier}/{fileName}`

Examples:

- `https://msdl.microsoft.com/download/symbols/System.Private.CoreLib.pdb/6D1E...1/System.Private.CoreLib.pdb`
- `https://your-symbol-server/MyLibrary.dll/65F2A4D31C000/MyLibrary.dll`

`identifier` is derived from PE/PDB metadata (timestamp+image size for PE files, GUID+age/stamp for PDB files).

## Arbor.Symbols.Server

### Trust model

Arbor.Symbols.Server is built for a **trusted internal network / single dev
machine**, not public exposure. The symbol-download and PDB-generation
endpoints have no authentication and no rate limiting; anyone who can reach
the port can request symbols or trigger ILSpy decompilation (CPU-bound, up to
several seconds per assembly per its own slow-generation logging). Only the
`/ui` dashboard is restricted, and only to loopback callers (see below). Run
this behind a firewall / on a private network; don't expose it directly to
the internet or to untrusted clients.

The on-disk symbol cache also has no automatic eviction — it grows without
bound as symbols are requested. The `/ui` dashboard's per-entry delete button
is currently the only cleanup mechanism; there is no size or age-based
pruning. Monitor disk usage yourself if this runs unattended for a long time.

### Behavior

For incoming symbol requests:

1. Check local disk cache (`SymbolServer:CacheDirectory`).
2. If not found, fetch from Microsoft symbol server (`SymbolServer:OfficialSymbolServerBaseUrl`).
3. If still not found and target is a `.pdb`, attempt PDB generation with ILSpy for matching local assemblies (`SymbolServer:AssemblySearchDirectories`).
4. Save generated/downloaded symbol artifact to disk cache for future requests.

### Endpoints

- `GET /{requestedFileName}/{identifier}/{resourceFileName}`
- `GET /symbols/{requestedFileName}/{identifier}/{resourceFileName}`
- `GET /` (health/status)
- `GET /ui` (web dashboard: statistics, disk usage, cached symbol browser)
- `DELETE /ui/cache/{requestedFileName}/{identifier}/{resourceFileName}` (delete a cached symbol entry)

### Run

```bash
dotnet run --project src/Arbor.Symbols.Server/Arbor.Symbols.Server.csproj
```

Configure in `appsettings.json` (`SymbolServer` section).

### HTTP / HTTPS

HTTP is the default everywhere; HTTPS is opt-in. Local runs and deployed
runs are configured independently, since a fixed `Kestrel:Endpoints` entry
in the base `appsettings.json` would otherwise silently win over
`ASPNETCORE_URLS`/launch profiles (Kestrel endpoint configuration takes
precedence over `UseUrls`/`ASPNETCORE_URLS` once any endpoint is
configured):

- **Local (`dotnet run`, `ASPNETCORE_ENVIRONMENT=Development`)** — driven
  by `Properties/launchSettings.json`, which defines the `http` profile
  (default, `http://localhost:5000`) and an `https` profile
  (`https://localhost:5001;http://localhost:5000`). Opt into HTTPS with
  `dotnet run --project src/Arbor.Symbols.Server --launch-profile https`.
  Via the Aspire AppHost, set `ARBOR_SYMBOLS_LAUNCH_PROFILE=https` before
  running it (see `src/Arbor.Symbols.AppHost/Program.cs` — `--launch-profile`
  only applies to the AppHost project itself, which has no launch profiles
  of its own).

- **Deployed (published app, Windows Service, any environment other than
  Development)** — driven by `appsettings.Production.json`, which sets a
  `Kestrel:Endpoints:Http` default of `http://0.0.0.0:5000`. Add an
  `Https` entry there (or override via the `Kestrel__Endpoints__Https__Url`
  environment variable) to opt into HTTPS:

  ```json
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5000" },
      "Https": { "Url": "https://0.0.0.0:5001" }
    }
  }
  ```

### Windows Service (opt-in)

The server can run as a Windows Service — it's opt-in: `Program.cs` calls
`UseWindowsService()`, which is a no-op unless the process is actually
started by the Service Control Manager, so `dotnet run` / a normal console
launch is unaffected.

1. Publish a Windows-targeted, framework-dependent build (needs the
   [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) on
   the target machine, produces a native `Arbor.Symbols.Server.exe`):

   ```powershell
   dotnet publish src\Arbor.Symbols.Server\Arbor.Symbols.Server.csproj `
     --configuration Release --runtime win-x64 --self-contained false
   ```

2. Register and start the service (elevated PowerShell):

   ```powershell
   scripts\windows-service.ps1 -Install -ExePath <path>\Arbor.Symbols.Server.exe
   ```

   Uninstall with `scripts\windows-service.ps1 -Uninstall`. Under the hood
   this just wraps `sc.exe create`/`sc.exe delete`; a service-hosted process
   has no `ASPNETCORE_ENVIRONMENT=Development` from launch profiles, so it
   picks up `appsettings.Production.json`'s HTTP default automatically (see
   above).

   A Windows Service has no attached console session, so in addition to the
   Console sink, Serilog also writes to a rolling daily file under
   `logs/arbor-symbols-<date>.log` next to the published executable
   (14 days retained) — that's where to look for service-hosted logs.

## Arbor.Symbols.ConsoleClient

Scans a local directory for `.dll`, `.exe`, and `.pdb`, creates debugger-compatible symbol requests, downloads from Arbor.Symbols.Server, and stores symbols in Visual Studio symbol-cache structure:

`<symbol-cache>/<fileName>/<identifier>/<fileName>`

Default symbol-cache location:

- Windows: `%LOCALAPPDATA%\\Temp\\SymbolCache`
- Linux/macOS: `~/.vs/symbols`

Run `dotnet run --project src/Arbor.Symbols.ConsoleClient -- --help` for full,
self-contained usage documentation (options, defaults, exit codes, examples)
— it's kept in the tool itself so both humans and AI agents can discover it
without reading source.

### Run

```bash
dotnet run --project src/Arbor.Symbols.ConsoleClient/Arbor.Symbols.ConsoleClient.csproj -- \
  /path/to/scan \
  --server http://localhost:5000 \
  --symbol-cache /path/to/symbol-cache
```

### Options

| Option | Description | Default |
| --- | --- | --- |
| `--server <url>` | Base URL of `Arbor.Symbols.Server`. | `http://localhost:5000` |
| `--symbol-cache <path>` | Destination symbol cache directory. | OS default, see above |
| `--force` | Re-download and overwrite files already present in the cache. | off |
| `--dry-run` | Report what would be downloaded without contacting the server or writing files. | off |
| `--max-concurrency <n>` | Number of symbol requests to run in parallel. | `8` |
| `--include <glob>` | Only scan files matching this glob (repeatable). | `**/*.dll`, `**/*.exe`, `**/*.pdb` |
| `--exclude <glob>` | Skip files matching this glob (repeatable, applied after `--include`). | none |
| `-h`, `--help` | Print usage and exit. | — |

Downloads run concurrently (bounded by `--max-concurrency`) and already-cached
files are skipped unless `--force` is set. Outbound HTTP calls to the server
use the same standard `Microsoft.Extensions.Http.Resilience` (Polly-based)
pipeline as the rest of the solution — automatic retry with backoff, a
request timeout, and a circuit breaker — so transient network issues are
retried automatically; 404 "symbol not found" responses are not retried.

Exit codes: `0` success, `1` invalid arguments, `2` scan directory not found,
`3` completed with one or more failed downloads, `130` cancelled (Ctrl+C).

The client logs download status using Serilog.

## Build and test

```bash
dotnet build Arbor.Symbols.slnx
dotnet test Arbor.Symbols.slnx
```

Package versions are centrally managed in `Directory.Packages.props` (NuGet
Central Package Management); project files reference packages without a
version. Build/publish output is written under `artifacts/` (MSBuild
[artifacts output layout](https://learn.microsoft.com/dotnet/core/sdk/artifacts-output)),
not per-project `bin`/`obj`.

## Release artifacts

```bash
scripts/release.sh [version]
```

Builds `Arbor.Symbols.Server` and `Arbor.Symbols.ConsoleClient` in Release
and publishes each as a **framework-dependent, portable** deployment (no
runtime identifier, no bundled runtime) into `artifacts/release/`, one
`.zip` (or `.tar.gz` if `zip` isn't available) plus a `.sha256` checksum per
project. The resulting archive can be copied to any machine with the
matching [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
installed and run with `dotnet <Project>.dll` — no SDK required on the
target machine.
