using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using ActivaFest.Data;
using ActivaFest.Models;

namespace ActivaFest.Services.Analitica;

public class FiltroAnalitica
{
    // Período por fecha de pago (fechas locales, "hasta" incluye todo el día)
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public int? CategoriaId { get; set; }

    public static FiltroAnalitica MesActual(DateTime hoy) =>
        new() { Desde = new DateTime(hoy.Year, hoy.Month, 1), Hasta = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(1).AddDays(-1) };

    public FiltroAnalitica Normalizado()
    {
        var desde = Desde.Date;
        var hasta = Hasta.Date;
        if (hasta < desde) (desde, hasta) = (hasta, desde);
        if ((hasta - desde).TotalDays > 366) desde = hasta.AddDays(-366);
        return new FiltroAnalitica { Desde = desde, Hasta = hasta, CategoriaId = CategoriaId };
    }

    public string Clave => $"{Desde:yyyyMMdd}-{Hasta:yyyyMMdd}-{CategoriaId}";

    // Períodos rápidos del dashboard. Si vienen fechas, es un período personalizado.
    public static readonly (string Clave, string Nombre)[] Rangos =
    {
        ("hoy", "Hoy"), ("7d", "Últimos 7 días"), ("mes", "Este mes"), ("mespasado", "Mes pasado"),
        ("30d", "Últimos 30 días"), ("anio", "Este año"), ("personalizado", "Personalizado")
    };

    public static (FiltroAnalitica Filtro, string Rango) DeRango(string? rango, DateTime? desde, DateTime? hasta, int? categoriaId, DateTime hoy)
    {
        hoy = hoy.Date;
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        rango = Rangos.Any(r => r.Clave == rango) ? rango : desde != null || hasta != null ? "personalizado" : "mes";
        var (d, h) = rango switch
        {
            "hoy" => (hoy, hoy),
            "7d" => (hoy.AddDays(-6), hoy),
            "30d" => (hoy.AddDays(-29), hoy),
            "mespasado" => (inicioMes.AddMonths(-1), inicioMes.AddDays(-1)),
            "anio" => (new DateTime(hoy.Year, 1, 1), hoy),
            "personalizado" => (desde ?? inicioMes, hasta ?? hoy),
            _ => (inicioMes, hoy)   // este mes, hasta hoy (sin días futuros, para comparar con el período anterior)
        };
        return (new FiltroAnalitica { Desde = d, Hasta = h, CategoriaId = categoriaId }.Normalizado(), rango!);
    }
}

public record PuntoSemana(string Etiqueta, decimal Ingresos);
public record VentaCategoria(string Categoria, string? Color, int Entradas, decimal Ingresos);

// Compra reciente para el dashboard (el cliente va con su código anónimo, nunca con su nombre)
public record CompraReciente(DateTime Fecha, string Evento, string Cliente, int Entradas, decimal Total);

// =============================
// 📊 INDICADORES DEL DASHBOARD (HU21)
// =============================
public class DashboardAnalitica
{
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }
    public int? CategoriaId { get; set; }
    public DateTime CalculadoEn { get; set; }

    // Del período (por fecha de pago)
    public decimal Ingresos { get; set; }
    public int EntradasVendidas { get; set; }
    public int Compras { get; set; }
    public decimal Comisiones { get; set; }
    public decimal TicketPromedio => Compras == 0 ? 0 : Math.Round(Ingresos / Compras, 2);

    // Período anterior de igual duración (para "+12 % vs. el período anterior")
    public decimal IngresosAnterior { get; set; }
    public int EntradasAnterior { get; set; }
    public decimal TicketAnterior { get; set; }

    // De los eventos que vendieron en el período o que se realizan en él (por fecha del evento)
    public int EventosConsiderados { get; set; }
    public int EventosRealizados { get; set; }
    public int AforoEventos { get; set; }
    public int VendidasEventos { get; set; }
    public int VendidasEventosRealizados { get; set; }
    public int UsadasEventosRealizados { get; set; }
    public int ComprasEventos { get; set; }
    public int VisitasEventos { get; set; }

    public double? Ocupacion => AforoEventos == 0 ? null : Math.Round(100.0 * VendidasEventos / AforoEventos, 1);
    public double? Asistencia => VendidasEventosRealizados == 0 ? null : Math.Round(100.0 * UsadasEventosRealizados / VendidasEventosRealizados, 1);
    public double? Conversion => VisitasEventos == 0 ? null : Math.Round(100.0 * ComprasEventos / VisitasEventos, 1);

    // Usuarios con al menos una compra en los 90 días previos al fin del período
    public int UsuariosActivos { get; set; }

    public List<PuntoSemana> IngresosPorSemana { get; set; } = new();
    public List<VentaCategoria> VentasPorCategoria { get; set; } = new();

    // Las 10 compras pagadas más recientes del período (se actualizan en cuanto se paga una compra)
    public List<CompraReciente> UltimasCompras { get; set; } = new();

    public static double? Variacion(decimal actual, decimal anterior) =>
        anterior == 0 ? null : Math.Round((double)((actual - anterior) / anterior * 100), 1);
}

public interface IAnaliticaService
{
    Task<DashboardAnalitica> DashboardAsync(FiltroAnalitica filtro, CancellationToken ct = default);

