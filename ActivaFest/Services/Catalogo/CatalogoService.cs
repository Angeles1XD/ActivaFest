using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ActivaFest.Data;
using ActivaFest.Models;

namespace ActivaFest.Services.Catalogo;

public interface ICatalogoService
{
    Task<ResultadoCatalogo> BuscarAsync(CatalogoFiltro filtro, CancellationToken ct = default);

    Task<List<Categoria>> ObtenerCategoriasAsync(CancellationToken ct = default);

    // Eventos próximos publicados por categoría (CategoriaId -> cantidad)
    Task<Dictionary<int, int>> ContarPorCategoriaAsync(CancellationToken ct = default);

    // Se llama cuando cambia la oferta publicada (aprobación, rechazo, edición)
    Task InvalidarAsync(CancellationToken ct = default);
}

// =============================
// 📚 CATÁLOGO DE EVENTOS (HU02 + HU05)
// =============================
// Consulta paginada y filtrada con EF Core. Los resultados se guardan en caché (Redis o memoria)
// bajo una "versión" del catálogo: al invalidar se cambia la versión y todas las páginas caducan a la vez.
public class CatalogoService : ICatalogoService
{
    private const string ClaveVersion = "catalogo:version";
    private const string ClaveCategorias = "catalogo:categorias";

    private static readonly DistributedCacheEntryOptions OpcionesCache = new DistributedCacheEntryOptions()
        .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

    private readonly ApplicationDbContext _context;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CatalogoService> _logger;

    public CatalogoService(ApplicationDbContext context, IDistributedCache cache, ILogger<CatalogoService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ResultadoCatalogo> BuscarAsync(CatalogoFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();

        var version = await _cache.GetStringAsync(ClaveVersion, ct) ?? "0";
        var clave = $"catalogo:{version}:{filtro.ClaveCache()}";

        var enCache = await _cache.GetStringAsync(clave, ct);
        if (enCache != null)
        {
            _logger.LogDebug("🔥 CACHE HIT {Clave}", clave);
            return JsonSerializer.Deserialize<ResultadoCatalogo>(enCache)!;
        }

        _logger.LogDebug("💾 CACHE MISS {Clave}", clave);
        var resultado = await ConsultarAsync(filtro, ct);

        await _cache.SetStringAsync(clave, JsonSerializer.Serialize(resultado), OpcionesCache, ct);
        return resultado;
    }

    public async Task<Dictionary<int, int>> ContarPorCategoriaAsync(CancellationToken ct = default)
    {
        var version = await _cache.GetStringAsync(ClaveVersion, ct) ?? "0";
        var clave = $"catalogo:{version}:conteo-categorias";

        var enCache = await _cache.GetStringAsync(clave, ct);
        if (enCache != null)
            return JsonSerializer.Deserialize<Dictionary<int, int>>(enCache)!;

        var conteo = await EventosPublicados(DateTime.Today)
            .GroupBy(e => e.CategoriaId)
            .Select(g => new { g.Key, Cantidad = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct);

        await _cache.SetStringAsync(clave, JsonSerializer.Serialize(conteo), OpcionesCache, ct);
        return conteo;
    }

    // Eventos aprobados desde una fecha: la base de todas las consultas públicas
    private IQueryable<Evento> EventosPublicados(DateTime desde) =>
        _context.Eventos
            .AsNoTracking()
            .Where(e => e.Estado == EstadoEvento.Aprobado && e.Fecha >= desde);

    private async Task<ResultadoCatalogo> ConsultarAsync(CatalogoFiltro filtro, CancellationToken ct)
    {
        // Por defecto solo eventos próximos
        var query = EventosPublicados(filtro.Desde?.Date ?? DateTime.Today);

        if (filtro.Hasta.HasValue)
        {
            var hastaExclusivo = filtro.Hasta.Value.Date.AddDays(1);
            query = query.Where(e => e.Fecha < hastaExclusivo);
        }

        if (filtro.Buscar != null)
        {
            var patron = $"%{EscaparLike(filtro.Buscar)}%";
            query = query.Where(e =>
                EF.Functions.Like(e.Titulo, patron, "\\") ||
                EF.Functions.Like(e.Ubicacion, patron, "\\") ||
                EF.Functions.Like(e.Categoria!.Nombre, patron, "\\"));
        }

        if (filtro.CategoriaId.HasValue)
            query = query.Where(e => e.CategoriaId == filtro.CategoriaId.Value);

        // Precio: el evento debe tener al menos un tipo de entrada dentro del rango pedido
        var precioMin = filtro.SoloGratis ? null : filtro.PrecioMin;
        var precioMax = filtro.SoloGratis ? 0 : filtro.PrecioMax;

        if (precioMin.HasValue || precioMax.HasValue)
        {
            query = query.Where(e => e.TiposEntrada.Any(t =>
                (precioMin == null || t.Precio >= precioMin) &&
                (precioMax == null || t.Precio <= precioMax)));
        }

        var proyeccion = query.Select(e => new EventoCardViewModel
        {
            Id = e.Id,
            Titulo = e.Titulo,
            Categoria = e.Categoria!.Nombre,
            CategoriaIcono = e.Categoria.Icono,
            CategoriaColor = e.Categoria.Color,
            Ubicacion = e.Ubicacion,
            Fecha = e.Fecha,
            PrecioDesde = e.TiposEntrada.Min(t => (decimal?)t.Precio),
            ImagenPortada = e.Imagenes.OrderBy(i => i.Orden).Select(i => i.Ruta).FirstOrDefault()
        });

        var total = await proyeccion.CountAsync(ct);
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)CatalogoFiltro.TamanoPagina));
        var pagina = Math.Min(filtro.Pagina, totalPaginas);

