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

The server listens on plain HTTP by default (`http://0.0.0.0:5000`, see the
`Kestrel:Endpoints:Http` section in `appsettings.json`). HTTPS is optional —
add an `Https` endpoint to enable it:

```json
"Kestrel": {
  "Endpoints": {
    "Http": { "Url": "http://0.0.0.0:5000" },
    "Https": { "Url": "https://0.0.0.0:5001" }
  }
}
```

or set the `ASPNETCORE_URLS` environment variable, e.g.
`ASPNETCORE_URLS=http://0.0.0.0:5000;https://0.0.0.0:5001`. Locally,
`dotnet run` uses the `http` launch profile by default (`--launch-profile
https` to opt into HTTPS); the Aspire AppHost does the same.

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
   this just wraps `sc.exe create`/`sc.exe delete`; service-managed
   processes have no console, so make sure `appsettings.json` (or
   `ASPNETCORE_URLS`) sets an explicit HTTP(S) endpoint as shown above.

## Arbor.Symbols.ConsoleClient

Scans a local directory for `.dll`, `.exe`, and `.pdb`, creates debugger-compatible symbol requests, downloads from Arbor.Symbols.Server, and stores symbols in Visual Studio symbol-cache structure:

`<symbol-cache>/<fileName>/<identifier>/<fileName>`

Default symbol-cache location:

- Windows: `%LOCALAPPDATA%\\Temp\\SymbolCache`
- Linux/macOS: `~/.vs/symbols`

### Run

```bash
dotnet run --project src/Arbor.Symbols.ConsoleClient/Arbor.Symbols.ConsoleClient.csproj -- \
  /path/to/scan \
  --server http://localhost:5000 \
  --symbol-cache /path/to/symbol-cache
```

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
