namespace ActivaFest.Models
{
    public class Categoria
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = "";
        public ICollection<Evento>? Eventos { get; set; }
    }
}