using System.Text.Json.Serialization;
using HhAutoApplier.Api.Auth;
using HhAutoApplier.Application;
using HhAutoApplier.Infrastructure;
using HhAutoApplier.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAuth();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
// CORS по умолчанию выключен: расширение ходит из service worker с host_permissions (Chrome CORS не проверяет),
// страницы backend — с того же адреса. Чужой источник — только явно: Cors:Origins (CORS_ORIGINS через запятую), например chrome-extension://<id>
var corsOrigins = (builder.Configuration["Cors:Origins"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

await app.Services.InitializeDatabaseAsync();

app.UseCors();
// Страница ручной проверки (wwwroot) — без входа: данных в ней нет, вход она спрашивает сама
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// Без входа: проверка живости и документация API
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference().AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;