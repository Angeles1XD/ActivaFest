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
        public List<string> Roles { get; set; } = new();
    }

    // =============================
    // 👥 GESTIÓN DE USUARIOS (módulo de administración)
    // =============================
    // Permite dar o quitar el perfil de Control de Acceso (personal de puerta, HU08) y de Organizador.
    [Authorize(Roles = Roles.Administrador)]
    public class UsuariosController : Controller
    {
        // Roles que el administrador puede asignar desde esta pantalla
        public static readonly string[] RolesGestionables = { Roles.ControlAcceso, Roles.Organizador };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public UsuariosController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

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

            ViewBag.Buscar = buscar;
            return View(usuarios.Select(u => new UsuarioFila
            {
                Id = u.Id,
                Nombre = u.NombreCompleto,
                Email = u.Email ?? "",
                Roles = roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).OrderBy(n => n).ToList()
            }).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CambiarRol(string usuarioId, string rol, bool asignar, string? buscar)
        {
            if (!RolesGestionables.Contains(rol))
                return BadRequest();

            var usuario = await _userManager.FindByIdAsync(usuarioId);
            if (usuario == null)
                return NotFound();

            var resultado = asignar
                ? await _userManager.AddToRoleAsync(usuario, rol)
                : await _userManager.RemoveFromRoleAsync(usuario, rol);

            if (resultado.Succeeded)
            {
                // Obliga a que el cambio de permisos aplique en la próxima petición del usuario
                await _userManager.UpdateSecurityStampAsync(usuario);
                TempData["Mensaje"] = $"{usuario.Email}: {(asignar ? "ahora tiene" : "ya no tiene")} el perfil {NombreRol(rol)}.";
            }
            else
            {
                TempData["Error"] = string.Join(" ", resultado.Errors.Select(e => e.Description));
            }

            return RedirectToAction(nameof(Index), new { buscar });
        }

        public static string NombreRol(string rol) => Roles.NombreVisible(rol);
    }
}
