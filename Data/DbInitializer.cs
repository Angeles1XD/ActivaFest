using ActivaFest.Models;

namespace ActivaFest.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            context.Database.EnsureCreated();

            if (!context.Categorias.Any())
            {
                context.Categorias.AddRange(
                    new Categoria { Nombre = "Conciertos" },
                    new Categoria { Nombre = "Deportes" }
                );

                context.SaveChanges();
            }
        }
    }
}