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
