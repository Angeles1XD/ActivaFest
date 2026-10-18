using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;
using ActivaFest.Services.Email;
using ActivaFest.Services.Entradas;

namespace ActivaFest.Services.Compras;

public class ComprasOptions
{
    // Diagrama de secuencia de compra: la reserva dura 10 minutos
    public int MinutosReserva { get; set; } = 10;
    public int MaxEntradasPorOrden { get; set; } = 10;
}

public class ComisionesOptions
{
    // Porcentaje que cobra la plataforma sobre cada venta de entradas (RF14)
    public decimal Porcentaje { get; set; } = 8m;
}

public record ResultadoOrden(Orden? Orden, string? Error)
{
    public static ResultadoOrden Fallo(string error) => new(null, error);
}

public enum ResultadoConfirmacion
{
    Confirmada,
    YaEstabaConfirmada,
    RequiereReembolso,
    MontoNoCoincide,
    NoEncontrada
}

public interface ICompraService
{
    Task<ResultadoOrden> CrearOrdenAsync(string usuarioId, int eventoId, IReadOnlyDictionary<int, int> cantidades, CancellationToken ct = default);

    Task<ResultadoConfirmacion> ConfirmarPagoAsync(string codigoOrden, string proveedor, string referencia, decimal montoPagado,
        string urlMisEntradas, CancellationToken ct = default);

    Task<bool> CancelarAsync(string codigoOrden, string usuarioId, CancellationToken ct = default);

    // HU06: un pago rechazado no descuenta aforo; la reserva se libera de inmediato
    Task<bool> RechazarPagoAsync(string codigoOrden, CancellationToken ct = default);

    Task<int> LiberarExpiradasAsync(CancellationToken ct = default);
}

// =============================
// 🛒 COMPRA DE ENTRADAS (HU06) + CONTROL DE AFORO SIN SOBREVENTA (HU11) + EMISIÓN (HU07)
// =============================
// Regla de oro: el stock solo cambia con UPDATE condicionales atómicos ("...WHERE quedan >= cantidad"),
// con token de concurrencia (RowVersion) por zona y un bloqueo distribuido por evento (Redis) al reservar (HU11).
// Así, aunque lleguen compras simultáneas, la base de datos impide vender más de la capacidad.
public class CompraService : ICompraService
{
    private readonly ApplicationDbContext _db;
    private readonly ICatalogoService _catalogo;
    private readonly IQrEntradaService _qr;
    private readonly IEmailService _email;
    private readonly ComprasOptions _opciones;
    private readonly ComisionesOptions _comisiones;
    private const int IntentosReserva = 5;
    private static readonly TimeSpan DuracionBloqueo = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan EsperaBloqueo = TimeSpan.FromSeconds(10);

    private readonly IBloqueoDistribuido _bloqueos;
    private readonly ILogger<CompraService> _logger;

    public CompraService(ApplicationDbContext db, ICatalogoService catalogo, IQrEntradaService qr, IEmailService email,
        IOptions<ComprasOptions> opciones, IOptions<ComisionesOptions> comisiones, IBloqueoDistribuido bloqueos, ILogger<CompraService> logger)
    {
        _db = db;
        _catalogo = catalogo;
        _qr = qr;
        _email = email;
        _opciones = opciones.Value;
        _comisiones = comisiones.Value;
        _bloqueos = bloqueos;
        _logger = logger;
    }

