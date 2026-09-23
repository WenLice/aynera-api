using Aynera.Api.Filters;
using Aynera.Api.Middleware;
using Aynera.Api.OpenApi;
using Aynera.Application;
using Aynera.Domain.Auth.Validators;
using Aynera.Domain.Common;
using Aynera.Infrastructure;
using Aynera.Notifications;
using Aynera.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json.Serialization;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Sink(new Aynera.Infrastructure.Logging.AuditLogSerilogSink(services));

    var seqUrl = context.Configuration["Seq:ServerUrl"]
        ?? context.Configuration["AYNERA_SEQ_URL"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        configuration.WriteTo.Seq(seqUrl);
    }
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<FluentValidationActionFilter>();
    options.Filters.Add<DiagnosticLoggingFilter>();
})
.AddJsonOptions(options =>
{
    // Enums arrive by name ("Female"), which is how every response already reports them —
    // `Gender` is the only enum in a request or response DTO, and it came back as a string while
    // only accepting a number. Numbers are still accepted, so existing callers are unaffected.
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddValidatorsFromAssemblyContaining<CreateMemberRequestValidator>();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var correlationId = context.HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value)
            ? value?.ToString()
            : null;

        var errors = context.ModelState
            .Where(pair => pair.Value is { Errors.Count: > 0 })
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value!.Errors
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                    .ToArray());

        var body = ApiResponse.Fail(
            "validation_failed",
            StatusCodes.Status400BadRequest,
            errors,
            correlationId);

        return new BadRequestObjectResult(body);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(ApiAudience.All, new OpenApiInfo
    {
        Title = "Aynera API (all endpoints)",
        Version = "v1",
        Description = "Every endpoint, member and staff, in one document for browsing and manual testing. "
            + "Staff endpoints still require an admin token (aud=admin); see the Admin API document for that set alone."
    });
    options.SwaggerDoc(ApiAudience.Member, new OpenApiInfo
    {
        Title = "Aynera Member API",
        Version = "v1",
        Description = "Public and member-facing endpoints (marketing site, member app). Member tokens carry aud=member."
    });
    options.SwaggerDoc(ApiAudience.Admin, new OpenApiInfo
    {
        Title = "Aynera Admin API",
        Version = "v1",
        Description = "Staff endpoints for the admin panel. Admin tokens carry aud=admin; member tokens are refused."
    });
    options.DocInclusionPredicate(ApiAudience.IncludedIn);

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT access token. Example: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    // Per-operation rather than document-wide: a global requirement padlocks anonymous
    // endpoints too, which made the public early-access routes look like they needed a token.
    options.OperationFilter<SecurityOnAuthorizedOperations>();

    var xmlPath = Path.Combine(AppContext.BaseDirectory, "Aynera.Api.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

// OTP codes may only ever be printed by the Console providers in Development. Any other
// environment has the switch forced off before options are bound, whatever the config says.
if (!builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("Aynera:Otp:RevealCodesInLogs", false))
{
    builder.Configuration["Aynera:Otp:RevealCodesInLogs"] = "false";
    Log.Warning("Aynera:Otp:RevealCodesInLogs was set outside Development and has been forced off.");
}

builder.Services.AddProblemDetails();
builder.Services.AddAyneraApplication();
builder.Services.AddAyneraNotifications(builder.Configuration);
// Honoured in every environment, so a single-instance deployment can run without Redis at all.
// It was previously read only under Testing, which left every other environment pinned to Redis
// and falling back to localhost:6379 — a deployed instance with no Redis could not serve a single
// request. The default still differs by environment: Testing runs in memory, everything else
// expects Redis, because OTP challenges held in memory are per-process and a second instance
// could not verify a code the first issued.
builder.Services.AddAyneraInfrastructure(builder.Configuration, options =>
{
    options.UseInMemoryOtpStore = builder.Configuration.GetValue(
        "Aynera:UseInMemoryOtpStore",
        builder.Environment.IsEnvironment("Testing"));
    options.UseInMemoryMediaStorage = builder.Configuration.GetValue(
        "Aynera:UseInMemoryMediaStorage",
        builder.Environment.IsEnvironment("Testing"));
    options.UseStubLiveness = builder.Environment.IsEnvironment("Testing");
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AyneraClients", policy =>
    {
        var origins = builder.Configuration.GetSection("Aynera:Cors:Origins").Get<string[]>()
            ??
            [
                "http://localhost:3000",
                "http://localhost:5173",
                "http://localhost:5174"
            ];

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync(
        """
        DO $$ DECLARE
          r RECORD;
        BEGIN
          FOR r IN (SELECT tablename FROM pg_tables
                    WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
            EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' CASCADE';
          END LOOP;
        END $$;
        """);
}
else
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AyneraDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// The liveness page (wwwroot/liveness, built from /liveness-page). Served before authentication on
// purpose: it is a public page that holds nothing secret, and every API call it leads to is authorised.
app.UseDefaultFiles();
app.UseStaticFiles();

// Always on in Development, and elsewhere only when explicitly switched on.
//
// Deliberately its own flag rather than tied to the environment: switching a deployed
// instance to Development to get the docs would also pick up appsettings.Development.json,
// which sets Aynera:Otp:RevealCodesInLogs and would print every login code into that
// environment's log stream. This exposes the API's shape, not its data — every endpoint
// still enforces its own policy — but leave it off once you no longer need it.
if (app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Aynera:ExposeSwagger", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint($"/swagger/{ApiAudience.All}/swagger.json", "Aynera API (all endpoints)");
        options.SwaggerEndpoint($"/swagger/{ApiAudience.Member}/swagger.json", "Aynera Member API");
        options.SwaggerEndpoint($"/swagger/{ApiAudience.Admin}/swagger.json", "Aynera Admin API");
        options.DocumentTitle = "Aynera API";
        options.DisplayRequestDuration();
    });
}

app.UseHttpsRedirection();
app.UseCors("AyneraClients");
app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();
app.UseMiddleware<RequestLoggingMiddleware>();

app.MapControllers();

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
