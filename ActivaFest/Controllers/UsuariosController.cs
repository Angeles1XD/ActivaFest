using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;

namespace ActivaFest.Controllers
{
    public class UsuarioFila
    {
        public string Id { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Email { get; set; } = "";
        public bool Activo { get; set; }
        public List<string> Roles { get; set; } = new();
        public int EventosAsignados { get; set; }
    }

    public class AsignacionesViewModel
    {
        public string UsuarioId { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Email { get; set; } = "";
        public List<Evento> Eventos { get; set; } = new();
        public HashSet<int> Asignados { get; set; } = new();
    }

    // =============================
    // 👥 GESTIÓN DE CUENTAS, ROLES Y PERSONAL DE CONTROL (HU19)
    // =============================
    [Authorize(Roles = Roles.Administrador)]
    public class UsuariosController : Controller
    {
        // Roles que el administrador puede dar o quitar desde esta pantalla
        public static readonly string[] RolesGestionables = { Roles.Organizador, Roles.ControlAcceso, Roles.Administrador };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<UsuariosController> _logger;

        public UsuariosController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ILogger<UsuariosController> logger)
        {
            _db = db;
            _userManager = userManager;
            _logger = logger;
        }

        private string MiId => _userManager.GetUserId(User)!;

        public async Task<IActionResult> Index(string? buscar, CancellationToken ct)
        {
            var query = _db.Users.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(buscar))
            {
                var patron = $"%{buscar.Trim()}%";
                query = query.Where(u => EF.Functions.Like(u.Email!, patron) || EF.Functions.Like(u.NombreCompleto, patron));
            }

            var usuarios = await query.OrderBy(u => u.Email).Take(100).ToListAsync(ct);
            var ids = usuarios.Select(u => u.Id).ToList();

            var roles = await (from ur in _db.UserRoles
                               join r in _db.Roles on ur.RoleId equals r.Id
                               where ids.Contains(ur.UserId)
                               select new { ur.UserId, r.Name }).ToListAsync(ct);

