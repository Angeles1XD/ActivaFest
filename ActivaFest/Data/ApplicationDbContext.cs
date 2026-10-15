using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Models;

namespace ActivaFest.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Evento> Eventos { get; set; }
        public DbSet<Categoria> Categorias { get; set; }
        public DbSet<TipoEntrada> TiposEntrada { get; set; }
        public DbSet<EventoImagen> EventoImagenes { get; set; }

        // Categorías iniciales del catálogo (RF02)
        public static readonly Categoria[] CategoriasIniciales =
        {
            new() { Id = 1, Nombre = "Conciertos", Icono = "🎵", Color = "#a855f7", Imagen = "/img/categorias/conciertos.jpg" },
            new() { Id = 2, Nombre = "Teatro", Icono = "🎭", Color = "#ec4899", Imagen = "/img/categorias/teatro.jpg" },
            new() { Id = 3, Nombre = "Festivales", Icono = "🎉", Color = "#f97316", Imagen = "/img/categorias/festivales.jpg" },
            new() { Id = 4, Nombre = "Ferias", Icono = "🎪", Color = "#f59e0b", Imagen = "/img/categorias/ferias.jpg" },
            new() { Id = 5, Nombre = "Exposiciones", Icono = "🖼️", Color = "#06b6d4", Imagen = "/img/categorias/exposiciones.jpg" },
            new() { Id = 6, Nombre = "Deportes", Icono = "⚽", Color = "#22c55e", Imagen = "/img/categorias/deportes.jpg" },
            new() { Id = 7, Nombre = "Gastronomía", Icono = "🍽️", Color = "#ef4444", Imagen = "/img/categorias/gastronomia.jpg" },
            new() { Id = 8, Nombre = "Familiar", Icono = "👨‍👩‍👧", Color = "#14b8a6", Imagen = "/img/categorias/familiar.jpg" },
            new() { Id = 9, Nombre = "Cultura", Icono = "🏛️", Color = "#3b82f6", Imagen = "/img/categorias/cultura.jpg" },
            new() { Id = 10, Nombre = "Otros", Icono = "🎫", Color = "#8b5cf6", Imagen = "/img/categorias/otros.jpg" }
        };

        public const int CategoriaOtrosId = 10;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Categoria>(c =>
            {
                c.HasIndex(x => x.Nombre).IsUnique();
                c.HasData(CategoriasIniciales);
            });

            builder.Entity<Evento>(e =>
            {
                e.HasIndex(x => x.Estado);
                e.HasIndex(x => x.Fecha);

                e.HasOne(x => x.Organizador)
                    .WithMany()
                    .HasForeignKey(x => x.OrganizadorId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(x => x.Categoria)
                    .WithMany(c => c.Eventos)
                    .HasForeignKey(x => x.CategoriaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<TipoEntrada>(t =>
            {
                t.Property(x => x.Precio).HasPrecision(10, 2);

                // SQLite no ordena ni agrega columnas decimal: se guarda como REAL solo en ese proveedor
                if (Database.IsSqlite())
                    t.Property(x => x.Precio).HasConversion<double>();

                t.HasOne(x => x.Evento)
                    .WithMany(e => e.TiposEntrada)
                    .HasForeignKey(x => x.EventoId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<EventoImagen>(i =>
            {
                i.HasOne(x => x.Evento)
                    .WithMany(e => e.Imagenes)
                    .HasForeignKey(x => x.EventoId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
