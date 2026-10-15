using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services;
using ActivaFest.Services.Catalogo;
using ActivaFest.Services.Email;
using ActivaFest.Services.Imagenes;

var builder = WebApplication.CreateBuilder(args);

// =============================
// 🔥 BASE DE DATOS SQLITE
// =============================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=app.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// =============================
// 🔐 IDENTITY: USUARIOS Y ROLES (HU01)
// =============================
// Las contraseñas se guardan con hash PBKDF2 (PasswordHasher de ASP.NET Core Identity)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = true;
        options.Password.RequireDigit = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
    options.AccessDeniedPath = "/Auth/AccesoDenegado";
    options.Cookie.Name = "ActivaFest.Auth";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// =============================
// ✉️ CORREO (HU15)
// =============================
builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// =============================
// 📚 CATÁLOGO E IMÁGENES (HU02, HU03, HU05)
// =============================
builder.Services.AddScoped<ICatalogoService, CatalogoService>();
builder.Services.AddSingleton<IAlmacenamientoImagenes, AlmacenamientoLocalImagenes>();

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

// Migraciones BD + roles y administrador inicial
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();

    await DbInitializer.SeedAsync(scope.ServiceProvider, app.Configuration, app.Logger);
}

// =============================
// 🔥 PIPELINE
// =============================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Fechas en español y montos en soles (es-PE)
var culturaPeru = new CultureInfo("es-PE");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culturaPeru),
    SupportedCultures = new[] { culturaPeru },
    SupportedUICultures = new[] { culturaPeru }
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 🔥 SESIÓN (antes de endpoints)
app.UseSession();

app.UseAuthentication();
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
