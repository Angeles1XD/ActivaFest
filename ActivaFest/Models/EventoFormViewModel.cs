using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    public class TipoEntradaInput
    {
        [Required(ErrorMessage = "Indica el nombre de la zona.")]
        [StringLength(60)]
        public string Nombre { get; set; } = "General";

        [Range(0, 100000, ErrorMessage = "El precio debe estar entre 0 y 100 000.")]
        public decimal Precio { get; set; }

        [Range(1, 1000000, ErrorMessage = "La capacidad debe ser al menos 1.")]
        public int Capacidad { get; set; } = 100;
    }

    // =============================
    // 📝 FORMULARIO DE PUBLICACIÓN DE EVENTO
    // =============================
    public class EventoFormViewModel : IValidatableObject
    {
        public const int MinFotos = 3;
        public const int MaxFotos = 10;
        public const int MaxTiposEntrada = 10;

        [Required(ErrorMessage = "El título es obligatorio.")]
        [StringLength(150)]
        [Display(Name = "Título")]
        public string Titulo { get; set; } = "";

        [Required(ErrorMessage = "Describe el evento.")]
        [StringLength(4000, MinimumLength = 20, ErrorMessage = "La descripción debe tener entre 20 y 4000 caracteres.")]
        [Display(Name = "Descripción")]
        public string Descripcion { get; set; } = "";

        [Required(ErrorMessage = "Selecciona una categoría.")]
        [Display(Name = "Categoría")]
        public int? CategoriaId { get; set; }

        [Required(ErrorMessage = "Indica la fecha y hora.")]
        [Display(Name = "Fecha y hora")]
        public DateTime? Fecha { get; set; }

        [Required(ErrorMessage = "La dirección es obligatoria.")]
        [StringLength(200)]
        [Display(Name = "Dirección del recinto")]
        public string Ubicacion { get; set; } = "";

        [Required(ErrorMessage = "Marca la ubicación del recinto en el mapa.")]
        [Range(-90, 90)]
        public double? Latitud { get; set; }

        [Required(ErrorMessage = "Marca la ubicación del recinto en el mapa.")]
        [Range(-180, 180)]
        public double? Longitud { get; set; }

        public List<TipoEntradaInput> TiposEntrada { get; set; } = new() { new TipoEntradaInput() };

        [Display(Name = "Fotos del recinto")]
        public List<IFormFile> Fotos { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Fecha.HasValue && Fecha.Value <= DateTime.Now)
                yield return new ValidationResult("La fecha del evento debe ser futura.", new[] { nameof(Fecha) });

            if (TiposEntrada.Count == 0)
                yield return new ValidationResult("Agrega al menos un tipo de entrada.", new[] { nameof(TiposEntrada) });

            if (TiposEntrada.Count > MaxTiposEntrada)
                yield return new ValidationResult($"Máximo {MaxTiposEntrada} tipos de entrada.", new[] { nameof(TiposEntrada) });

            var nombres = TiposEntrada.Select(t => t.Nombre?.Trim().ToLowerInvariant()).ToList();
            if (nombres.Distinct().Count() != nombres.Count)
                yield return new ValidationResult("Los tipos de entrada no pueden repetir nombre.", new[] { nameof(TiposEntrada) });

            var fotos = Fotos.Where(f => f.Length > 0).ToList();
            if (fotos.Count < MinFotos)
                yield return new ValidationResult($"Sube al menos {MinFotos} fotos del recinto.", new[] { nameof(Fotos) });

            if (fotos.Count > MaxFotos)
                yield return new ValidationResult($"Puedes subir como máximo {MaxFotos} fotos.", new[] { nameof(Fotos) });
        }
    }
}