    // =============================
    // 1️⃣ CREAR ORDEN Y RESERVAR AFORO
    // =============================
    public async Task<ResultadoOrden> CrearOrdenAsync(string usuarioId, int eventoId, IReadOnlyDictionary<int, int> cantidades,
        CancellationToken ct = default)
    {
        var pedidas = cantidades.Where(c => c.Value > 0).ToDictionary(c => c.Key, c => c.Value);

        if (pedidas.Count == 0)
            return ResultadoOrden.Fallo("Selecciona al menos una entrada.");

        if (pedidas.Values.Sum() > _opciones.MaxEntradasPorOrden)
            return ResultadoOrden.Fallo($"Puedes comprar como máximo {_opciones.MaxEntradasPorOrden} entradas por compra.");

        var evento = await _db.Eventos
            .AsNoTracking()
            .Include(e => e.TiposEntrada)
            .FirstOrDefaultAsync(e => e.Id == eventoId, ct);

        if (evento == null || evento.Estado != EstadoEvento.Aprobado)
            return ResultadoOrden.Fallo("Este evento no está disponible para la venta.");

        if (evento.Fecha <= DateTime.Now)
            return ResultadoOrden.Fallo("La venta de este evento ya terminó.");

        var tipos = evento.TiposEntrada.ToDictionary(t => t.Id);
        if (pedidas.Keys.Any(id => !tipos.ContainsKey(id)))
            return ResultadoOrden.Fallo("Tipo de entrada no válido.");

        // Una sola orden pendiente por persona y evento: la anterior se cancela y libera su reserva
        var pendientes = await _db.Ordenes
            .Where(o => o.UsuarioId == usuarioId && o.EventoId == eventoId && o.Estado == EstadoOrden.PendientePago)
            .Select(o => o.Codigo)
            .ToListAsync(ct);

        foreach (var codigo in pendientes)
            await LiberarAsync(codigo, EstadoOrden.Cancelada, ct);

        var orden = new Orden
        {
            Codigo = CodigoSeguro.Generar(12),
            UsuarioId = usuarioId,
            EventoId = eventoId,
            Estado = EstadoOrden.PendientePago,
            FechaCreacion = DateTime.UtcNow,
            FechaExpiracion = DateTime.UtcNow.AddMinutes(_opciones.MinutosReserva),
            Items = pedidas.Select(p => new OrdenItem
            {
                TipoEntradaId = p.Key,
                Cantidad = p.Value,
                PrecioUnitario = tipos[p.Key].Precio
            }).ToList()
        };
        orden.Total = orden.Items.Sum(i => i.Subtotal);

        // HU11: bloqueo distribuido (Redis) por evento: las reservas del mismo evento se procesan de una en una
        await using var bloqueo = await _bloqueos.AdquirirAsync($"aforo:evento:{eventoId}", DuracionBloqueo, EsperaBloqueo, ct);
        if (bloqueo == null)
            return ResultadoOrden.Fallo("Hay mucha demanda en este momento. Inténtalo de nuevo en unos segundos.");

        // Una reserva vencida devuelve su aforo ya mismo (no espera al proceso que corre cada minuto)
        await LiberarExpiradasDelEventoAsync(eventoId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (var item in orden.Items)
        {
            if (!await ReservarAsync(item.TipoEntradaId, item.Cantidad, ct))
            {
                await tx.RollbackAsync(ct);
                var zona = tipos[item.TipoEntradaId];
                return ResultadoOrden.Fallo($"Ya no quedan {item.Cantidad} entradas disponibles en la zona {zona.Nombre}.");
            }
        }

        _db.Ordenes.Add(orden);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("🛒 Orden {Codigo} creada: {Cantidad} entradas, total {Total}", orden.Codigo, orden.CantidadEntradas, orden.Total);
        return new ResultadoOrden(orden, null);
    }

    // =============================
    // 2️⃣ CONFIRMAR PAGO Y EMITIR ENTRADAS (idempotente)
    // =============================
    public async Task<ResultadoConfirmacion> ConfirmarPagoAsync(string codigoOrden, string proveedor, string referencia,
        decimal montoPagado, string urlMisEntradas, CancellationToken ct = default)
    {
        var orden = await _db.Ordenes
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Codigo == codigoOrden, ct);

        if (orden == null)
            return ResultadoConfirmacion.NoEncontrada;

        if (orden.Estado == EstadoOrden.Pagada)
            return ResultadoConfirmacion.YaEstabaConfirmada;

        // Nunca se confía en el monto que diga el cliente: debe coincidir con el total de la orden
        if (Math.Abs(montoPagado - orden.Total) > 0.01m)
        {
            _logger.LogWarning("⚠️ Pago {Referencia} de {Monto} no coincide con la orden {Codigo} ({Total})",
                referencia, montoPagado, orden.Codigo, orden.Total);
            return ResultadoConfirmacion.MontoNoCoincide;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Solo una ejecución puede pasar la orden de Pendiente a Pagada (evita doble emisión si llegan
        // a la vez la redirección del navegador y la notificación de la pasarela)
        var marcada = await MarcarPagadaAsync(orden.Codigo, EstadoOrden.PendientePago, proveedor, referencia, ct);

        if (marcada)
        {
            foreach (var item in orden.Items)
            {
                var cantidad = item.Cantidad;
                await _db.TiposEntrada
                    .Where(t => t.Id == item.TipoEntradaId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.Reservadas, t => t.Reservadas - cantidad)
                        .SetProperty(t => t.Vendidas, t => t.Vendidas + cantidad)
                        .SetProperty(t => t.RowVersion, t => t.RowVersion + 1), ct);
            }
        }
        else
        {
            // La orden ya no estaba pendiente: ¿otro proceso la confirmó, o la reserva caducó?
            var estadoActual = await _db.Ordenes.Where(o => o.Id == orden.Id).Select(o => o.Estado).FirstAsync(ct);
            if (estadoActual == EstadoOrden.Pagada)
            {
                await tx.RollbackAsync(ct);
                return ResultadoConfirmacion.YaEstabaConfirmada;
            }

            // Reserva caducada pero el pago llegó: se intenta vender directo si aún hay aforo
            if (!await VenderSinReservaAsync(orden, ct))
            {
                await tx.RollbackAsync(ct);
                await _db.Ordenes.Where(o => o.Id == orden.Id && o.Estado != EstadoOrden.Pagada)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Estado, EstadoOrden.RequiereReembolso)
                        .SetProperty(o => o.ProveedorPago, proveedor)
                        .SetProperty(o => o.ReferenciaPago, referencia), ct);

                _logger.LogError("💸 Orden {Codigo} pagada sin aforo disponible: requiere reembolso", orden.Codigo);
                return ResultadoConfirmacion.RequiereReembolso;
            }

            if (!await MarcarPagadaAsync(orden.Codigo, estadoActual, proveedor, referencia, ct))
            {
                await tx.RollbackAsync(ct);
                return ResultadoConfirmacion.YaEstabaConfirmada;
            }
        }

