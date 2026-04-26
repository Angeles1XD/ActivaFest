using System;

namespace ActivaFest.Models
{
    public class Evento
    {
        public int Id { get; set; }

        public string Titulo { get; set; } = "";

        public string Categoria { get; set; } = "";

        public string Ubicacion { get; set; } = "";

        public DateTime Fecha { get; set; }

        public int Asistentes { get; set; }
    }
}