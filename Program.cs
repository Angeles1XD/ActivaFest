using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// =============================
// 🔌 BASE DE DATOS SQLITE
// =============================
var connectionString = "Data Source=app.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

// =============================
// 🔐 IDENTITY (LOGIN)
// =============================
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

// =============================
// 🔥 MVC
// =============================
builder.Services.AddControllersWithViews();

// =============================
// 🔥 CACHE EN MEMORIA
// =============================
builder.Services.AddMemoryCache();

// =============================
// 🔥 SESIONES
// =============================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

// =============================
// 🔥 REDIS (LO ACTIVAMOS LUEGO)
// =============================
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

// 🔐 LOGIN
app.UseAuthentication();
app.UseAuthorization();

// 🔥 SESIÓN
app.UseSession();

// =============================
// 🔥 RUTA PRINCIPAL
// =============================
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Eventos}/{action=Index}/{id?}");

app.Run();