        // Emisión: una entrada con QR único por cada persona
        var entradas = orden.Items
            .SelectMany(i => Enumerable.Range(0, i.Cantidad).Select(_ => new Entrada
            {
                Codigo = CodigoSeguro.Generar(),
                OrdenId = orden.Id,
                EventoId = orden.EventoId,
                TipoEntradaId = i.TipoEntradaId,
                UsuarioId = orden.UsuarioId,
                Estado = EstadoEntrada.Activa,
                FechaEmision = DateTime.UtcNow
            }))
            .ToList();

        _db.Entradas.AddRange(entradas);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation("✅ Orden {Codigo} pagada ({Proveedor} {Referencia}): {Cantidad} entradas emitidas",
            orden.Codigo, proveedor, referencia, entradas.Count);

        await _catalogo.InvalidarAsync(ct);
        await EnviarEntradasAsync(orden.Id, urlMisEntradas, ct);

        return ResultadoConfirmacion.Confirmada;
    }

    // =============================
    // 3️⃣ CANCELAR / EXPIRAR: devuelve la reserva al aforo
    // =============================
    public async Task<bool> CancelarAsync(string codigoOrden, string usuarioId, CancellationToken ct = default)
    {
        var esDelUsuario = await _db.Ordenes.AnyAsync(o => o.Codigo == codigoOrden && o.UsuarioId == usuarioId, ct);
        return esDelUsuario && await LiberarAsync(codigoOrden, EstadoOrden.Cancelada, ct);
    }

    public async Task<bool> RechazarPagoAsync(string codigoOrden, CancellationToken ct = default)
    {
        var liberada = await LiberarAsync(codigoOrden, EstadoOrden.Rechazada, ct);
        if (liberada)
        {
            _logger.LogInformation("💳 Pago rechazado: la orden {Codigo} liberó su reserva", codigoOrden);
            await _catalogo.InvalidarAsync(ct);
        }
        return liberada;
    }

    public async Task<int> LiberarExpiradasAsync(CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        var vencidas = await _db.Ordenes
            .Where(o => o.Estado == EstadoOrden.PendientePago && o.FechaExpiracion < ahora)
            .Select(o => o.Codigo)
            .ToListAsync(ct);

        var liberadas = 0;
        foreach (var codigo in vencidas)
        {
            if (await LiberarAsync(codigo, EstadoOrden.Expirada, ct))
                liberadas++;
        }

        if (liberadas > 0)
        {
            _logger.LogInformation("⏰ {Cantidad} reservas vencidas liberadas", liberadas);
            await _catalogo.InvalidarAsync(ct);
        }

        return liberadas;
    }

    // =============================
    // 🔐 RESERVA CON TOKEN DE CONCURRENCIA (RowVersion)
    // =============================
    // Se lee la zona con su versión y se actualiza "WHERE RowVersion = la leída": si otra compra la cambió
    // entre medio, no se toca ninguna fila y se reintenta con datos frescos. La condición de stock en el
    // WHERE es una segunda barrera: aunque algo falle, la base nunca deja Vendidas + Reservadas > Capacidad.
    private async Task<bool> ReservarAsync(int tipoEntradaId, int cantidad, CancellationToken ct)
    {
        for (var intento = 0; intento < IntentosReserva; intento++)
        {
            var zona = await _db.TiposEntrada.AsNoTracking()
                .Where(t => t.Id == tipoEntradaId)
                .Select(t => new { Libres = t.Capacidad - t.Vendidas - t.Reservadas, t.RowVersion })
                .FirstOrDefaultAsync(ct);

            if (zona == null || zona.Libres < cantidad)
                return false;

            var version = zona.RowVersion;
            var actualizadas = await _db.TiposEntrada
                .Where(t => t.Id == tipoEntradaId && t.RowVersion == version
                            && t.Capacidad - t.Vendidas - t.Reservadas >= cantidad
                            && _db.Eventos.Any(e => e.Id == t.EventoId && e.Estado == EstadoEvento.Aprobado)) // por si se canceló mientras tanto
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Reservadas, t => t.Reservadas + cantidad)
                    .SetProperty(t => t.RowVersion, t => t.RowVersion + 1), ct);

            if (actualizadas == 1)
                return true;
        }

        _logger.LogWarning("Reserva de {Cantidad} entradas en la zona {Zona} no lograda tras {Intentos} intentos por concurrencia",
            cantidad, tipoEntradaId, IntentosReserva);
        return false;
    }

    private async Task LiberarExpiradasDelEventoAsync(int eventoId, CancellationToken ct)
    {
        var ahora = DateTime.UtcNow;
        var vencidas = await _db.Ordenes
            .Where(o => o.EventoId == eventoId && o.Estado == EstadoOrden.PendientePago && o.FechaExpiracion < ahora)
            .Select(o => o.Codigo)
            .ToListAsync(ct);

        foreach (var codigo in vencidas)
            await LiberarAsync(codigo, EstadoOrden.Expirada, ct);
    }

    private async Task<bool> LiberarAsync(string codigoOrden, EstadoOrden nuevoEstado, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Solo libera quien logra sacar la orden de Pendiente: la reserva nunca se devuelve dos veces
        var cambiadas = await _db.Ordenes
            .Where(o => o.Codigo == codigoOrden && o.Estado == EstadoOrden.PendientePago)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Estado, nuevoEstado), ct);

        if (cambiadas == 0)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        var items = await _db.OrdenItems
            .Where(i => i.Orden!.Codigo == codigoOrden)
            .Select(i => new { i.TipoEntradaId, i.Cantidad })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            var cantidad = item.Cantidad;
            await _db.TiposEntrada
                .Where(t => t.Id == item.TipoEntradaId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Reservadas, t => t.Reservadas - cantidad)
                    .SetProperty(t => t.RowVersion, t => t.RowVersion + 1), ct);
        }

        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<bool> VenderSinReservaAsync(Orden orden, CancellationToken ct)
    {
        foreach (var item in orden.Items)
        {
            var cantidad = item.Cantidad;
            var vendidas = await _db.TiposEntrada
                .Where(t => t.Id == item.TipoEntradaId && t.Capacidad - t.Vendidas - t.Reservadas >= cantidad
                            && _db.Eventos.Any(e => e.Id == t.EventoId && e.Estado == EstadoEvento.Aprobado)) // no se vende si se canceló
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Vendidas, t => t.Vendidas + cantidad)
                    .SetProperty(t => t.RowVersion, t => t.RowVersion + 1), ct);

            if (vendidas == 0)
                return false;
        }

        return true;
    }

    private async Task<bool> MarcarPagadaAsync(string codigo, EstadoOrden desde, string proveedor, string referencia, CancellationToken ct)
    {
        DateTime? ahora = DateTime.UtcNow;
        var total = await _db.Ordenes.Where(o => o.Codigo == codigo).Select(o => o.Total).FirstAsync(ct);

        // La comisión se congela en el mismo UPDATE que confirma el pago (OE5: trazable y sin recalcular después)
        var porcentaje = _comisiones.Porcentaje;
        var comision = CalcularComision(total, porcentaje);

        var filas = await _db.Ordenes
            .Where(o => o.Codigo == codigo && o.Estado == desde)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Estado, EstadoOrden.Pagada)
                .SetProperty(o => o.FechaPago, ahora)
                .SetProperty(o => o.ProveedorPago, proveedor)
                .SetProperty(o => o.ReferenciaPago, referencia)
                .SetProperty(o => o.PorcentajeComision, porcentaje)
                .SetProperty(o => o.Comision, comision), ct);

        return filas == 1;
    }

    // Redondeo comercial a céntimos
    public static decimal CalcularComision(decimal total, decimal porcentaje) =>
        Math.Round(total * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);

    // Correo con un QR adjunto por entrada (HU07)
    private async Task EnviarEntradasAsync(int ordenId, string urlMisEntradas, CancellationToken ct)
    {
        try
        {
            var orden = await _db.Ordenes
                .AsNoTracking()
                .Include(o => o.Usuario)
                .Include(o => o.Evento)
                .Include(o => o.Items).ThenInclude(i => i.TipoEntrada)
                .Include(o => o.Entradas).ThenInclude(e => e.TipoEntrada)
                .AsSplitQuery()
                .FirstAsync(o => o.Id == ordenId, ct);

            if (orden.Usuario?.Email == null)
                return;

            var adjuntos = orden.Entradas
                .Select((e, i) => new AdjuntoCorreo($"entrada-{i + 1}-{e.TipoEntrada?.Nombre}.png", _qr.GenerarPng(e.Codigo)))
                .ToList();

            await _email.EnviarAsync(orden.Usuario.Email, $"Tus entradas para {orden.Evento!.Titulo}",
                EmailTemplates.EntradasCompradas(orden.Usuario.NombreCompleto, orden, orden.Evento, urlMisEntradas), adjuntos);
        }
        catch (Exception ex)
        {
            // Las entradas ya están emitidas y visibles en "Mis entradas": un fallo de correo no revierte la compra
            _logger.LogError(ex, "No se pudo enviar el correo de entradas de la orden {OrdenId}", ordenId);
        }
    }
}