    // HU22: clientes (usuarios con compras pagadas) con su segmento RFM, ordenados por gasto
    Task<List<ClienteAnalitica>> ClientesAsync(CancellationToken ct = default);

    // HU29: filtros combinables (gasto, entradas, compras, período, categoría y segmento), de mayor a menor
    Task<List<ClienteAnalitica>> FiltrarClientesAsync(FiltroClientes filtro, CancellationToken ct = default);

    // HU22: ficha analítica de un cliente; cada acceso queda en AUDITORIA_ANALITICA
    Task<FichaCliente?> FichaClienteAsync(string usuarioId, string administradorId, CancellationToken ct = default);

    Task RegistrarAuditoriaAsync(string administradorId, string consulta, string fuenteDatos, CancellationToken ct = default);

    Task<List<AuditoriaAnalitica>> AuditoriaAsync(int maximo = 50, CancellationToken ct = default);

    // =============================
    // 🔎 CONSULTAS DE SOLO LECTURA PARA LA IA (HU24) Y LA VARIABILIDAD (HU23)
    // =============================
    Task<List<VentaEvento>> TopEventosAsync(DateTime desde, DateTime hasta, int n, CancellationToken ct = default);
    Task<List<VentaCategoriaPeriodo>> IngresosPorCategoriaAsync(DateTime desde, DateTime hasta, CancellationToken ct = default);
    Task<List<VariabilidadEvento>> VariabilidadPorEventoAsync(DateTime desde, DateTime hasta, CancellationToken ct = default);
    Task<List<ResumenSegmento>> ResumenSegmentosAsync(CancellationToken ct = default);
    Task<List<EventoOcupacion>> EventosBajaOcupacionAsync(int dias, double umbral, CancellationToken ct = default);

    // HU23: variabilidad de ventas, precios y gasto con semáforo de decisión (umbrales configurables)
    Task<ReporteVariabilidad> VariabilidadAsync(FiltroAnalitica filtro, CancellationToken ct = default);
    Task<UmbralesVariabilidad> UmbralesAsync(CancellationToken ct = default);
    Task<string?> GuardarUmbralesAsync(UmbralesVariabilidad umbrales, string administradorId, CancellationToken ct = default);
}

// =============================
// 📈 ANALÍTICA DEL ADMINISTRADOR (HU21, HU22, HU29)
// =============================
// Consultas LINQ de solo lectura sobre EF Core. El dashboard se guarda en caché (Redis o memoria) 5 minutos
// para cumplir el tiempo de carga (RNF02); por eso muestra cuándo se calculó.
public class AnaliticaService : IAnaliticaService
{
    public const int SemanasTendencia = 10;
    private static readonly TimeSpan DuracionCache = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext _db;
    private readonly IDistributedCache _cache;

