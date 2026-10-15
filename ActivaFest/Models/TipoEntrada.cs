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

        // =============================
        // 📊 CONTROL DE AFORO (HU11)
        // =============================
        // Se modifican solo con UPDATE condicionales atómicos (ver ReservaService), nunca leyendo y escribiendo.
        public int Vendidas { get; set; }

        // Entradas apartadas por órdenes pendientes de pago
        public int Reservadas { get; set; }

        public int Disponibles => Math.Max(0, Capacidad - Vendidas - Reservadas);
    }
}
