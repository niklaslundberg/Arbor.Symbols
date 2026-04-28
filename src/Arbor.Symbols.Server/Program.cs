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
builder.Services.AddSingleton<SymbolServerStatistics>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "Arbor.Symbols.Server", status = "ok" }));

var uiEndpoints = app.MapGroup("/ui").RequireHost("localhost", "127.0.0.1");

uiEndpoints.MapGet("", (SymbolServerStatistics statistics, SymbolStorage storage)
    => UiEndpoints.Dashboard(statistics, storage));

uiEndpoints.MapDelete("/cache/{requestedFileName}/{identifier}/{resourceFileName}",
    (string requestedFileName, string identifier, string resourceFileName, SymbolStorage storage)
        => UiEndpoints.DeleteCacheEntry(requestedFileName, identifier, resourceFileName, storage));

app.MapGet("/{requestedFileName}/{identifier}/{resourceFileName}",
    (string requestedFileName, string identifier, string resourceFileName, SymbolRequestHandler handler, CancellationToken cancellationToken)
        => handler.HandleAsync(requestedFileName, identifier, resourceFileName, cancellationToken));

app.MapGet("/symbols/{requestedFileName}/{identifier}/{resourceFileName}",
    (string requestedFileName, string identifier, string resourceFileName, SymbolRequestHandler handler, CancellationToken cancellationToken)
        => handler.HandleAsync(requestedFileName, identifier, resourceFileName, cancellationToken));

app.Run();

public partial class Program;
