using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.StackExchangeRedis;

var builder = WebApplication.CreateBuilder(args);

// =============================
// 🔥 BASE DE DATOS SQLITE
// =============================
var connectionString = "Data Source=app.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// =============================
// 🔥 MVC
// =============================
builder.Services.AddControllersWithViews();

// =============================
// 🔥 CACHE EN MEMORIA (para tu Index)
// =============================
builder.Services.AddMemoryCache();

// =============================
// 🔥 SESIONES (necesitan cache distribuido)
// =============================
builder.Services.AddDistributedMemoryCache(); // 👈 IMPORTANTE
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20); // tiempo de sesión
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddHttpContextAccessor();

// =============================
// 🔥 SERVICIOS DE IA
// =============================
builder.Services.AddScoped<ActivaFest.Services.AgentService>();

builder.Services.AddSingleton<MachineLearningService>();

// =============================
// 🔥 REDIS (opcional, no romper en local)
// =============================
// Si NO tienes Redis local, deja esto comentado por ahora.
// Lo activamos luego para Render.
var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT");
var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");

if (!string.IsNullOrEmpty(redisHost))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = $"{redisHost}:{redisPort},password={redisPassword}";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache(); // local
}





var app = builder.Build();

// Migraciones BD
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// =============================
// 🔥 PIPELINE
// =============================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 🔥 SESIÓN (antes de endpoints)
app.UseSession();

app.UseAuthorization();

// =============================
// 🔥 RUTA PRINCIPAL
// =============================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Eventos}/{action=Index}/{id?}");

// =============================
// 🔥 ENDPOINT DEL AGENTE IA
// =============================
app.MapPost("/api/chat", async (ActivaFest.Services.ChatRequest request, ActivaFest.Services.AgentService agentService) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest("El mensaje no puede estar vacío.");

    var response = await agentService.ProcessChatAsync(request.Message);
    return Results.Ok(new { Reply = response });
});

app.Run();