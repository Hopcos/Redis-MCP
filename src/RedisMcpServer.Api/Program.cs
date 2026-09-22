using Microsoft.OpenApi;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using RedisMcpServer.Api.Api;
using RedisMcpServer.Api.Mcp;
using RedisMcpServer.Core.Configuration;
using RedisMcpServer.Core.Environment;
using RedisMcpServer.Core.Redis;
using RedisMcpServer.Core.Tools;
using RedisMcpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Configuration (appsettings "Redis" array + environment variable overrides)
// ---------------------------------------------------------------------------
builder.Services.Configure<RedisOptions>(builder.Configuration);

builder.Services.AddSingleton<IRedisEnvironmentResolver>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>().Value;
    return new RedisEnvironmentResolver(options.Redis);
});
builder.Services.AddSingleton<IRedisConnectionProvider, RedisConnectionProvider>();
builder.Services.AddSingleton<RedisMcpServer.Core.Browse.IRedisBrowseService, RedisMcpServer.Core.Browse.RedisBrowseService>();

// ---------------------------------------------------------------------------
// 2. Tool discovery + registry + invoker (single execution path for MCP and REST)
// ---------------------------------------------------------------------------
var toolMethods = ToolDiscovery.Scan(typeof(EnvironmentTools).Assembly);
foreach (var classType in toolMethods.Select(m => m.ClassType).Distinct())
{
    builder.Services.AddSingleton(classType);
}

builder.Services.AddSingleton<IToolRegistry>(sp =>
{
    var tools = ToolDiscovery.Build(toolMethods, type => sp.GetRequiredService(type));
    return new ToolRegistry(tools);
});
builder.Services.AddSingleton<IToolInvoker, ToolInvoker>();

// ---------------------------------------------------------------------------
// 3. MCP gateway (tools/list + tools/call handlers, instructions, descriptions)
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<RedisMcpGateway>();

// ---------------------------------------------------------------------------
// 4. MCP server over HTTP transport (streamable HTTP + legacy SSE on /mcp)
//    Handlers are wired per session through ConfigureSessionOptions so they can
//    resolve the gateway (and hence Redis services) from the request services.
// ---------------------------------------------------------------------------
builder.Services
    .AddMcpServer()
    .WithHttpTransport(transport =>
    {
        // Legacy SSE clients reuse the session between GET /mcp and POST /mcp, which requires
        // in-memory session state; streamable-HTTP clients also keep working over the same endpoint.
        transport.SessionMode = ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateful;
        // Legacy SSE (2024-11-05 transport) is intentionally enabled for older MCP clients in
        // addition to streamable HTTP; the server is read-only and meant for trusted clients.
#pragma warning disable MCP9004
        transport.EnableLegacySse = true;
#pragma warning restore MCP9004
        transport.ConfigureSessionOptions = (httpContext, options, _) =>
        {
            var gateway = httpContext.RequestServices.GetRequiredService<RedisMcpGateway>();
            options.ServerInfo = gateway.ServerInfo;
            options.ServerInstructions = gateway.Instructions;
            options.Handlers.ListToolsHandler = (request, ct) => new ValueTask<ListToolsResult>(gateway.ListTools());
            options.Handlers.CallToolHandler = (request, ct) => gateway.CallToolAsync(request, ct);
            return Task.CompletedTask;
        };
    });

// ---------------------------------------------------------------------------
// 5. REST API + Swagger UI
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(swagger =>
{
    swagger.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Redis MCP Server REST API",
        Version = "v1",
        Description = "Read-only Redis query tools: the same capabilities exposed over MCP (HTTP transport) "
            + "and usable directly by front-end applications. Invocation results mirror the MCP tools/call "
            + "response: { isError, data, text, durationMs }.",
    });
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// 6. Middleware & endpoints
// ---------------------------------------------------------------------------
// Swagger UI is always available (any environment, e.g. also behind a TLS proxy in Production).
app.UseSwagger();
app.UseSwaggerUI(ui => ui.SwaggerEndpoint("/swagger/v1/swagger.json", "Redis MCP Server REST API v1"));

app.MapToolApi();
app.MapBrowseApi();

// MCP over HTTP: streamable HTTP (POST /mcp) + legacy SSE (GET /mcp) for compatible clients.
app.MapMcp("/mcp");

app.MapGet("/", () => Results.Redirect("/swagger"));

await app.RunAsync();
