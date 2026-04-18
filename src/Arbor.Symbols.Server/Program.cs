using Arbor.Symbols.Server;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<SymbolServerOptions>()
    .Bind(builder.Configuration.GetSection(SymbolServerOptions.SectionName))
    .ValidateOnStart();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddSingleton(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SymbolServerOptions>>().Value;
    return new SymbolStorage(options);
});

builder.Services.AddSingleton<IIlSpySymbolGenerator, IlSpySymbolGenerator>();
builder.Services.AddHttpClient<IOfficialSymbolClient, OfficialSymbolClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SymbolServerOptions>>().Value;
    client.BaseAddress = new Uri(options.OfficialSymbolServerBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton<SymbolRequestHandler>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "Arbor.Symbols.Server", status = "ok" }));

app.MapGet("/{requestedFileName}/{identifier}/{resourceFileName}",
    (string requestedFileName, string identifier, string resourceFileName, SymbolRequestHandler handler, CancellationToken cancellationToken)
        => handler.HandleAsync(requestedFileName, identifier, resourceFileName, cancellationToken));

app.MapGet("/symbols/{requestedFileName}/{identifier}/{resourceFileName}",
    (string requestedFileName, string identifier, string resourceFileName, SymbolRequestHandler handler, CancellationToken cancellationToken)
        => handler.HandleAsync(requestedFileName, identifier, resourceFileName, cancellationToken));

app.Run();

public partial class Program;
