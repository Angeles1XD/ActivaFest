using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    public class Evento
    {
        public int Id { get; set; }

        [Required]
        public string? Titulo { get; set; }

        public string? Descripcion { get; set; }

        [Required]
        public DateTime Fecha { get; set; }

        public string? Ubicacion { get; set; }

        // 🔥 ESTE REEMPLAZA "Asistentes"
        public int Asistentes { get; set; }

        // 🔥 OPCIONAL (puedes usarlo o no)
        public string? Categoria { get; set; }
    }
}