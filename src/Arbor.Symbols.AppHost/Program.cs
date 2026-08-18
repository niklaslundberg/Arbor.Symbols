var builder = DistributedApplication.CreateBuilder(args);

// Selects which of the Server's launch profiles (see
// src/Arbor.Symbols.Server/Properties/launchSettings.json) the AppHost
// orchestrates. `--launch-profile` only applies to the AppHost project
// itself (which has no launchSettings.json of its own), so HTTPS is opted
// into via an environment variable instead:
//   ARBOR_SYMBOLS_LAUNCH_PROFILE=https dotnet run --project src/Arbor.Symbols.AppHost
var serverLaunchProfileName = Environment.GetEnvironmentVariable("ARBOR_SYMBOLS_LAUNCH_PROFILE") ?? "http";
builder.AddProject<Projects.Arbor_Symbols_Server>("arbor-symbols-server", launchProfileName: serverLaunchProfileName);

builder.Build().Run();
