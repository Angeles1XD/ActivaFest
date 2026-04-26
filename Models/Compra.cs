using System;
using System.ComponentModel.DataAnnotations;

namespace ActivaFest.Models
{
    public class Compra
    {
        public int Id { get; set; }

        public int EventoId { get; set; }
        public Evento? Evento { get; set; }

        public int Cantidad { get; set; }

        public decimal Total { get; set; }

        public DateTime FechaCompra { get; set; }

        public string? UsuarioId { get; set; }
    }
}