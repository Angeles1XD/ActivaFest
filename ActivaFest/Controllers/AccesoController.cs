using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Acceso;

namespace ActivaFest.Controllers
{
    public record SolicitudValidacion(int EventoId, string Contenido);

    // =============================
    // 🚪 CONTROL DE ACCESO EN PUERTA (HU08) — web móvil con la cámara del celular
    // =============================
    [Authorize(Roles = Roles.ControlAcceso + "," + Roles.Organizador + "," + Roles.Administrador)]
    public class AccesoController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IAccesoService _acceso;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccesoController(ApplicationDbContext db, IAccesoService acceso, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _acceso = acceso;
            _userManager = userManager;
        }

        private string UsuarioId => _userManager.GetUserId(User)!;

        // Un organizador (sin otro rol) solo controla sus propios eventos
        private bool SoloSusEventos =>
            !User.IsInRole(Roles.Administrador) && !User.IsInRole(Roles.ControlAcceso);

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var eventos = await _acceso.EventosControlablesAsync(UsuarioId, SoloSusEventos, ct);
            return View(eventos);
        }

        public async Task<IActionResult> Escanear(int id, CancellationToken ct)
        {
            if (!await _acceso.PuedeControlarAsync(UsuarioId, SoloSusEventos, id, ct))
                return NotFound();

            var evento = await _db.Eventos.AsNoTracking().FirstAsync(e => e.Id == id, ct);
            var (ingresados, vendidas) = await _acceso.AsistenciaAsync(id, ct);

            ViewBag.Ingresados = ingresados;
            ViewBag.Vendidas = vendidas;
            return View(evento);
        }

        [HttpPost("/api/acceso/validar")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Validar([FromBody] SolicitudValidacion solicitud, CancellationToken ct)
        {
            if (!await _acceso.PuedeControlarAsync(UsuarioId, SoloSusEventos, solicitud.EventoId, ct))
                return Forbid();

            var resultado = await _acceso.ValidarAsync(solicitud.EventoId, solicitud.Contenido, UsuarioId, DateTime.Now, ct);

            return Json(new
            {
                tipo = resultado.Tipo.ToString(),
                permiteIngreso = resultado.PermiteIngreso,
                mensaje = resultado.Mensaje,
                zona = resultado.Zona,
                titular = resultado.Titular,
                fechaUso = resultado.FechaUso?.ToString("dd/MM HH:mm:ss"),
                ingresados = resultado.Ingresados,
                vendidas = resultado.Vendidas
            });
        }
    }
}
