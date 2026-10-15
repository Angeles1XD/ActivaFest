using System;
using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    public enum EstadoEvento
    {
        Pendiente = 0,
        Aprobado = 1,
        Rechazado = 2
    }

    public class Evento
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "El título es obligatorio.")]
        [StringLength(150)]
        [Display(Name = "Título")]
        public string Titulo { get; set; } = "";

        [Required(ErrorMessage = "La categoría es obligatoria.")]
        [StringLength(80)]
        [Display(Name = "Categoría")]
        public string Categoria { get; set; } = "";

        [Required(ErrorMessage = "La ubicación es obligatoria.")]
        [StringLength(200)]
        [Display(Name = "Ubicación")]
        public string Ubicacion { get; set; } = "";

        [Display(Name = "Fecha")]
        public DateTime Fecha { get; set; }

        [Range(0, int.MaxValue)]
        [Display(Name = "Aforo / asistentes")]
        public int Asistentes { get; set; }

        // =============================
        // 🛡️ MODERACIÓN (HU10)
        // =============================
        public EstadoEvento Estado { get; set; } = EstadoEvento.Pendiente;

        [StringLength(500)]
        public string? MotivoRechazo { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

        public DateTime? FechaModeracion { get; set; }

        // Organizador que publicó el evento
        public string? OrganizadorId { get; set; }

        public ApplicationUser? Organizador { get; set; }
    }
}
