using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;
using ActivaFest.Services.Email;

namespace ActivaFest.Controllers
{
    // =============================
    // 🛡️ PANEL DE MODERACIÓN (HU10)
    // =============================
    [Authorize(Roles = Roles.Administrador)]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ICatalogoService _catalogo;
        private readonly IEmailService _email;

        public AdminController(ApplicationDbContext context, ICatalogoService catalogo, IEmailService email)
        {
            _context = context;
            _catalogo = catalogo;
            _email = email;
        }

        public async Task<IActionResult> Index(EstadoEvento estado = EstadoEvento.Pendiente)
        {
            var eventos = await _context.Eventos
                .Include(e => e.Organizador)
                .Include(e => e.Categoria)
                .Where(e => e.Estado == estado)
                .OrderBy(e => e.FechaCreacion)
                .ToListAsync();

            ViewBag.EstadoActual = estado;
            ViewBag.Conteos = await _context.Eventos
                .GroupBy(e => e.Estado)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            return View(eventos);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Aprobar(int id)
        {
            var evento = await _context.Eventos.Include(e => e.Organizador).FirstOrDefaultAsync(e => e.Id == id);
            if (evento == null)
                return NotFound();

            evento.Estado = EstadoEvento.Aprobado;
            evento.MotivoRechazo = null;
            evento.FechaModeracion = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _catalogo.InvalidarAsync();

            if (evento.Organizador?.Email != null)
            {
                var url = Url.Action("Detalle", "Eventos", new { id = evento.Id }, Request.Scheme)!;
                await _email.EnviarAsync(evento.Organizador.Email, $"Evento aprobado: {evento.Titulo}",
                    EmailTemplates.EventoAprobado(evento, url));
            }

            TempData["Mensaje"] = $"Evento \"{evento.Titulo}\" aprobado y publicado.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rechazar(int id, string? motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                TempData["Error"] = "Debes indicar el motivo del rechazo.";
                return RedirectToAction(nameof(Index));
            }

            var evento = await _context.Eventos.Include(e => e.Organizador).FirstOrDefaultAsync(e => e.Id == id);
            if (evento == null)
                return NotFound();

            var estabaPublicado = evento.Estado == EstadoEvento.Aprobado;

            evento.Estado = EstadoEvento.Rechazado;
            evento.MotivoRechazo = motivo.Trim();
            evento.FechaModeracion = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            if (estabaPublicado)
                await _catalogo.InvalidarAsync();

            if (evento.Organizador?.Email != null)
            {
                var url = Url.Action("MisEventos", "Eventos", null, Request.Scheme)!;
                await _email.EnviarAsync(evento.Organizador.Email, $"Evento no aprobado: {evento.Titulo}",
                    EmailTemplates.EventoRechazado(evento, url));
            }

            TempData["Mensaje"] = $"Evento \"{evento.Titulo}\" rechazado.";
            return RedirectToAction(nameof(Index));
        }
    }
}
