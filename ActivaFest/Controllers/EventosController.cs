using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;
using ActivaFest.Services.Email;
using ActivaFest.Services.Imagenes;
using ActivaFest.Services.Recomendaciones;

namespace ActivaFest.Controllers
{
    public class EventosController : Controller
    {
        private const string RolesPublicadores = Roles.Organizador + "," + Roles.Administrador;
        private const long LimiteSubida = 60 * 1024 * 1024; // 10 fotos x 5 MB + formulario

        private readonly ApplicationDbContext _context;
        private readonly ICatalogoService _catalogo;
        private readonly IAlmacenamientoImagenes _imagenes;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _email;
        private readonly IRecomendacionService _recomendaciones;
        private readonly ILogger<EventosController> _logger;

        public EventosController(
            ApplicationDbContext context,
            ICatalogoService catalogo,
            IAlmacenamientoImagenes imagenes,
            UserManager<ApplicationUser> userManager,
            IEmailService email,
            IRecomendacionService recomendaciones,
            ILogger<EventosController> logger)
        {
            _context = context;
            _catalogo = catalogo;
            _imagenes = imagenes;
            _userManager = userManager;
            _email = email;
            _recomendaciones = recomendaciones;
            _logger = logger;
        }

        // =============================
        // 📚 CATÁLOGO + BÚSQUEDA + FILTROS + PAGINACIÓN (HU02, HU05)
        // =============================
        public async Task<IActionResult> Index([FromQuery] CatalogoFiltro filtro, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                filtro = new CatalogoFiltro();

            var modelo = new CatalogoViewModel
            {
                Filtro = filtro,
                Resultado = await _catalogo.BuscarAsync(filtro, ct),
                Categorias = await _catalogo.ObtenerCategoriasAsync(ct)
            };

            // Lo que busca el usuario alimenta sus recomendaciones (HU17); cambiar de página no es otra búsqueda
            if (User.Identity?.IsAuthenticated == true && filtro.Pagina == 1 && (filtro.CategoriaId != null || !string.IsNullOrWhiteSpace(filtro.Buscar)))
                await _recomendaciones.RegistrarBusquedaAsync(_userManager.GetUserId(User)!, filtro.CategoriaId, filtro.Buscar, ct);

            return View(modelo);
        }

        // =============================
        // 🔥 FICHA DEL EVENTO: GALERÍA, PRECIOS Y MAPA (HU03, HU04)
        // =============================
        public async Task<IActionResult> Detalle(int id, CancellationToken ct)
        {
            var evento = await _context.Eventos
                .AsNoTracking()
                .Include(e => e.Categoria)
                .Include(e => e.TiposEntrada)
                .Include(e => e.Imagenes.OrderBy(i => i.Orden))
                .AsSplitQuery()
                .FirstOrDefaultAsync(e => e.Id == id, ct);

            if (evento == null)
                return NotFound();

            var esDuenio = evento.OrganizadorId != null && evento.OrganizadorId == _userManager.GetUserId(User);

            // Los eventos no aprobados solo los ve su organizador o un administrador
            // Los cancelados siguen visibles (con aviso) para quien llegue desde el correo o "Mis entradas"
            if (evento.Estado is not (EstadoEvento.Aprobado or EstadoEvento.Cancelado) && !User.IsInRole(Roles.Administrador) && !esDuenio)
                return NotFound();

            // Visualizaciones para el panel del organizador (RF11); sus propias visitas no cuentan
            if (evento.Estado == EstadoEvento.Aprobado && !esDuenio)
            {
                await _context.Eventos.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.Visitas, e => e.Visitas + 1), ct);

                // Historial para "Eventos recomendados para ti" (HU17)
                if (User.Identity?.IsAuthenticated == true)
                    await _recomendaciones.RegistrarVistaAsync(_userManager.GetUserId(User)!, evento.Id, ct);
            }

            ViewBag.PuedeEditar = esDuenio || User.IsInRole(Roles.Administrador);

