using Elaris.Api.Filters;
using Elaris.Api.Middleware;
using Elaris.Application;
using Elaris.Domain.Auth.Validators;
using Elaris.Domain.Common;
using Elaris.Infrastructure;
using Elaris.Notifications;
using Elaris.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;

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
        .WriteTo.Sink(new Elaris.Infrastructure.Logging.AuditLogSerilogSink(services));

    var seqUrl = context.Configuration["Seq:ServerUrl"]
        ?? context.Configuration["ELARIS_SEQ_URL"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        configuration.WriteTo.Seq(seqUrl);
    }
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<FluentValidationActionFilter>();
    options.Filters.Add<DiagnosticLoggingFilter>();
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
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ElAris API",
        Version = "v1",
        Description = "REST API for ElAris (member-web auth first; more features later)."
    });

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

    var xmlPath = Path.Combine(AppContext.BaseDirectory, "Elaris.Api.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

builder.Services.AddProblemDetails();
builder.Services.AddElarisApplication();
builder.Services.AddElarisNotifications(builder.Configuration);
builder.Services.AddElarisInfrastructure(builder.Configuration, options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryOtpStore = true;
    }
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("ElarisClients", policy =>
    {
        var origins = builder.Configuration.GetSection("Elaris:Cors:Origins").Get<string[]>()
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
    var db = scope.ServiceProvider.GetRequiredService<ElarisDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync(
        """
        DO $$ DECLARE
          r RECORD;
        BEGIN
          FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP
            EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' CASCADE';
          END LOOP;
        END $$;
        """);
}
else
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ElarisDbContext>();
    await db.Database.MigrateAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ElAris API v1");
        options.DocumentTitle = "ElAris API";
        options.DisplayRequestDuration();
    });
}

app.UseHttpsRedirection();
app.UseCors("ElarisClients");
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
