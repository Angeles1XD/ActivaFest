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
        public DbSet<Orden> Ordenes { get; set; }
        public DbSet<OrdenItem> OrdenItems { get; set; }
        public DbSet<Entrada> Entradas { get; set; }
        public DbSet<PaqueteDestacado> PaquetesDestacado { get; set; }
        public DbSet<ContratoDestacado> ContratosDestacado { get; set; }
        public DbSet<Liquidacion> Liquidaciones { get; set; }

        // Paquetes publicitarios iniciales (el administrador puede editarlos)
        public static readonly PaqueteDestacado[] PaquetesIniciales =
        {
            new() { Id = 1, Nombre = "Impulso", Descripcion = "Tu evento en la sección Destacados de la portada durante 3 días.", Dias = 3, Precio = 29.90m, Prioridad = 1 },
            new() { Id = 2, Nombre = "Plus", Descripcion = "Una semana en Destacados, por delante de los paquetes Impulso.", Dias = 7, Precio = 59.90m, Prioridad = 2 },
            new() { Id = 3, Nombre = "Premium", Descripcion = "Dos semanas en los primeros lugares de Destacados.", Dias = 15, Precio = 109.90m, Prioridad = 3 }
        };

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

            // =============================
            // 🧾 COMPRAS Y ENTRADAS (SPRINT 3)
            // =============================
            builder.Entity<Orden>(o =>
            {
                o.HasIndex(x => x.Codigo).IsUnique();
                o.HasIndex(x => new { x.Estado, x.FechaExpiracion });
                o.HasIndex(x => x.UsuarioId);
                o.Property(x => x.Total).HasPrecision(10, 2);

                o.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
                // Un evento con ventas no se puede borrar en cascada
                o.HasOne(x => x.Evento).WithMany().HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<OrdenItem>(i =>
            {
                i.Property(x => x.PrecioUnitario).HasPrecision(10, 2);
                i.HasOne(x => x.Orden).WithMany(o => o.Items).HasForeignKey(x => x.OrdenId).OnDelete(DeleteBehavior.Cascade);
                i.HasOne(x => x.TipoEntrada).WithMany().HasForeignKey(x => x.TipoEntradaId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Entrada>(e =>
            {
                e.HasIndex(x => x.Codigo).IsUnique();
                e.HasIndex(x => x.UsuarioId);
                e.HasOne(x => x.Orden).WithMany(o => o.Entradas).HasForeignKey(x => x.OrdenId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Evento).WithMany().HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne(x => x.TipoEntrada).WithMany().HasForeignKey(x => x.TipoEntradaId).OnDelete(DeleteBehavior.Restrict);
            });

            // =============================
            // ⭐ PUBLICIDAD Y 💸 LIQUIDACIONES (SPRINT 5)
            // =============================
            builder.Entity<Orden>(o =>
            {
                o.Property(x => x.Comision).HasPrecision(10, 2);
                o.Property(x => x.PorcentajeComision).HasPrecision(5, 2);
            });

            builder.Entity<PaqueteDestacado>(p =>
            {
                p.Property(x => x.Precio).HasPrecision(10, 2);
                p.HasData(PaquetesIniciales);
            });

            builder.Entity<ContratoDestacado>(c =>
            {
                c.HasIndex(x => x.Codigo).IsUnique();
                c.HasIndex(x => new { x.Estado, x.FechaFin });
                c.Property(x => x.Precio).HasPrecision(10, 2);
                c.HasOne(x => x.Evento).WithMany().HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Restrict);
                c.HasOne(x => x.Paquete).WithMany().HasForeignKey(x => x.PaqueteId).OnDelete(DeleteBehavior.Restrict);
                c.HasOne(x => x.Organizador).WithMany().HasForeignKey(x => x.OrganizadorId).OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Liquidacion>(l =>
            {
                l.HasIndex(x => new { x.OrganizadorId, x.EventoId });
                l.Property(x => x.Monto).HasPrecision(10, 2);
                l.HasOne(x => x.Organizador).WithMany().HasForeignKey(x => x.OrganizadorId).OnDelete(DeleteBehavior.Restrict);
                l.HasOne(x => x.Evento).WithMany().HasForeignKey(x => x.EventoId).OnDelete(DeleteBehavior.Restrict);
            });

            // SQLite no ordena ni agrega columnas decimal: en ese proveedor todos los montos se guardan como REAL
            if (Database.IsSqlite())
            {
                foreach (var propiedad in builder.Model.GetEntityTypes()
                             .SelectMany(t => t.GetProperties())
                             .Where(p => p.ClrType == typeof(decimal)))
                {
                    propiedad.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<decimal, double>(
                        v => (double)v, v => (decimal)v));
                }
            }
        }
    }
}