            // HU25: plano del recinto generado desde la plantilla (con los asientos numerados si es teatro)
            var asientos = evento.PlantillaPlano == PlantillaPlano.Teatro
                ? await _context.Asientos.AsNoTracking().Where(a => a.TipoEntrada!.EventoId == id).ToListAsync(ct)
                : new List<Asiento>();
            ViewBag.PlanoSvg = Services.Plano.PlanoService.SvgDeEvento(evento, asientos);
            return View(evento);
        }

        // =============================
        // 📝 CREAR EVENTO (queda pendiente de moderación)
        // =============================
        [Authorize(Roles = RolesPublicadores)]
        public async Task<IActionResult> Crear(CancellationToken ct)
        {
            await CargarCategoriasAsync(ct);
            return View(new EventoFormViewModel { Fecha = DateTime.Today.AddDays(7).AddHours(20) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RolesPublicadores)]
        [RequestSizeLimit(LimiteSubida)]
        [RequestFormLimits(MultipartBodyLengthLimit = LimiteSubida)]
        public async Task<IActionResult> Crear(EventoFormViewModel model, CancellationToken ct)
        {
            var fotos = model.Fotos.Where(f => f.Length > 0).ToList();

            foreach (var foto in fotos)
            {
                var error = ValidadorImagenes.Validar(foto);
                if (error != null)
                    ModelState.AddModelError(nameof(model.Fotos), error);
            }

            // Plano oficial del recinto (opcional): mismas reglas que las fotos (JPG, PNG o WebP y tamaño máximo)
            var planoOficial = model.PlanoOficialArchivo is { Length: > 0 } archivoPlano ? archivoPlano : null;
            if (planoOficial != null && ValidadorImagenes.Validar(planoOficial) is { } errorPlano)
                ModelState.AddModelError(nameof(model.PlanoOficialArchivo), errorPlano);

            if (model.CategoriaId.HasValue && !await _context.Categorias.AnyAsync(c => c.Id == model.CategoriaId, ct))
                ModelState.AddModelError(nameof(model.CategoriaId), "Categoría no válida.");

            if (!ModelState.IsValid)
            {
                await CargarCategoriasAsync(ct);
                return View(model);
            }

            var usuario = await _userManager.GetUserAsync(User);

            var evento = new Evento
            {
                Titulo = model.Titulo.Trim(),
                Descripcion = model.Descripcion.Trim(),
                CategoriaId = model.CategoriaId!.Value,
                Fecha = model.Fecha!.Value,
                Ubicacion = model.Ubicacion.Trim(),
                Latitud = Math.Round(model.Latitud!.Value, 6),
                Longitud = Math.Round(model.Longitud!.Value, 6),
                OrganizadorId = usuario?.Id,
                Estado = EstadoEvento.Pendiente,
                FechaCreacion = DateTime.UtcNow,
                PlantillaPlano = model.Plantilla,
                TiposEntrada = model.TiposEntrada.Select((t, i) => Services.Plano.PlanoService.AplicarZona(new TipoEntrada
                {
                    Nombre = t.Nombre.Trim(),
                    Precio = Math.Round(t.Precio, 2),
                    Capacidad = t.Capacidad
                }, t, model.Plantilla, i)).ToList()
            };
            // HU25: el aforo del evento es siempre la suma de sus zonas; en teatro se generan los asientos numerados
            evento.Aforo = evento.TiposEntrada.Sum(t => t.Capacidad);
            if (evento.PlantillaPlano == PlantillaPlano.Teatro)
                foreach (var asiento in Services.Plano.PlanoService.GenerarAsientos(evento.TiposEntrada.ToList()))
                    asiento.TipoEntrada!.Asientos.Add(asiento);

            // Primero se guardan las fotos; si la BD falla, se eliminan para no dejar archivos huérfanos
            var rutasGuardadas = new List<string>();
            try
            {
                for (var i = 0; i < fotos.Count; i++)
                {
                    var ruta = await _imagenes.GuardarAsync(fotos[i], "eventos", ct);
                    rutasGuardadas.Add(ruta);
                    evento.Imagenes.Add(new EventoImagen { Ruta = ruta, Orden = i });
                }

                if (planoOficial != null)
                {
                    evento.PlanoOficial = await _imagenes.GuardarAsync(planoOficial, "planos", ct);
                    rutasGuardadas.Add(evento.PlanoOficial);
                }

                _context.Eventos.Add(evento);
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el evento {Titulo}", model.Titulo);

                foreach (var ruta in rutasGuardadas)
                    await _imagenes.EliminarAsync(ruta, CancellationToken.None);

                ModelState.AddModelError(string.Empty, "No se pudo guardar el evento. Inténtalo de nuevo.");
                await CargarCategoriasAsync(ct);
                return View(model);
            }

            HttpContext.Session.SetString("UltimoEvento", evento.Titulo);

            if (usuario?.Email != null)
            {
                await _email.EnviarAsync(usuario.Email, $"Evento recibido: {evento.Titulo}",
                    EmailTemplates.EventoRecibido(evento));
            }

            TempData["Mensaje"] = "Tu evento fue enviado a revisión. Te avisaremos por correo cuando sea aprobado.";
            return RedirectToAction(nameof(MisEventos));
        }

        // =============================
        // ✏️ EDITAR EVENTO (HU09)
        // =============================
        [Authorize(Roles = RolesPublicadores)]
        public async Task<IActionResult> Editar(int id, CancellationToken ct)
        {
            var evento = await CargarEventoEditableAsync(id, ct);
            if (evento == null)
                return NotFound();

            if (evento.Estado == EstadoEvento.Cancelado)
            {
                TempData["Error"] = "Este evento fue cancelado y ya no se puede editar.";
                return RedirectToAction(nameof(MisEventos));
            }

            await CargarCategoriasAsync(ct);
            return View(new EventoFormViewModel
            {
                Id = evento.Id,
                Titulo = evento.Titulo,
                Descripcion = evento.Descripcion,
                CategoriaId = evento.CategoriaId,
                Fecha = evento.Fecha,
                Ubicacion = evento.Ubicacion,
                Latitud = evento.Latitud,
                Longitud = evento.Longitud,
                Plantilla = evento.PlantillaPlano,
                TiposEntrada = evento.TiposEntrada.OrderBy(t => t.Id).Select(t => new TipoEntradaInput
                {
                    Id = t.Id,
                    Nombre = t.Nombre,
                    Precio = t.Precio,
                    Capacidad = t.Capacidad,
                    Color = t.Color,
                    Puerta = t.PuertaIngreso,
                    Filas = t.Filas,
                    AsientosPorFila = t.AsientosPorFila,
                    Comprometidas = t.Vendidas + t.Reservadas
                }).ToList(),
                ImagenesActuales = evento.Imagenes.OrderBy(i => i.Orden).ToList(),
                ImagenesActualesIds = evento.Imagenes.Select(i => i.Id).ToList(),
                PlanoOficialActual = evento.PlanoOficial
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RolesPublicadores)]
        [RequestSizeLimit(LimiteSubida)]
        [RequestFormLimits(MultipartBodyLengthLimit = LimiteSubida)]
        public async Task<IActionResult> Editar(int id, EventoFormViewModel model,
            [FromServices] Services.Eventos.IEventoEdicionService edicion, CancellationToken ct)
        {
            var evento = await CargarEventoEditableAsync(id, ct);
            if (evento == null)
                return NotFound();

            if (model.CategoriaId.HasValue && !await _context.Categorias.AnyAsync(c => c.Id == model.CategoriaId, ct))
                ModelState.AddModelError(nameof(model.CategoriaId), "Categoría no válida.");

            if (ModelState.IsValid)
            {
                var url = Url.Action(nameof(Detalle), "Eventos", new { id }, Request.Scheme)!;
                var resultado = await edicion.ActualizarAsync(id, model, url, ct);

                if (resultado.Exito)
                {
                    TempData["Mensaje"] = evento.Estado == EstadoEvento.Aprobado
                        ? "Cambios guardados." + (resultado.CompradoresNotificados > 0
                            ? $" Avisamos por correo a {resultado.CompradoresNotificados} {(resultado.CompradoresNotificados == 1 ? "comprador" : "compradores")}."
                            : "")
                        : "Cambios guardados. El evento volvió a revisión.";
                    return RedirectToAction(nameof(MisEventos));
                }

                foreach (var error in resultado.Errores)
                    ModelState.AddModelError(string.Empty, error);
            }

            // Se vuelve a mostrar el formulario con las fotos actuales y lo comprometido por zona
            model.Id = id;
            model.ImagenesActuales = evento.Imagenes.OrderBy(i => i.Orden).ToList();
            model.ImagenesActualesIds = evento.Imagenes.Select(i => i.Id).ToList();
            model.PlanoOficialActual = evento.PlanoOficial;
            foreach (var tipo in model.TiposEntrada.Where(t => t.Id.HasValue))
            {
                var actual = evento.TiposEntrada.FirstOrDefault(t => t.Id == tipo.Id);
                tipo.Comprometidas = actual == null ? 0 : actual.Vendidas + actual.Reservadas;
            }

            await CargarCategoriasAsync(ct);
            return View(model);
        }

        // Solo el organizador dueño o un administrador pueden editar
        // =============================
        // 🚫 CANCELAR EVENTO (HU15): anula las entradas y avisa a los compradores
        // =============================
        [Authorize(Roles = RolesPublicadores)]
        public async Task<IActionResult> Cancelar(int id, CancellationToken ct)
        {
            var evento = await CargarEventoEditableAsync(id, ct);
            if (evento == null)
                return NotFound();

            if (evento.Estado != EstadoEvento.Aprobado || evento.YaOcurrio(DateTime.Now))
            {
                TempData["Error"] = "Solo se puede cancelar un evento publicado que aún no se realizó.";
                return RedirectToAction(nameof(MisEventos));
            }

            ViewBag.EntradasVendidas = evento.TiposEntrada.Sum(t => t.Vendidas);
            ViewBag.Compradores = await _context.Ordenes.CountAsync(o => o.EventoId == id && o.Estado == EstadoOrden.Pagada, ct);
            return View(evento);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = RolesPublicadores)]
        [ActionName(nameof(Cancelar))]
        public async Task<IActionResult> ConfirmarCancelacion(int id, string? motivo,
            [FromServices] Services.Eventos.ICancelacionEventoService cancelacion, CancellationToken ct)
        {
            // Solo el organizador del evento o un administrador
            if (await CargarEventoEditableAsync(id, ct) == null)
                return NotFound();

            var urlMisEntradas = Url.Action("Index", "Entradas", null, Request.Scheme)!;
            var r = await cancelacion.CancelarAsync(id, motivo, urlMisEntradas, ct);

            switch (r.Resultado)
            {
                case Services.Eventos.ResultadoCancelacion.Cancelado:
                    TempData["Mensaje"] = $"El evento fue cancelado. Se anularon {r.EntradasAnuladas} entradas y avisamos por correo a " +
                                          $"{r.CompradoresAvisados} {(r.CompradoresAvisados == 1 ? "comprador" : "compradores")}.";
                    return RedirectToAction(nameof(MisEventos));
                case Services.Eventos.ResultadoCancelacion.MotivoInvalido:
                    TempData["Error"] = $"Indica el motivo de la cancelación (máximo {Services.Eventos.CancelacionEventoService.LargoMaximoMotivo} caracteres).";
                    return RedirectToAction(nameof(Cancelar), new { id });
                default:
                    TempData["Error"] = "Solo se puede cancelar un evento publicado que aún no se realizó.";
                    return RedirectToAction(nameof(MisEventos));
            }
        }

        // =============================
        // 🗺️ VISTA PREVIA DEL PLANO (HU25): el SVG se genera con los datos del formulario, sin guardar nada
        // =============================
        [Authorize(Roles = RolesPublicadores)]
        [HttpGet]
        public IActionResult VistaPreviaPlano([FromQuery] PlantillaPlano plantilla, [FromQuery] List<TipoEntradaInput> tiposEntrada)
        {
            if (!Enum.IsDefined(plantilla))
                return BadRequest();

            return Content(Services.Plano.PlanoService.SvgVistaPrevia(plantilla, tiposEntrada), "image/svg+xml; charset=utf-8");
        }

        private Task<Evento?> CargarEventoEditableAsync(int id, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User);
            var esAdmin = User.IsInRole(Roles.Administrador);

            return _context.Eventos
                .AsNoTracking()
                .Include(e => e.TiposEntrada)
                .Include(e => e.Imagenes)
                .AsSplitQuery()
                .FirstOrDefaultAsync(e => e.Id == id && (esAdmin || e.OrganizadorId == userId), ct);
        }

        // =============================
        // 📋 MIS EVENTOS (estado de moderación del organizador)
        // =============================
        [Authorize(Roles = RolesPublicadores)]
        public async Task<IActionResult> MisEventos(CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User);

            var eventos = await _context.Eventos
                .AsNoTracking()
                .Include(e => e.Categoria)
                .Include(e => e.Imagenes.OrderBy(i => i.Orden).Take(1))
                .Where(e => e.OrganizadorId == userId)
                .OrderByDescending(e => e.FechaCreacion)
                .ToListAsync(ct);

            return View(eventos);
        }

        private async Task CargarCategoriasAsync(CancellationToken ct)
        {
            ViewBag.Categorias = await _catalogo.ObtenerCategoriasAsync(ct);
        }
    }
}
