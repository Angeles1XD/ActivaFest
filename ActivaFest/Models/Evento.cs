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

        [StringLength(4000)]
        [Display(Name = "Descripción")]
        public string Descripcion { get; set; } = "";

        [Display(Name = "Categoría")]
        public int CategoriaId { get; set; }

        public Categoria? Categoria { get; set; }

        [Required(ErrorMessage = "La ubicación es obligatoria.")]
        [StringLength(200)]
        [Display(Name = "Ubicación")]
        public string Ubicacion { get; set; } = "";

        // =============================
        // 🗺️ GEOLOCALIZACIÓN (HU04)
        // =============================
        public double? Latitud { get; set; }

        public double? Longitud { get; set; }

        [Display(Name = "Fecha")]
        public DateTime Fecha { get; set; }

        // Aforo total = suma de la capacidad de los tipos de entrada
        [Range(0, int.MaxValue)]
        [Display(Name = "Aforo")]
        public int Aforo { get; set; }

        public ICollection<TipoEntrada> TiposEntrada { get; set; } = new List<TipoEntrada>();

        public ICollection<EventoImagen> Imagenes { get; set; } = new List<EventoImagen>();

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

        public bool TieneUbicacion => Latitud.HasValue && Longitud.HasValue;

        // =============================
        // ⏱️ EVENTOS PASADOS
        // =============================
        // Las entradas sirven para ingresar hasta unas horas después del inicio (eventos largos, ferias, rezagados)
        public const int HorasValidezEntradas = 12;

        public DateTime FinValidezEntradas => Fecha.AddHours(HorasValidezEntradas);

        public bool YaOcurrio(DateTime ahora) => Fecha <= ahora;
    }
}
