using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    public class Categoria
    {
        public int Id { get; set; }

        [Required]
        public string Nombre { get; set; }

        public ICollection<Evento>? Eventos { get; set; }
    }
}
