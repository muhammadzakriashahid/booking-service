using System.Reflection;
using BookingService.Data;
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