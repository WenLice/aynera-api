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
    options.DocInclusionPredicate((docName, api) => ApiAudience.Of(api) == docName);

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "JWT access token. Example: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });

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
builder.Services.AddAyneraInfrastructure(builder.Configuration, options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryOtpStore = builder.Configuration.GetValue("Aynera:UseInMemoryOtpStore", true);
    }
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
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