    public AnaliticaService(ApplicationDbContext db, IDistributedCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<DashboardAnalitica> DashboardAsync(FiltroAnalitica filtro, CancellationToken ct = default)
    {
        filtro = filtro.Normalizado();
        // La clave incluye una "huella" de los datos: una compra pagada o una entrada validada invalida la caché al instante
        var clave = "analitica:dashboard:" + filtro.Clave + ":" + await HuellaAsync(ct);

        var enCache = await _cache.GetStringAsync(clave, ct);
        if (enCache != null)
            return JsonSerializer.Deserialize<DashboardAnalitica>(enCache)!;

        var d = await CalcularAsync(filtro, ct);
        await _cache.SetStringAsync(clave, JsonSerializer.Serialize(d),
            new DistributedCacheEntryOptions().SetAbsoluteExpiration(DuracionCache), ct);
        return d;
    }

    // Órdenes pagadas en un rango de fechas locales (FechaPago se guarda en UTC)
    private IQueryable<Orden> Pagadas(DateTime desde, DateTime hastaInclusive, int? categoriaId)
    {
        var desdeUtc = desde.Date.ToUniversalTime();
        var hastaUtc = hastaInclusive.Date.AddDays(1).ToUniversalTime();
        var q = _db.Ordenes.AsNoTracking()
            .Where(o => o.Estado == EstadoOrden.Pagada && o.FechaPago >= desdeUtc && o.FechaPago < hastaUtc);
        return categoriaId == null ? q : q.Where(o => o.Evento!.CategoriaId == categoriaId);
    }

    private async Task<DashboardAnalitica> CalcularAsync(FiltroAnalitica f, CancellationToken ct)
    {
        var d = new DashboardAnalitica { Desde = f.Desde, Hasta = f.Hasta, CategoriaId = f.CategoriaId, CalculadoEn = DateTime.Now };

        // ---------- Ventas del período ----------
        var periodo = Pagadas(f.Desde, f.Hasta, f.CategoriaId);
        d.Compras = await periodo.CountAsync(ct);
        d.Ingresos = await periodo.SumAsync(o => (decimal?)o.Total, ct) ?? 0;
        d.Comisiones = await periodo.SumAsync(o => (decimal?)o.Comision, ct) ?? 0;
        d.EntradasVendidas = await periodo.SelectMany(o => o.Items).SumAsync(i => (int?)i.Cantidad, ct) ?? 0;

        var dias = (f.Hasta - f.Desde).Days + 1;
        var anterior = Pagadas(f.Desde.AddDays(-dias), f.Desde.AddDays(-1), f.CategoriaId);
        var comprasAnterior = await anterior.CountAsync(ct);
        d.IngresosAnterior = await anterior.SumAsync(o => (decimal?)o.Total, ct) ?? 0;
        d.EntradasAnterior = await anterior.SelectMany(o => o.Items).SumAsync(i => (int?)i.Cantidad, ct) ?? 0;
        d.TicketAnterior = comprasAnterior == 0 ? 0 : Math.Round(d.IngresosAnterior / comprasAnterior, 2);

        // ---------- Ocupación, asistencia y conversión ----------
        // De los eventos que vendieron en el período o que se realizan en él: así una compra de hoy para un evento
        // del próximo mes también cuenta en la ocupación (antes solo se miraban los eventos con fecha en el período)
        var hastaEvento = f.Hasta.AddDays(1);
        var conVentas = periodo.Select(o => o.EventoId);
        var eventos = _db.Eventos.AsNoTracking()
            .Where(e => e.Estado == EstadoEvento.Aprobado && ((e.Fecha >= f.Desde && e.Fecha < hastaEvento) || conVentas.Contains(e.Id)));
        if (f.CategoriaId != null)
            eventos = eventos.Where(e => e.CategoriaId == f.CategoriaId);

        var ahora = DateTime.Now;
        var porEvento = await eventos
            .Select(e => new
            {
                e.Fecha,
                e.Visitas,
                Aforo = e.TiposEntrada.Sum(t => t.Capacidad),
                Vendidas = _db.Entradas.Count(x => x.EventoId == e.Id && x.Estado != EstadoEntrada.Anulada),
                Usadas = _db.Entradas.Count(x => x.EventoId == e.Id && x.Estado == EstadoEntrada.Usada),
                Compras = _db.Ordenes.Count(o => o.EventoId == e.Id && o.Estado == EstadoOrden.Pagada)
            })
            .ToListAsync(ct);

        d.EventosConsiderados = porEvento.Count;
        d.EventosRealizados = porEvento.Count(e => e.Fecha <= ahora);
        d.AforoEventos = porEvento.Sum(e => e.Aforo);
        d.VendidasEventos = porEvento.Sum(e => e.Vendidas);
        d.VendidasEventosRealizados = porEvento.Where(e => e.Fecha <= ahora).Sum(e => e.Vendidas);
        d.UsadasEventosRealizados = porEvento.Where(e => e.Fecha <= ahora).Sum(e => e.Usadas);
        d.ComprasEventos = porEvento.Sum(e => e.Compras);
        d.VisitasEventos = porEvento.Sum(e => e.Visitas);

        // ---------- Usuarios activos (compra en los últimos 90 días) ----------
        d.UsuariosActivos = await Pagadas(f.Hasta.AddDays(-89), f.Hasta, f.CategoriaId).Select(o => o.UsuarioId).Distinct().CountAsync(ct);

        // ---------- Ingresos por semana (últimas 10 semanas hasta el fin del período) ----------
        var finSemana = f.Hasta.Date;
        var inicioTendencia = finSemana.AddDays(-7 * SemanasTendencia + 1);
        var pagos = await Pagadas(inicioTendencia, finSemana, f.CategoriaId)
            .Select(o => new { o.FechaPago, o.Total })
            .ToListAsync(ct);
        for (var s = 0; s < SemanasTendencia; s++)
        {
            var ini = inicioTendencia.AddDays(7 * s);
            var fin = ini.AddDays(7);
            var semana = ISOWeek.GetWeekOfYear(ini);
            d.IngresosPorSemana.Add(new PuntoSemana($"S{semana}",
                pagos.Where(p => p.FechaPago!.Value.ToLocalTime() >= ini && p.FechaPago.Value.ToLocalTime() < fin).Sum(p => p.Total)));
        }

        // ---------- Ventas por categoría en el período (sin el filtro de categoría, para comparar) ----------
        var porCategoria = await Pagadas(f.Desde, f.Hasta, null)
            .SelectMany(o => o.Items, (o, i) => new { o.Evento!.CategoriaId, i.Cantidad, Subtotal = i.PrecioUnitario * i.Cantidad })
            .GroupBy(x => x.CategoriaId)
            .Select(g => new { CategoriaId = g.Key, Entradas = g.Sum(x => x.Cantidad), Ingresos = g.Sum(x => x.Subtotal) })
            .ToListAsync(ct);
        var categorias = await _db.Categorias.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        d.VentasPorCategoria = porCategoria
            .Select(c => new VentaCategoria(categorias[c.CategoriaId].Nombre, categorias[c.CategoriaId].Color, c.Entradas, c.Ingresos))
            .OrderByDescending(c => c.Entradas)
            .ToList();

        // ---------- Últimas compras del período ----------
        d.UltimasCompras = (await periodo
                .OrderByDescending(o => o.FechaPago)
                .Take(10)
                .Select(o => new { o.FechaPago, Evento = o.Evento!.Titulo, o.UsuarioId, Entradas = o.Items.Sum(i => i.Cantidad), o.Total })
                .ToListAsync(ct))
            .Select(o => new CompraReciente(o.FechaPago!.Value.ToLocalTime(), o.Evento, ClienteAnalitica.CodigoDe(o.UsuarioId), o.Entradas, o.Total))
            .ToList();

        return d;
    }

    // Cambia cuando hay una compra pagada nueva, una entrada validada en puerta o un evento aprobado o cancelado
    private async Task<string> HuellaAsync(CancellationToken ct)
    {
        var pagadas = _db.Ordenes.AsNoTracking().Where(o => o.Estado == EstadoOrden.Pagada);
        var cantidad = await pagadas.CountAsync(ct);
        var ultima = await pagadas.MaxAsync(o => o.FechaPago, ct);
        var usadas = await _db.Entradas.AsNoTracking().CountAsync(e => e.Estado == EstadoEntrada.Usada, ct);
        var aprobados = await _db.Eventos.AsNoTracking().CountAsync(e => e.Estado == EstadoEvento.Aprobado, ct);
        return $"{cantidad}-{ultima?.Ticks}-{usadas}-{aprobados}";
    }

    // =============================
    // 👥 CLIENTES Y SEGMENTACIÓN RFM (HU22)
    // =============================
    public async Task<List<ClienteAnalitica>> ClientesAsync(CancellationToken ct = default)
    {
        var clientes = await AgregarClientesAsync(_db.Ordenes.AsNoTracking().Where(o => o.Estado == EstadoOrden.Pagada), ct);

        // Puntajes por quintiles entre todos los clientes: Recencia (menos días = mejor), Frecuencia y Monto
        var r = Puntajes(clientes.Select(c => -(double)c.DiasDesdeUltima).ToList());
        var f = Puntajes(clientes.Select(c => (double)c.Compras).ToList());
        var m = Puntajes(clientes.Select(c => (double)c.Gasto).ToList());
        for (var i = 0; i < clientes.Count; i++)
        {
            var c = clientes[i];
            (c.R, c.F, c.M) = (r[i], f[i], m[i]);
            c.Segmento = SegmentoRfm.De(c.Compras, c.DiasDesdeUltima, c.R, c.F, c.M);
        }

        return clientes.OrderByDescending(c => c.Gasto).ThenByDescending(c => c.Compras).ToList();
    }

    // Gasto, compras, entradas y última compra por usuario sobre un conjunto de órdenes pagadas
    private async Task<List<ClienteAnalitica>> AgregarClientesAsync(IQueryable<Orden> pagadas, CancellationToken ct)
    {
        var porUsuario = await pagadas
            .GroupBy(o => o.UsuarioId)
            .Select(g => new
            {
                UsuarioId = g.Key,
                Gasto = g.Sum(o => o.Total),
                Compras = g.Count(),
                Entradas = g.SelectMany(o => o.Items).Sum(i => i.Cantidad),
                Ultima = g.Max(o => o.FechaPago)
            })
            .ToListAsync(ct);

        var promociones = (await _db.Users.AsNoTracking()
            .Where(u => u.AceptaPromociones)
            .Select(u => u.Id)
            .ToListAsync(ct)).ToHashSet();

        var ahora = DateTime.UtcNow;
        return porUsuario.Select(c => new ClienteAnalitica
        {
            UsuarioId = c.UsuarioId,
            Gasto = c.Gasto,
            Compras = c.Compras,
            Entradas = c.Entradas,
            UltimaCompra = (c.Ultima ?? ahora).ToLocalTime(),
            DiasDesdeUltima = (int)(ahora - (c.Ultima ?? ahora)).TotalDays,
            AceptaPromociones = promociones.Contains(c.UsuarioId)
        }).ToList();
    }

    // =============================
    // 🔎 FILTRO DE CLIENTES (HU29)
    // =============================
    public async Task<List<ClienteAnalitica>> FiltrarClientesAsync(FiltroClientes filtro, CancellationToken ct = default)
    {
        var historial = await ClientesAsync(ct);
        var clientes = historial;

        // Con período o categoría, las cifras se recalculan solo con esas compras; el segmento RFM sigue siendo el del historial
        if (filtro.Desde != null || filtro.Hasta != null || filtro.CategoriaId != null)
        {
            var q = _db.Ordenes.AsNoTracking().Where(o => o.Estado == EstadoOrden.Pagada);
            if (filtro.Desde != null)
            {
                var desdeUtc = filtro.Desde.Value.Date.ToUniversalTime();
                q = q.Where(o => o.FechaPago >= desdeUtc);
            }
            if (filtro.Hasta != null)
            {
                var hastaUtc = filtro.Hasta.Value.Date.AddDays(1).ToUniversalTime();
                q = q.Where(o => o.FechaPago < hastaUtc);
            }
            if (filtro.CategoriaId != null)
                q = q.Where(o => o.Evento!.CategoriaId == filtro.CategoriaId);

            var rfm = historial.ToDictionary(c => c.UsuarioId);
            clientes = await AgregarClientesAsync(q, ct);
            foreach (var c in clientes)
            {
                var h = rfm[c.UsuarioId];
                (c.R, c.F, c.M, c.Segmento) = (h.R, h.F, h.M, h.Segmento);
            }
        }

        IEnumerable<ClienteAnalitica> r = clientes;
        if (filtro.GastoMin != null) r = r.Where(c => c.Gasto >= filtro.GastoMin);
        if (filtro.GastoMax != null) r = r.Where(c => c.Gasto <= filtro.GastoMax);
        if (filtro.EntradasMin != null) r = r.Where(c => c.Entradas >= filtro.EntradasMin);
        if (filtro.ComprasMin != null) r = r.Where(c => c.Compras >= filtro.ComprasMin);
        if (!string.IsNullOrEmpty(filtro.Segmento)) r = r.Where(c => c.Segmento == filtro.Segmento);

        // De mayor a menor según el criterio elegido (desempate por gasto)
        r = filtro.Orden switch
        {
            "entradas" => r.OrderByDescending(c => c.Entradas).ThenByDescending(c => c.Gasto),
            "compras" => r.OrderByDescending(c => c.Compras).ThenByDescending(c => c.Gasto),
            _ => r.OrderByDescending(c => c.Gasto).ThenByDescending(c => c.Compras)
        };
        return r.ToList();
    }

    // Puntaje 1–5 según la posición del valor entre todos (empates reciben el mismo puntaje; el mayor valor, 5)
    public static List<int> Puntajes(IReadOnlyList<double> valores)
    {
        var n = valores.Count;
        var ordenados = valores.OrderBy(v => v).ToList();
        return valores.Select(v =>
        {
            var menores = ordenados.TakeWhile(x => x < v).Count();
            return Math.Clamp((int)Math.Ceiling(5.0 * (menores + 1) / n), 1, 5);
        }).ToList();
    }

    public async Task<FichaCliente?> FichaClienteAsync(string usuarioId, string administradorId, CancellationToken ct = default)
    {
        var usuario = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario == null)
            return null;

        // Ley 29733: el acceso a los datos de una persona queda registrado (aunque no tenga compras)
        await RegistrarAuditoriaAsync(administradorId, $"Ficha del cliente {ClienteAnalitica.CodigoDe(usuarioId)}", "Ordenes, Entradas, Eventos", ct);

        var resumen = (await ClientesAsync(ct)).FirstOrDefault(c => c.UsuarioId == usuarioId)
                      ?? new ClienteAnalitica { UsuarioId = usuarioId, Segmento = "Sin compras" };

        var pagadas = _db.Ordenes.AsNoTracking().Where(o => o.UsuarioId == usuarioId && o.Estado == EstadoOrden.Pagada);

        var ficha = new FichaCliente
        {
            Resumen = resumen,
            Nombre = usuario.NombreCompleto,
            Correo = usuario.Email ?? "",
            MiembroDesde = usuario.FechaRegistro.ToLocalTime(),
            UltimasCompras = (await pagadas
                    .OrderByDescending(o => o.FechaPago)
                    .Take(10)
                    .Select(o => new { o.Codigo, o.FechaPago, o.EventoId, Evento = o.Evento!.Titulo, Entradas = o.Items.Sum(i => i.Cantidad), o.Total })
                    .ToListAsync(ct))
                .Select(o => new CompraCliente(o.Codigo, o.FechaPago!.Value.ToLocalTime(), o.EventoId, o.Evento, o.Entradas, o.Total))
                .ToList(),

            // Asistió = tiene entradas Usadas (validadas en puerta); no basta con haber comprado
            EventosAsistidos = (await _db.Entradas.AsNoTracking()
                    .Where(e => e.UsuarioId == usuarioId && e.Estado == EstadoEntrada.Usada)
                    .GroupBy(e => new { e.EventoId, e.Evento!.Titulo, e.Evento.Fecha })
                    .Select(g => new { g.Key.EventoId, g.Key.Titulo, g.Key.Fecha, Entradas = g.Count() })
                    .ToListAsync(ct))
                .OrderByDescending(e => e.Fecha)
                .Select(e => new EventoAsistido(e.EventoId, e.Titulo, e.Fecha, e.Entradas))
                .ToList()
        };

        var items = await pagadas
            .SelectMany(o => o.Items, (o, i) => new { Evento = o.Evento!.Titulo, Categoria = o.Evento.Categoria!.Nombre, i.PrecioUnitario, i.Cantidad })
            .ToListAsync(ct);

        if (items.Count > 0)
        {
            var masCara = items.OrderByDescending(i => i.PrecioUnitario).First();
            ficha.MayorPrecioEntrada = (masCara.Evento, masCara.PrecioUnitario);

            var mayorGasto = items.GroupBy(i => i.Evento).Select(g => (Evento: g.Key, Total: g.Sum(i => i.PrecioUnitario * i.Cantidad)))
                .OrderByDescending(g => g.Total).First();
            ficha.MayorGastoEvento = mayorGasto;

            var preferida = items.GroupBy(i => i.Categoria).Select(g => (Categoria: g.Key, Entradas: g.Sum(i => i.Cantidad)))
                .OrderByDescending(g => g.Entradas).First();
            ficha.CategoriaPreferida = preferida;
        }

        return ficha;
    }

