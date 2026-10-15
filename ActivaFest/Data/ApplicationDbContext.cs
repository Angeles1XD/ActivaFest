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

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Evento>(e =>
            {
                e.HasIndex(x => x.Estado);

                e.HasOne(x => x.Organizador)
                    .WithMany()
                    .HasForeignKey(x => x.OrganizadorId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
