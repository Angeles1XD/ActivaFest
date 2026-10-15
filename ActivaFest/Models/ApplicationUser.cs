using Microsoft.AspNetCore.Identity;

namespace ActivaFest.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string NombreCompleto { get; set; } = "";

        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    }
}
