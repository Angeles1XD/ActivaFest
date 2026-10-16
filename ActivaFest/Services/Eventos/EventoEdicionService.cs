using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;
using ActivaFest.Services.Email;
using ActivaFest.Services.Imagenes;

namespace ActivaFest.Services.Eventos;

public record ResultadoEdicion(bool Exito, List<string> Errores, int CompradoresNotificados = 0)
{
    public static ResultadoEdicion Fallo(params string[] errores) => new(false, errores.ToList());
}

public interface IEventoEdicionService
{
    Task<ResultadoEdicion> ActualizarAsync(int eventoId, EventoFormViewModel datos, string urlEvento, CancellationToken ct = default);
}

// =============================
// ✏️ EDICIÓN DE EVENTOS (HU09 / RF07)
// =============================
// Protege lo ya vendido: la capacidad nunca baja de lo comprometido y no se borran zonas con compras.
public class EventoEdicionService : IEventoEdicionService
{
    private readonly ApplicationDbContext _db;
    private readonly IAlmacenamientoImagenes _imagenes;
    private readonly ICatalogoService _catalogo;
    private readonly IEmailService _email;
    private readonly ILogger<EventoEdicionService> _logger;

    public EventoEdicionService(ApplicationDbContext db, IAlmacenamientoImagenes imagenes, ICatalogoService catalogo,
        IEmailService email, ILogger<EventoEdicionService> logger)
    {
        _db = db;
        _imagenes = imagenes;
        _catalogo = catalogo;
        _email = email;
        _logger = logger;
    }

    public async Task<ResultadoEdicion> ActualizarAsync(int eventoId, EventoFormViewModel datos, string urlEvento, CancellationToken ct = default)
    {
        var evento = await _db.Eventos
            .Include(e => e.TiposEntrada)
            .Include(e => e.Imagenes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == eventoId, ct);

        if (evento == null)
            return ResultadoEdicion.Fallo("El evento no existe.");

        var errores = new List<string>();

        // ---------- Zonas de entrada ----------
        var enviadas = datos.TiposEntrada;
        var idsEnviados = enviadas.Where(t => t.Id.HasValue).Select(t => t.Id!.Value).ToHashSet();

        if (idsEnviados.Any(id => evento.TiposEntrada.All(t => t.Id != id)))
            return ResultadoEdicion.Fallo("Zona de entrada no válida.");

        var conCompras = await _db.OrdenItems
            .Where(i => i.TipoEntrada!.EventoId == eventoId)
            .Select(i => i.TipoEntradaId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tipo in evento.TiposEntrada.Where(t => !idsEnviados.Contains(t.Id)).ToList())
        {
            if (conCompras.Contains(tipo.Id))
                errores.Add($"La zona \"{tipo.Nombre}\" ya tiene compras: no se puede eliminar.");
            else
                _db.TiposEntrada.Remove(tipo);
        }

        foreach (var input in enviadas)
        {
            if (input.Id is int id)
            {
                var tipo = evento.TiposEntrada.First(t => t.Id == id);
                var comprometidas = tipo.Vendidas + tipo.Reservadas;
                if (input.Capacidad < comprometidas)
                {
                    errores.Add($"La zona \"{tipo.Nombre}\" ya tiene {comprometidas} entradas vendidas o reservadas: su capacidad no puede ser menor.");
                    continue;
                }

                tipo.Nombre = input.Nombre.Trim();
                tipo.Precio = Math.Round(input.Precio, 2); // solo afecta a compras futuras: cada orden guarda su precio
                tipo.Capacidad = input.Capacidad;
            }
            else
            {
                evento.TiposEntrada.Add(new TipoEntrada { Nombre = input.Nombre.Trim(), Precio = Math.Round(input.Precio, 2), Capacidad = input.Capacidad });
            }
        }

        // ---------- Fotos ----------
        var aEliminar = evento.Imagenes.Where(i => datos.ImagenesEliminar.Contains(i.Id)).ToList();
        var nuevas = datos.Fotos.Where(f => f.Length > 0).ToList();
        var total = evento.Imagenes.Count - aEliminar.Count + nuevas.Count;

        if (total < EventoFormViewModel.MinFotos)
            errores.Add($"El evento debe tener al menos {EventoFormViewModel.MinFotos} fotos.");
        if (total > EventoFormViewModel.MaxFotos)
            errores.Add($"El evento puede tener como máximo {EventoFormViewModel.MaxFotos} fotos.");

        foreach (var foto in nuevas)
        {
            var error = ValidadorImagenes.Validar(foto);
            if (error != null)
                errores.Add(error);
        }

        if (errores.Count > 0)
            return new ResultadoEdicion(false, errores);

        // ---------- Datos generales ----------
        var fechaAnterior = evento.Fecha;
        var ubicacionAnterior = evento.Ubicacion;

        evento.Titulo = datos.Titulo.Trim();
        evento.Descripcion = datos.Descripcion.Trim();
        evento.CategoriaId = datos.CategoriaId!.Value;
        evento.Fecha = datos.Fecha!.Value;
        evento.Ubicacion = datos.Ubicacion.Trim();
        evento.Latitud = Math.Round(datos.Latitud!.Value, 6);
        evento.Longitud = Math.Round(datos.Longitud!.Value, 6);
        evento.Aforo = evento.TiposEntrada.Sum(t => t.Capacidad);

        // Un evento publicado sigue publicado (tiene ventas en curso); uno pendiente o rechazado vuelve a revisión
        if (evento.Estado != EstadoEvento.Aprobado)
        {
            evento.Estado = EstadoEvento.Pendiente;
            evento.MotivoRechazo = null;
        }

        var rutasNuevas = new List<string>();
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // La capacidad se fija con un UPDATE condicional: si justo entró una compra, no puede quedar por debajo de lo vendido
            foreach (var input in enviadas.Where(t => t.Id.HasValue))
            {
                var nueva = input.Capacidad;
                var ok = await _db.TiposEntrada
                    .Where(t => t.Id == input.Id && t.Vendidas + t.Reservadas <= nueva)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Capacidad, nueva), ct);

