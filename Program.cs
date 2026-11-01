using System.Reflection;
using BookingService.Clients;
using BookingService.Data;
using BookingService.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var isOpenApiGeneration =
    Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var authority = builder.Configuration["Authentication:Authority"]
    ?? (isOpenApiGeneration
        ? "http://localhost:8080/realms/cinema"
        : throw new InvalidOperationException(
            "Authentication:Authority is required."));

var audience = builder.Configuration["Authentication:Audience"]
    ?? (isOpenApiGeneration
        ? "cinema-api"
        : throw new InvalidOperationException(
            "Authentication:Audience is required."));

builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();

        document.Components.SecuritySchemes =
            new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>
            {
                ["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
                {
                    Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                    Scheme = "bearer",
                    In = Microsoft.OpenApi.ParameterLocation.Header,
                    BearerFormat = "JWT",
                    Description = "Paste a Keycloak access token."
                }
            };

        foreach (var (path, pathItem) in document.Paths)
        {
            // Readiness is public; booking operations require a token.
            if (path == "/health/ready")
            {
                continue;
            }

            foreach (var operation in pathItem.Operations)
            {
                operation.Value.Security ??= [];
                operation.Value.Security.Add(
                    new Microsoft.OpenApi.OpenApiSecurityRequirement
                    {
                        [new Microsoft.OpenApi.OpenApiSecuritySchemeReference(
                            "Bearer",
                            document)] = []
                    });
            }
        }

        return Task.CompletedTask;
    });
});

builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<PaymentCompletedConsumer>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();

        // Keep JWT claim names such as "email" unchanged.
        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization(options =>
{
    // Require a valid access token for endpoints by default.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var showtimeBaseUrl = builder.Configuration["ShowtimeService:BaseUrl"]
    ?? throw new InvalidOperationException(
        "Configuration value 'ShowtimeService:BaseUrl' is required.");

if (!Uri.TryCreate(showtimeBaseUrl, UriKind.Absolute, out var showtimeBaseAddress))
{
    throw new InvalidOperationException(
        "'ShowtimeService:BaseUrl' must be an absolute URL.");
}

builder.Services.AddHttpClient<IShowtimeApiClient, ShowtimeApiClient>(client =>
{
    client.BaseAddress = showtimeBaseAddress;
});

var app = builder.Build();

if (!isOpenApiGeneration)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider
        .GetRequiredService<BookingDbContext>();

    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();