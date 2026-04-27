using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using Microsoft.Extensions.Caching.Memory;

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
// 🔥 REDIS (opcional, no romper en local)
// =============================
// Si NO tienes Redis local, deja esto comentado por ahora.
// Lo activamos luego para Render.
/*
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});
*/

var app = builder.Build();

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

app.Run();