            var asignaciones = await _db.AsignacionesAcceso
                .Where(a => ids.Contains(a.UsuarioId))
                .GroupBy(a => a.UsuarioId)
                .Select(g => new { UsuarioId = g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(x => x.UsuarioId, x => x.Cantidad, ct);

            ViewBag.Buscar = buscar;
            ViewBag.MiId = MiId;
            return View(usuarios.Select(u => new UsuarioFila
            {
                Id = u.Id,
                Nombre = u.NombreCompleto,
                Email = u.Email ?? "",
                Activo = u.Activo,
                Roles = roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).OrderBy(n => n).ToList(),
                EventosAsignados = asignaciones.GetValueOrDefault(u.Id)
            }).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarRol(string usuarioId, string rol, bool asignar, string? buscar)
        {
            if (!RolesGestionables.Contains(rol))
                return BadRequest();

            // Criterio de HU19: el administrador no puede quitarse a sí mismo el rol Administrador
            if (!asignar && rol == Roles.Administrador && usuarioId == MiId)
            {
                TempData["Error"] = "No puedes quitarte a ti mismo el rol de Administrador.";
                return RedirectToAction(nameof(Index), new { buscar });
            }

            var usuario = await _userManager.FindByIdAsync(usuarioId);
            if (usuario == null)
                return NotFound();

            var resultado = asignar
                ? await _userManager.AddToRoleAsync(usuario, rol)
                : await _userManager.RemoveFromRoleAsync(usuario, rol);

            if (resultado.Succeeded)
            {
                // Obliga a que el cambio de permisos aplique en la siguiente validación de su sesión
                await _userManager.UpdateSecurityStampAsync(usuario);
                _logger.LogInformation("{Admin} {Accion} el rol {Rol} a {Usuario}", User.Identity!.Name, asignar ? "asignó" : "quitó", rol, usuario.Email);
                TempData["Mensaje"] = $"{usuario.Email}: {(asignar ? "ahora tiene" : "ya no tiene")} el perfil {NombreRol(rol)}.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", resultado.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index), new { buscar });
        }

        // Activar o desactivar una cuenta: una cuenta desactivada no puede iniciar sesión
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarEstado(string usuarioId, bool activo, string? buscar)
        {
            if (usuarioId == MiId)
            {
                TempData["Error"] = "No puedes desactivar tu propia cuenta.";
                return RedirectToAction(nameof(Index), new { buscar });
            }

            var usuario = await _userManager.FindByIdAsync(usuarioId);
            if (usuario == null)
                return NotFound();

            usuario.Activo = activo;
            await _userManager.UpdateAsync(usuario);

            // Cierra las sesiones abiertas de una cuenta desactivada
            await _userManager.UpdateSecurityStampAsync(usuario);

            _logger.LogInformation("{Admin} {Accion} la cuenta {Usuario}", User.Identity!.Name, activo ? "activó" : "desactivó", usuario.Email);
            TempData["Mensaje"] = $"La cuenta {usuario.Email} fue {(activo ? "activada" : "desactivada")}.";
            return RedirectToAction(nameof(Index), new { buscar });
        }

        // =============================
        // 🚪 ASIGNACIÓN DEL PERSONAL DE CONTROL A EVENTOS (ASIGNACION_ACCESO)
        // =============================
        public async Task<IActionResult> Asignaciones(string id, CancellationToken ct)
        {
            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario == null || !await _userManager.IsInRoleAsync(usuario, Roles.ControlAcceso))
                return NotFound();

            var desde = DateTime.Now.AddHours(-Evento.HorasValidezEntradas);
            return View(new AsignacionesViewModel
            {
                UsuarioId = usuario.Id,
                Nombre = usuario.NombreCompleto,
                Email = usuario.Email ?? "",
                Eventos = await _db.Eventos.AsNoTracking()
                    .Where(e => e.Estado == EstadoEvento.Aprobado && e.Fecha >= desde)
                    .OrderBy(e => e.Fecha)
                    .ToListAsync(ct),
                Asignados = (await _db.AsignacionesAcceso.Where(a => a.UsuarioId == usuario.Id).Select(a => a.EventoId).ToListAsync(ct)).ToHashSet()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Asignaciones(string id, int[] eventos, CancellationToken ct)
        {
            var usuario = await _userManager.FindByIdAsync(id);
            if (usuario == null || !await _userManager.IsInRoleAsync(usuario, Roles.ControlAcceso))
                return NotFound();

            // Solo eventos aprobados y vigentes; las asignaciones de eventos ya pasados se conservan como historial
            var desde = DateTime.Now.AddHours(-Evento.HorasValidezEntradas);
            var vigentes = await _db.Eventos
                .Where(e => e.Estado == EstadoEvento.Aprobado && e.Fecha >= desde)
                .Select(e => e.Id)
                .ToListAsync(ct);
            var elegidos = eventos.Intersect(vigentes).ToHashSet();

            var actuales = await _db.AsignacionesAcceso
                .Where(a => a.UsuarioId == id && vigentes.Contains(a.EventoId))
                .ToListAsync(ct);

            _db.AsignacionesAcceso.RemoveRange(actuales.Where(a => !elegidos.Contains(a.EventoId)));
            _db.AsignacionesAcceso.AddRange(elegidos
                .Where(e => actuales.All(a => a.EventoId != e))
                .Select(e => new AsignacionAcceso { UsuarioId = id, EventoId = e }));
            await _db.SaveChangesAsync(ct);

            TempData["Mensaje"] = $"{usuario.Email} puede validar entradas en {elegidos.Count} {(elegidos.Count == 1 ? "evento" : "eventos")}.";
            return RedirectToAction(nameof(Index), new { buscar = usuario.Email });
        }

        public static string NombreRol(string rol) => Roles.NombreVisible(rol);
    }
}
