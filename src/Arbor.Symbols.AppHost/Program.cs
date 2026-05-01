var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Arbor_Symbols_Server>("arbor-symbols-server");

builder.Build().Run();
