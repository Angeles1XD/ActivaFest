using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    // Zona o tipo de entrada del evento (General, VIP, etc.). La venta es por zona, sin asientos numerados.
    public class TipoEntrada
    {
        public int Id { get; set; }

        public int EventoId { get; set; }

        public Evento? Evento { get; set; }

        [Required]
        [StringLength(60)]
        public string Nombre { get; set; } = "General";

        [Range(0, 100000)]
        public decimal Precio { get; set; }

        [Range(1, 1000000)]
        public int Capacidad { get; set; }
    }
}
