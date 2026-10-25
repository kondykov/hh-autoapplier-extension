using System.Text.Json.Serialization;
using HhAutoApplier.Api.Middlewares;
using HhAutoApplier.Application;
using HhAutoApplier.Infrastructure;
using HhAutoApplier.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseCors();
// Страница ручной проверки (wwwroot) — без ключа: данных в ней нет, ключ она спрашивает сама и шлёт в X-Api-Key
app.UseStaticFiles();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.Run();

public partial class Program;
