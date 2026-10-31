using System.Reflection;
using BookingService.Data;
using BookingService.Clients;
using BookingService.Messaging;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddHostedService<PaymentCompletedConsumer>();

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

var isOpenApiGeneration =
    Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

if (!isOpenApiGeneration)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider
        .GetRequiredService<BookingDbContext>();

    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.MapControllers();

app.Run();