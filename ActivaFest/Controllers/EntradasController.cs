using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Entradas;

namespace ActivaFest.Controllers
{
    // =============================
    // 🎟️ MIS ENTRADAS (HU07)
    // =============================
    [Authorize]
    public class EntradasController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IQrEntradaService _qr;
        private readonly UserManager<ApplicationUser> _userManager;

        public EntradasController(ApplicationDbContext db, IQrEntradaService qr, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _qr = qr;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User);

            var entradas = await _db.Entradas
                .AsNoTracking()
                .Include(e => e.Evento)
                .Include(e => e.TipoEntrada)
                .Include(e => e.Orden)
                .Where(e => e.UsuarioId == userId)
                .ToListAsync(ct);

            // Próximos eventos primero (por fecha), luego los pasados (más recientes primero)
            var hoy = DateTime.Today;
            var grupos = entradas
                .GroupBy(e => e.EventoId)
                .Select(g => g.OrderBy(e => e.TipoEntrada!.Nombre).ThenBy(e => e.Id).ToList())
                .OrderBy(g => g[0].Evento!.Fecha < hoy)
                .ThenBy(g => g[0].Evento!.Fecha < hoy ? -g[0].Evento!.Fecha.Ticks : g[0].Evento!.Fecha.Ticks)
                .ToList();

            return View(grupos);
        }

        // Imagen del QR: solo para el dueño de la entrada
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Client)]
        public async Task<IActionResult> Qr(int id, CancellationToken ct)
        {
            var userId = _userManager.GetUserId(User);
            var codigo = await _db.Entradas
                .Where(e => e.Id == id && e.UsuarioId == userId)
                .Select(e => e.Codigo)
                .FirstOrDefaultAsync(ct);

            if (codigo == null)
                return NotFound();

            return File(_qr.GenerarPng(codigo), "image/png", $"entrada-{id}.png");
        }
    }
}
