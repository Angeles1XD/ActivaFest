using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    public class Evento
    {
        public int Id { get; set; }

        [Required]
        public string Titulo { get; set; }

        public string? Descripcion { get; set; }

        [Required]
        public DateTime Fecha { get; set; }

        [Range(1, double.MaxValue)]
        public decimal Precio { get; set; }

        public string? Ubicacion { get; set; }

        public int CuposDisponibles { get; set; }

        public int CategoriaId { get; set; }
        public Categoria? Categoria { get; set; }

        public ICollection<Compra>? Compras { get; set; }
    }
}