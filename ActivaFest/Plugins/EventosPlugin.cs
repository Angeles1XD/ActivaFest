using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using Microsoft.SemanticKernel;

namespace ActivaFest.Plugins;

public class EventosPlugin
{
    private readonly ApplicationDbContext _db;

    public EventosPlugin(ApplicationDbContext db)
    {
        _db = db;
    }

    [KernelFunction("buscar_eventos_relevantes")]
    [Description("Busca en la BD un máximo de 3 eventos filtrados por una palabra clave.")]
    public async Task<string> BuscarEventosAsync(string termino)
    {
        var query = _db.Eventos.AsQueryable();

        termino = termino?.Trim().ToLower() ?? "";
        
        if (!string.IsNullOrWhiteSpace(termino) && termino != "todos" && termino != "ninguno")
        {
            query = query.Where(e =>
                e.Titulo.ToLower().Contains(termino) ||
                e.Categoria.ToLower().Contains(termino) ||
                e.Ubicacion.ToLower().Contains(termino));
        }
        
        var eventos = await query
            .Where(e => e.Fecha >= DateTime.Now)
            .OrderBy(e => e.Fecha)
            .Take(3)
            .Select(e => new { e.Titulo, e.Categoria, e.Ubicacion, e.Fecha })
            .ToListAsync();

        return JsonSerializer.Serialize(eventos);
    }
}