        List<EventoCardViewModel> eventos;

        if (filtro.Orden is OrdenCatalogo.PrecioAsc or OrdenCatalogo.PrecioDesc)
        {
            // SQLite no ordena columnas decimal en el servidor: se ordena en memoria sobre el resultado filtrado
            var todos = await proyeccion.ToListAsync(ct);
            var ordenados = filtro.Orden == OrdenCatalogo.PrecioAsc
                ? todos.OrderBy(e => e.PrecioDesde ?? decimal.MaxValue).ThenBy(e => e.Fecha)
                : todos.OrderByDescending(e => e.PrecioDesde ?? -1).ThenBy(e => e.Fecha);

            eventos = ordenados
                .Skip((pagina - 1) * CatalogoFiltro.TamanoPagina)
                .Take(CatalogoFiltro.TamanoPagina)
                .ToList();
        }
        else
        {
            var ordenada = filtro.Orden == OrdenCatalogo.Recientes
                ? proyeccion.OrderByDescending(e => e.Id)
                : proyeccion.OrderBy(e => e.Fecha).ThenBy(e => e.Id);

            eventos = await ordenada
                .Skip((pagina - 1) * CatalogoFiltro.TamanoPagina)
                .Take(CatalogoFiltro.TamanoPagina)
                .ToListAsync(ct);
        }

        return new ResultadoCatalogo
        {
            Eventos = eventos,
            Total = total,
            Pagina = pagina,
            TotalPaginas = totalPaginas
        };
    }

    public async Task<List<Categoria>> ObtenerCategoriasAsync(CancellationToken ct = default)
    {
        var enCache = await _cache.GetStringAsync(ClaveCategorias, ct);
        if (enCache != null)
            return JsonSerializer.Deserialize<List<Categoria>>(enCache)!;

        var categorias = await _context.Categorias
            .AsNoTracking()
            .OrderBy(c => c.Id == ApplicationDbContext.CategoriaOtrosId)
            .ThenBy(c => c.Nombre)
            .Select(c => new Categoria { Id = c.Id, Nombre = c.Nombre, Icono = c.Icono, Color = c.Color, Imagen = c.Imagen })
            .ToListAsync(ct);

        await _cache.SetStringAsync(ClaveCategorias, JsonSerializer.Serialize(categorias),
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromHours(1)), ct);

        return categorias;
    }

    public Task InvalidarAsync(CancellationToken ct = default) =>
        _cache.SetStringAsync(ClaveVersion, Guid.NewGuid().ToString("N"), ct);

    private static string EscaparLike(string texto) =>
        texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
