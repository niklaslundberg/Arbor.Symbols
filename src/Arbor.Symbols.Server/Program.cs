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

// UI endpoints are restricted to loopback connections (localhost / 127.0.0.1).
// RequireHost matches the Host header, which can be spoofed; the endpoint filter
// additionally validates the actual remote IP address.
// Note: RequireHost in .NET 10 does not support IPv6 address literals, so [::1] is not covered.
var uiEndpoints = app.MapGroup("/ui")
    .RequireHost("localhost", "127.0.0.1")
    .AddEndpointFilter(async (ctx, next) =>
    {
        var remoteIp = ctx.HttpContext.Connection.RemoteIpAddress;
        if (remoteIp is not null && !System.Net.IPAddress.IsLoopback(remoteIp))
        {
            return Results.StatusCode(403);
        }
        return await next(ctx);
    });

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
