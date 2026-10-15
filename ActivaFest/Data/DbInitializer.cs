using Microsoft.AspNetCore.Identity;
using ActivaFest.Models;

namespace ActivaFest.Data
{
    // Crea los roles del sistema y el usuario administrador inicial
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            foreach (var rol in Roles.Todos)
            {
                if (!await roleManager.RoleExistsAsync(rol))
                    await roleManager.CreateAsync(new IdentityRole(rol));
            }

            var adminEmail = config["SeedAdmin:Email"] ?? "admin@activafest.com";
            var adminPassword = config["SeedAdmin:Password"] ?? "Admin123!";

            if (await userManager.FindByEmailAsync(adminEmail) != null)
                return;

            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                NombreCompleto = "Administrador ActivaFest"
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, Roles.Administrador);
                logger.LogInformation("👑 Administrador inicial creado: {Email}", adminEmail);
            }
            else
            {
                logger.LogError("No se pudo crear el administrador inicial: {Errores}",
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
