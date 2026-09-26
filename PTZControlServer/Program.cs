using System.Security.Cryptography;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.OpenApi.Models;
using PTZControl.Core;
using PTZControlServer;

ServerOptions serverOptions;
try
{
    serverOptions = ServerOptions.Parse(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    Console.Error.WriteLine();
    Console.Error.WriteLine(ServerOptions.HelpText);
    return 2;
}

if (serverOptions.ShowHelp)
{
    Console.WriteLine(ServerOptions.HelpText);
    return 0;
}

var builder = WebApplication.CreateBuilder(args: []);
builder.Host.UseWindowsService(options => options.ServiceName = "PTZControlServer");
builder.Host.UseSystemd();
builder.WebHost.UseUrls(serverOptions.ListenUrls.ToArray());
if (serverOptions.FileLogLevel != LogLevel.None)
    builder.Logging.AddProvider(new DailyFileLoggerProvider(serverOptions.LogDirectory, serverOptions.FileLogLevel));
builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton(CameraBackendFactory.Create());
builder.Services.AddSingleton<CameraApiService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PTZControlServer HTTP API",
        Version = "v1",
        Description = "Control PTZ cameras from Bitfocus Companion, Stream Deck, scripts, and other HTTP clients."
    });
    options.AddSecurityDefinition("PTZControlToken", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ServerOptions.TokenHeaderName,
        Description = "Required when PTZControlServer was started with --token."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "PTZControlToken" } }] = Array.Empty<string>()
    });
});

var app = builder.Build();
var requestLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PTZControlServer.Requests");

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = exception switch
    {
        ApiInputException or ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
        CameraNotFoundException => (StatusCodes.Status404NotFound, "Camera not found"),
        NotSupportedException or PlatformNotSupportedException => (StatusCodes.Status501NotImplemented, "Camera operation not supported"),
        _ => (StatusCodes.Status500InternalServerError, "Camera operation failed")
    };
    await Results.Problem(exception?.Message ?? title, statusCode: status, title: title).ExecuteAsync(context);
}));

app.Use(async (context, next) =>
{
    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    try
    {
        await next();
        requestLogger.LogInformation("{Method} {Path}{Query} -> {StatusCode} in {ElapsedMs:F1} ms from {RemoteIp}",
            context.Request.Method, context.Request.Path, context.Request.QueryString, context.Response.StatusCode,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.Connection.RemoteIpAddress);
    }
    catch (Exception exception)
    {
        requestLogger.LogError(exception, "{Method} {Path}{Query} failed after {ElapsedMs:F1} ms from {RemoteIp}",
            context.Request.Method, context.Request.Path, context.Request.QueryString,
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, context.Connection.RemoteIpAddress);
        throw;
    }
});

if (serverOptions.SwaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/action"))
    {
        await next();
        return;
    }

    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp is null || !serverOptions.IsRemoteAddressAllowed(remoteIp))
    {
        await Results.Problem("The client IP address is not allowed.", statusCode: StatusCodes.Status403Forbidden, title: "Forbidden").ExecuteAsync(context);
        return;
    }

    if (!string.IsNullOrEmpty(serverOptions.Token))
    {
        var suppliedToken = context.Request.Headers[ServerOptions.TokenHeaderName].ToString();
        if (!FixedTimeEquals(suppliedToken, serverOptions.Token))
        {
            await Results.Problem($"Provide a valid {ServerOptions.TokenHeaderName} header.", statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized").ExecuteAsync(context);
            return;
        }
    }

    context.Response.Headers.CacheControl = "no-store";
    await next();
});

app.MapGet("/", () => Results.Redirect(serverOptions.SwaggerEnabled ? "/swagger" : "/health"))
    .ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "PTZControlServer" }))
    .WithTags("Server")
    .WithSummary("Check whether the HTTP server is running.");

var api = app.MapGroup("/api");
api.MapGet("/devices", (CameraApiService service) => service.GetDevices())
    .WithTags("Information")
    .WithSummary("List camera slots, device names, friendly names, and device paths.");
api.MapGet("/camera/{slot:int}/info", (int slot, CameraApiService service) => service.GetCameraInfo(slot))
    .WithTags("Information")
    .WithSummary("Show camera ranges, current values, and preset names.");
api.MapGet("/camera/info", (int slot, CameraApiService service) => service.GetCameraInfo(slot))
    .WithTags("Information")
    .WithSummary("Show camera information using a slot query parameter.");

EndpointMappings.MapRestActions(api);
EndpointMappings.MapConvenienceActions(app.MapGroup("/action"));

Console.WriteLine("PTZControlServer");
foreach (var url in serverOptions.ListenUrls)
    Console.WriteLine($"Listening on {url}");
Console.WriteLine(serverOptions.SwaggerEnabled ? "Swagger UI is available at /swagger" : "Swagger UI is disabled.");
Console.WriteLine(serverOptions.AllowedIps.Count == 0 ? "IP allowlist: not configured" : $"IP allowlist: {string.Join(", ", serverOptions.AllowedIps.Select(rule => rule.Source))}");
Console.WriteLine(string.IsNullOrEmpty(serverOptions.Token) ? "Token authentication: disabled" : $"Token authentication: enabled ({ServerOptions.TokenHeaderName})");
Console.WriteLine(serverOptions.FileLogLevel == LogLevel.None
    ? "File logging: disabled"
    : $"File logging: {serverOptions.FileLogLevel} ({serverOptions.LogDirectory})");

await app.RunAsync();
return 0;

static bool FixedTimeEquals(string supplied, string expected)
{
    var suppliedBytes = System.Text.Encoding.UTF8.GetBytes(supplied);
    var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
    return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
}
