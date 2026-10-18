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
}

public record PuntoSemana(string Etiqueta, decimal Ingresos);
public record VentaCategoria(string Categoria, string? Color, int Entradas, decimal Ingresos);

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

    // De los eventos que se realizan en el período (por fecha del evento)
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
        var clave = "analitica:dashboard:" + filtro.Clave;

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

        // ---------- Eventos que se realizan en el período: ocupación, asistencia y conversión ----------
        var hastaEvento = f.Hasta.AddDays(1);
        var eventos = _db.Eventos.AsNoTracking()
            .Where(e => e.Estado == EstadoEvento.Aprobado && e.Fecha >= f.Desde && e.Fecha < hastaEvento);
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

        return d;
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
}
