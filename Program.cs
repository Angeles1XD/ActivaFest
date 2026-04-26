using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ActivaFest.Data;
using ActivaFest.Models;

var builder = WebApplication.CreateBuilder(args);

// 🔌 CONEXIÓN DB
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// 🔐 IDENTITY + ROLES
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>() // 👈 HABILITA ROLES
.AddEntityFrameworkStores<ApplicationDbContext>();

// 🎮 MVC
builder.Services.AddControllersWithViews();

// 🔴 REDIS (CACHE)
//builder.Services.AddStackExchangeRedisCache(options =>
//{
//    options.Configuration = "localhost:6379";
//});

// 🧠 SESSION
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

var app = builder.Build();


// 🔐 CREAR ROLES + 🌱 SEED DATOS
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var context = services.GetRequiredService<ApplicationDbContext>();

    // 👉 ROLES
    string[] roles = { "ADMIN", "USER" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // 👉 SEED CATEGORIAS
    if (!context.Categorias.Any())
    {
        context.Categorias.AddRange(
            new Categoria { Nombre = "Conciertos" },
            new Categoria { Nombre = "Deportes" }
        );

        context.SaveChanges();
    }
}


// ⚙️ PIPELINE
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 🔐 ORDEN CORRECTO
app.UseAuthentication();
app.UseAuthorization();

app.UseSession(); // 👈 SESSION

// 👑 SOPORTE PARA AREAS (ADMIN)
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}"
);

// 🌐 RUTA NORMAL
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.MapRazorPages();

app.Run();