using AqlamAcademy.Api.Infrastructure;
using AqlamAcademy.Api.Infrastructure.DependencyInjection;
using AqlamAcademy.Api.Infrastructure.Extensions;
using AqlamAcademy.Api.Infrastructure.Middlewares;
using AqlamAcademy.Application;
using AqlamAcademy.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("Logs/aqlam-events-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add services to the container
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Aqlam Academy API";
        document.Info.Version = "v1";
        document.Info.Description = "The backend API for the Aqlam Academy SaaS platform.";
        return Task.CompletedTask;
    });
});

// 3. Add Layers (Core & Infrastructure & Presentation)
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddPresentation(builder.Configuration);
// Enums accepted as names ("School") or numbers; returned as names.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// 4. Global Error Handling
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Behind a hosting proxy (Render): client IP for rate limits and login records, HTTPS for secure cookies.
// The proxy's addresses are not known in advance, so it is trusted for the configured number of hops.
var forwardedHeaders = builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled");
if (forwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = builder.Configuration.GetValue("ForwardedHeaders:ForwardLimit", 1);
        // Render's proxy can drop X-Forwarded-For on rewrites; its Cloudflare edge sends the visitor in CF-Connecting-IP.
        if (builder.Configuration["ForwardedHeaders:ForwardedForHeaderName"] is { Length: > 0 } header)
            options.ForwardedForHeaderName = header;
    });
}

var app = builder.Build();

if (forwardedHeaders)
    app.UseForwardedHeaders();

// 5. Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.WithTitle("Aqlam Academy API")
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

// Correlation id and exception handling run first so every later log line
// (including auth failures) carries the correlation id and errors are shaped consistently.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UsePresentation();
app.MapControllers();


try
{
    Log.Information("Applying migrations and seeding database...");
    await app.InitialiseDatabaseAsync();

    Log.Information("Starting Aqlam Academy API...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "The application failed to start correctly.");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed so integration tests can host the API with WebApplicationFactory<Program>.
public partial class Program;
