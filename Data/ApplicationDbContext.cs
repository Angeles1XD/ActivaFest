using Microsoft.EntityFrameworkCore;
using ActivaFest.Models;

namespace ActivaFest.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Evento> Eventos { get; set; }
    }
}