                if (ok == 0)
                {
                    await tx.RollbackAsync(ct);
                    return ResultadoEdicion.Fallo("Se vendieron entradas mientras editabas: revisa las capacidades e inténtalo de nuevo.");
                }
            }

            var orden = evento.Imagenes.Except(aEliminar).Select(i => i.Orden).DefaultIfEmpty(-1).Max() + 1;
            foreach (var foto in nuevas)
            {
                var ruta = await _imagenes.GuardarAsync(foto, "eventos", ct);
                rutasNuevas.Add(ruta);
                evento.Imagenes.Add(new EventoImagen { Ruta = ruta, Orden = orden++ });
            }

            // Los contadores de venta los maneja solo CompraService: esta edición nunca los sobrescribe
            foreach (var tipo in evento.TiposEntrada.Where(t => _db.Entry(t).State == EntityState.Modified))
            {
                var entrada = _db.Entry(tipo);
                entrada.Property(t => t.Vendidas).IsModified = false;
                entrada.Property(t => t.Reservadas).IsModified = false;
                entrada.Property(t => t.Capacidad).IsModified = false;
            }

            _db.EventoImagenes.RemoveRange(aEliminar);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Error al actualizar el evento {Id}", eventoId);
            foreach (var ruta in rutasNuevas)
                await _imagenes.EliminarAsync(ruta, CancellationToken.None);
            return ResultadoEdicion.Fallo("No se pudo guardar el evento. Inténtalo de nuevo.");
        }

        // Los archivos quitados se borran recién cuando la BD ya no los referencia
        foreach (var imagen in aEliminar)
            await _imagenes.EliminarAsync(imagen.Ruta, CancellationToken.None);

        if (evento.Estado == EstadoEvento.Aprobado)
            await _catalogo.InvalidarAsync(ct);

        // ---------- Aviso a compradores si cambió la fecha o el lugar (RF13) ----------
        var notificados = 0;
        if (fechaAnterior != evento.Fecha || ubicacionAnterior != evento.Ubicacion)
        {
            var correos = await _db.Entradas
                .Where(e => e.EventoId == eventoId && e.Estado == EstadoEntrada.Activa)
                .Join(_db.Users, e => e.UsuarioId, u => u.Id, (e, u) => u.Email)
                .Where(c => c != null)
                .Distinct()
                .ToListAsync(ct);

            var html = EmailTemplates.EventoActualizado(evento, fechaAnterior, ubicacionAnterior, urlEvento);
            foreach (var correo in correos)
                await _email.EnviarAsync(correo!, $"Cambio en tu evento: {evento.Titulo}", html);

            notificados = correos.Count;
        }

        return new ResultadoEdicion(true, new List<string>(), notificados);
    }
}
