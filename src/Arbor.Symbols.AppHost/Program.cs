var builder = DistributedApplication.CreateBuilder(args);

// Defaults to the "http" launch profile; run with --launch-profile https to opt into HTTPS.
builder.AddProject<Projects.Arbor_Symbols_Server>("arbor-symbols-server", launchProfileName: "http");

builder.Build().Run();