    public async Task RegistrarAuditoriaAsync(string administradorId, string consulta, string fuenteDatos, CancellationToken ct = default)
    {
        _db.AuditoriasAnalitica.Add(new AuditoriaAnalitica
        {
            AdministradorId = administradorId,
            Consulta = consulta.Length > 200 ? consulta[..200] : consulta,
            FuenteDatos = fuenteDatos,
            Fecha = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    public Task<List<AuditoriaAnalitica>> AuditoriaAsync(int maximo = 50, CancellationToken ct = default) =>
        _db.AuditoriasAnalitica.AsNoTracking()
            .Include(a => a.Administrador)
            .OrderByDescending(a => a.Fecha)
            .Take(maximo)
            .ToListAsync(ct);

    // =============================
    // 🔎 CONSULTAS DE SOLO LECTURA (HU24 / HU23)
    // =============================
    // Fechas locales; "hasta" incluye todo el día. Solo agregados: nunca nombres ni correos.
    public async Task<List<VentaEvento>> TopEventosAsync(DateTime desde, DateTime hasta, int n, CancellationToken ct = default)
    {
        var ventas = await Pagadas(desde, hasta, null)
            .GroupBy(o => new { o.EventoId, o.Evento!.Titulo, Categoria = o.Evento.Categoria!.Nombre, o.Evento.Aforo })
            .Select(g => new { g.Key.EventoId, g.Key.Titulo, g.Key.Categoria, g.Key.Aforo, Entradas = g.SelectMany(o => o.Items).Sum(i => i.Cantidad), Ingresos = g.Sum(o => o.Total) })
            .ToListAsync(ct);

        return ventas
            .Select(v => new VentaEvento(v.EventoId, v.Titulo, v.Categoria, v.Entradas, v.Ingresos, v.Aforo))
            .OrderByDescending(v => v.Entradas).ThenByDescending(v => v.Ingresos)
            .Take(Math.Clamp(n, 1, 50))
            .ToList();
    }

    public async Task<List<VentaCategoriaPeriodo>> IngresosPorCategoriaAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var ventas = await Pagadas(desde, hasta, null)
            .GroupBy(o => o.Evento!.Categoria!.Nombre)
            .Select(g => new { Categoria = g.Key, Eventos = g.Select(o => o.EventoId).Distinct().Count(), Entradas = g.SelectMany(o => o.Items).Sum(i => i.Cantidad), Ingresos = g.Sum(o => o.Total) })
            .ToListAsync(ct);

        return ventas
            .Select(v => new VentaCategoriaPeriodo(v.Categoria, v.Eventos, v.Entradas, v.Ingresos))
            .OrderByDescending(v => v.Ingresos)
            .ToList();
    }

    // Entradas vendidas por semana de cada evento (desde su primera venta del período hasta su fecha o el fin del período).
    // Con menos de 2 semanas no hay variabilidad que medir. σ es la desviación estándar muestral (n − 1).
    public async Task<List<VariabilidadEvento>> VariabilidadPorEventoAsync(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var umbrales = await UmbralesAsync(ct);
        var inicio = desde.Date;
        var ventas = await Pagadas(inicio, hasta, null)
            .SelectMany(o => o.Items, (o, i) => new { o.EventoId, o.Evento!.Titulo, Categoria = o.Evento.Categoria!.Nombre, EventoFecha = o.Evento.Fecha, o.FechaPago, i.Cantidad })
            .ToListAsync(ct);

        var semanaFin = (int)((hasta.Date - inicio).TotalDays / 7);
        var resultado = new List<VariabilidadEvento>();
        foreach (var g in ventas.GroupBy(v => new { v.EventoId, v.Titulo, v.Categoria, v.EventoFecha }))
        {
            int Semana(DateTime d) => (int)((d.Date - inicio).TotalDays / 7);
            var porSemana = g.GroupBy(v => Semana(v.FechaPago!.Value.ToLocalTime())).ToDictionary(s => s.Key, s => s.Sum(v => v.Cantidad));
            var primera = porSemana.Keys.Min();
            var ultima = Math.Max(porSemana.Keys.Max(), Math.Min(semanaFin, Semana(g.Key.EventoFecha)));
            if (ultima - primera < 1)
                continue;

            var serie = Enumerable.Range(primera, ultima - primera + 1).Select(s => porSemana.GetValueOrDefault(s)).ToList();
            var valores = serie.Select(v => (double)v).ToArray();
            resultado.Add(new VariabilidadEvento(g.Key.EventoId, g.Key.Titulo, g.Key.Categoria, serie,
                Math.Round(MathNet.Numerics.Statistics.Statistics.Mean(valores), 2),
                Math.Round(MathNet.Numerics.Statistics.Statistics.StandardDeviation(valores), 2))
            { LimiteEstable = umbrales.CvEstable, LimiteInestable = umbrales.CvInestable });
        }

        return resultado.OrderByDescending(v => v.Cv ?? -1).ToList();
    }

    public async Task<List<ResumenSegmento>> ResumenSegmentosAsync(CancellationToken ct = default)
    {
        var clientes = await ClientesAsync(ct);
        return SegmentoRfm.Todos
            .Select(s => clientes.Where(c => c.Segmento == s).ToList())
            .Select((g, i) => new ResumenSegmento(SegmentoRfm.Todos[i], g.Count, g.Sum(c => c.Gasto), g.Count(c => c.AceptaPromociones)))
            .ToList();
    }

    // Eventos aprobados de los próximos "dias" con ocupación (entradas emitidas ÷ aforo) por debajo del umbral (0–1)
    public async Task<List<EventoOcupacion>> EventosBajaOcupacionAsync(int dias, double umbral, CancellationToken ct = default)
    {
        var ahora = DateTime.Now;
        var limite = ahora.Date.AddDays(Math.Clamp(dias, 1, 366) + 1);
        var eventos = await _db.Eventos.AsNoTracking()
            .Where(e => e.Estado == EstadoEvento.Aprobado && e.Fecha >= ahora && e.Fecha < limite)
            .Select(e => new
            {
                e.Id, e.Titulo, Categoria = e.Categoria!.Nombre, e.Fecha,
                Aforo = e.TiposEntrada.Sum(t => t.Capacidad),
                Vendidas = _db.Entradas.Count(x => x.EventoId == e.Id && x.Estado != EstadoEntrada.Anulada)
            })
            .ToListAsync(ct);

        return eventos
            .Select(e => new EventoOcupacion(e.Id, e.Titulo, e.Categoria, e.Fecha, e.Vendidas, e.Aforo))
            .Where(e => e.Aforo > 0 && e.Ocupacion < umbral)
            .OrderBy(e => e.Ocupacion).ThenBy(e => e.Fecha)
            .ToList();
    }

    // =============================
    // 🚦 VARIABILIDAD Y SEMÁFORO DE DECISIÓN (HU23)
    // =============================
    public async Task<UmbralesVariabilidad> UmbralesAsync(CancellationToken ct = default) =>
        await _db.UmbralesVariabilidad.AsNoTracking().OrderBy(u => u.Id).FirstOrDefaultAsync(ct) ?? new UmbralesVariabilidad();

    public async Task<string?> GuardarUmbralesAsync(UmbralesVariabilidad umbrales, string administradorId, CancellationToken ct = default)
    {
        var error = umbrales.Validar();
        if (error != null)
            return error;

        var actual = await _db.UmbralesVariabilidad.OrderBy(u => u.Id).FirstOrDefaultAsync(ct);
        if (actual == null)
            _db.UmbralesVariabilidad.Add(actual = new UmbralesVariabilidad());
        actual.CvEstable = Math.Round(umbrales.CvEstable, 1);
        actual.CvInestable = Math.Round(umbrales.CvInestable, 1);
        actual.OcupacionBaja = Math.Round(umbrales.OcupacionBaja, 1);
        actual.OcupacionAlta = Math.Round(umbrales.OcupacionAlta, 1);
        actual.ActualizadoEn = DateTime.UtcNow;
        actual.ActualizadoPorId = administradorId;
        await _db.SaveChangesAsync(ct);
        return null;
    }

    // Regla del semáforo: rojo = actuar (demanda inestable u ocupación baja), ámbar = monitorear, verde = mantener precio
    public static (Semaforo Color, string Decision) Decidir(double? cv, double? ocupacion, UmbralesVariabilidad u)
    {
        var ocupacionPct = ocupacion * 100;
        var inestable = cv > u.CvInestable;
        var baja = ocupacionPct < u.OcupacionBaja;
        var alta = ocupacionPct >= u.OcupacionAlta;

        if (inestable && baja)
            return (Semaforo.Rojo, "Demanda inestable y baja ocupación: lanzar una promoción y destacar el evento en las semanas de baja venta.");
        if (inestable)
            return (Semaforo.Rojo, "Demanda inestable: destacar el evento en las semanas de baja venta.");
        if (baja)
            return (Semaforo.Rojo, "Ocupación baja: revisar el precio o la fecha y lanzar una promoción.");
        if (cv == null)
            return (Semaforo.Ambar, "Pocas semanas de ventas para medir la variabilidad: monitorear.");
        if (cv >= u.CvEstable)
            return (Semaforo.Ambar, "Demanda variable: monitorear las ventas de las próximas semanas.");
        return alta
            ? (Semaforo.Verde, "Demanda estable y alta ocupación: mantener el precio (o evaluar subirlo).")
            : (Semaforo.Verde, "Demanda estable: mantener el precio.");
    }

    public async Task<ReporteVariabilidad> VariabilidadAsync(FiltroAnalitica filtro, CancellationToken ct = default)
    {
        var f = filtro.Normalizado();
        var umbrales = await UmbralesAsync(ct);
        var reporte = new ReporteVariabilidad { Desde = f.Desde, Hasta = f.Hasta, CategoriaId = f.CategoriaId, Umbrales = umbrales };

        var items = await Pagadas(f.Desde, f.Hasta, f.CategoriaId)
            .SelectMany(o => o.Items, (o, i) => new
            {
                OrdenId = o.Id, o.UsuarioId, o.Total, o.EventoId, o.Evento!.Titulo, Categoria = o.Evento.Categoria!.Nombre,
                EventoFecha = o.Evento.Fecha, o.FechaPago, i.Cantidad, i.PrecioUnitario
            })
            .ToListAsync(ct);
        if (items.Count == 0)
            return reporte;

        var inicio = f.Desde.Date;
        int Semana(DateTime d) => (int)((d.Date - inicio).TotalDays / 7);
        var semanaFin = Semana(f.Hasta);

        // Precios de las zonas y ocupación actual de los eventos con ventas en el período
        var ids = items.Select(i => i.EventoId).Distinct().ToList();
        var zonas = await _db.TiposEntrada.AsNoTracking().Where(t => ids.Contains(t.EventoId))
            .Select(t => new { t.EventoId, t.Precio, t.Capacidad }).ToListAsync(ct);
        var emitidas = await _db.Entradas.AsNoTracking().Where(e => ids.Contains(e.EventoId) && e.Estado != EstadoEntrada.Anulada)
            .GroupBy(e => e.EventoId).Select(g => new { g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);

        // ---------- Por evento: ventas semanales, precios, ocupación y semáforo ----------
        foreach (var g in items.GroupBy(i => new { i.EventoId, i.Titulo, i.Categoria, i.EventoFecha }))
        {
            var porSemana = g.GroupBy(v => Semana(v.FechaPago!.Value.ToLocalTime())).ToDictionary(s => s.Key, s => s.Sum(v => v.Cantidad));
            var primera = porSemana.Keys.Min();
            var ultima = Math.Max(porSemana.Keys.Max(), Math.Min(semanaFin, Semana(g.Key.EventoFecha)));
            var serie = Enumerable.Range(primera, ultima - primera + 1).Select(s => porSemana.GetValueOrDefault(s)).ToList();

            var precios = zonas.Where(z => z.EventoId == g.Key.EventoId).Select(z => z.Precio).ToList();
            var aforo = zonas.Where(z => z.EventoId == g.Key.EventoId).Sum(z => z.Capacidad);
            double? ocupacion = aforo == 0 ? null : (double)emitidas.GetValueOrDefault(g.Key.EventoId) / aforo;

            var fila = new FilaVariabilidadEvento
            {
                EventoId = g.Key.EventoId, Evento = g.Key.Titulo, Categoria = g.Key.Categoria, Fecha = g.Key.EventoFecha,
                Semanas = serie, Entradas = serie.Sum(),
                Ventas = Estadisticos.De(serie.Select(v => (double)v).ToList()),
                Precios = Estadisticos.De(precios.Select(p => (double)p).ToList()),
                PrecioMin = precios.DefaultIfEmpty().Min(), PrecioMax = precios.DefaultIfEmpty().Max(),
                Ocupacion = ocupacion
            };
            (fila.Semaforo, fila.Decision) = Decidir(fila.Ventas.Cv, ocupacion, umbrales);
            reporte.Eventos.Add(fila);
        }

        // Eventos con ventas atípicas en el período (más entradas que Q3 + 1,5 × IQR)
        var limiteEventos = Estadisticos.De(reporte.Eventos.Select(e => (double)e.Entradas).ToList()).LimiteAtipico;
        foreach (var e in reporte.Eventos)
            e.Atipico = limiteEventos != null && e.Entradas > limiteEventos;

        reporte.Eventos = reporte.Eventos
            .OrderByDescending(e => e.Semaforo).ThenByDescending(e => e.Ventas.Cv ?? -1).ThenBy(e => e.Evento)
            .ToList();

        // ---------- Por categoría: ventas semanales (con semanas sin venta), precio pagado y gasto por compra ----------
        foreach (var g in items.GroupBy(i => i.Categoria))
        {
            var porSemana = g.GroupBy(v => Semana(v.FechaPago!.Value.ToLocalTime())).ToDictionary(s => s.Key, s => s.Sum(v => v.Cantidad));
            var serie = Enumerable.Range(0, semanaFin + 1).Select(s => (double)porSemana.GetValueOrDefault(s)).ToList();
            reporte.Categorias.Add(new FilaVariabilidadCategoria
            {
                Categoria = g.Key,
                Eventos = g.Select(v => v.EventoId).Distinct().Count(),
                Entradas = g.Sum(v => v.Cantidad),
                VentasSemanales = Estadisticos.De(serie),
                PrecioPagado = Estadisticos.De(g.SelectMany(v => Enumerable.Repeat((double)v.PrecioUnitario, v.Cantidad)).ToList()),
                GastoPorCompra = Estadisticos.De(g.DistinctBy(v => v.OrdenId).Select(v => (double)v.Total).ToList())
            });
        }
        reporte.Categorias = reporte.Categorias.OrderByDescending(c => c.Entradas).ToList();

        // ---------- Gasto: por compra y por cliente, con clientes atípicos (código anónimo) ----------
        var ordenes = items.DistinctBy(i => i.OrdenId).ToList();
        reporte.GastoPorCompra = Estadisticos.De(ordenes.Select(o => (double)o.Total).ToList());
        var porCliente = ordenes.GroupBy(o => o.UsuarioId).Select(c => (UsuarioId: c.Key, Gasto: c.Sum(o => o.Total), Compras: c.Count())).ToList();
        reporte.GastoPorCliente = Estadisticos.De(porCliente.Select(c => (double)c.Gasto).ToList());
        var limiteClientes = reporte.GastoPorCliente.LimiteAtipico;
        reporte.ClientesAtipicos = limiteClientes == null ? new() : porCliente
            .Where(c => (double)c.Gasto > limiteClientes)
            .OrderByDescending(c => c.Gasto)
            .Select(c => new ClienteAtipico(c.UsuarioId, ClienteAnalitica.CodigoDe(c.UsuarioId), c.Gasto, c.Compras))
            .ToList();

        return reporte;
    }
}
