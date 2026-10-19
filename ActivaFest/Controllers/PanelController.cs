using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ActivaFest.Models;
using ActivaFest.Services.Panel;

namespace ActivaFest.Controllers
{
    // =============================
    // 📊 PANEL DEL ORGANIZADOR (HU13)
    // =============================
    [Authorize(Roles = Roles.Organizador + "," + Roles.Administrador)]
    public class PanelController : Controller
    {
        private readonly IPanelService _panel;
        private readonly UserManager<ApplicationUser> _userManager;

        public PanelController(IPanelService panel, UserManager<ApplicationUser> userManager)
        {
            _panel = panel;
            _userManager = userManager;
        }

        private string UsuarioId => _userManager.GetUserId(User)!;

        private bool EsAdmin => User.IsInRole(Roles.Administrador);

        // Búsqueda, filtros y paginación (con muchos eventos no se cargan todos a la vez)
        public async Task<IActionResult> Index([FromQuery] FiltroPanel filtro, CancellationToken ct) =>
            View(await _panel.BuscarAsync(UsuarioId, EsAdmin, filtro, ct));

        public async Task<IActionResult> Evento(int id, CancellationToken ct)
        {
            var panel = await _panel.DetalleAsync(id, UsuarioId, EsAdmin, ct);
            return panel == null ? NotFound() : View(panel);
        }
    }
}
