using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Email;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace ActivaFest.Controllers
{
    public class EventosController : Controller
    {
        public const string CacheKey = "eventos_lista";

        private readonly ApplicationDbContext _context;
        private readonly IDistributedCache _cache;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _email;

        public EventosController(
            ApplicationDbContext context,
            IDistributedCache cache,
            UserManager<ApplicationUser> userManager,
            IEmailService email)
        {
            _context = context;
            _cache = cache;
            _userManager = userManager;
            _email = email;
        }

        // =============================
        // 🔍 LISTAR + BUSCAR EVENTOS (solo aprobados)
        // =============================
        public async Task<IActionResult> Index(string? buscar)
        {
            var aprobados = _context.Eventos.Where(e => e.Estado == EstadoEvento.Aprobado);

            if (!string.IsNullOrEmpty(buscar))
            {
                var eventosFiltrados = await aprobados
                    .Where(e =>
                        e.Titulo.Contains(buscar) ||
                        e.Categoria.Contains(buscar) ||
                        e.Ubicacion.Contains(buscar))
                    .ToListAsync();

                return View(eventosFiltrados);
            }

            var cachedData = await _cache.GetStringAsync(CacheKey);

            List<Evento> eventos;

            if (!string.IsNullOrEmpty(cachedData))
            {
                Console.WriteLine("🔥 CACHE HIT");
                eventos = JsonSerializer.Deserialize<List<Evento>>(cachedData)!;
            }
            else
            {
                Console.WriteLine("💾 CACHE MISS");

                eventos = await aprobados.ToListAsync();

                var options = new DistributedCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                    .SetSlidingExpiration(TimeSpan.FromMinutes(2));

                await _cache.SetStringAsync(
                    CacheKey,
                    JsonSerializer.Serialize(eventos),
                    options
                );
            }

            return View(eventos);
        }

        // =============================
        // 📝 CREAR EVENTO (queda pendiente de moderación)
        // =============================
        [Authorize(Roles = Roles.Organizador + "," + Roles.Administrador)]
        public IActionResult Crear()
        {
            return View(new Evento { Fecha = DateTime.Today.AddDays(7).AddHours(20) });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = Roles.Organizador + "," + Roles.Administrador)]
        public async Task<IActionResult> Crear([Bind("Titulo,Categoria,Ubicacion,Fecha,Asistentes")] Evento evento)
        {
            if (!ModelState.IsValid)
                return View(evento);

            var usuario = await _userManager.GetUserAsync(User);

            evento.OrganizadorId = usuario?.Id;
            evento.Estado = EstadoEvento.Pendiente;
            evento.FechaCreacion = DateTime.UtcNow;

            _context.Add(evento);
            await _context.SaveChangesAsync();

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
        // 📋 MIS EVENTOS (estado de moderación del organizador)
        // =============================
        [Authorize(Roles = Roles.Organizador + "," + Roles.Administrador)]
        public async Task<IActionResult> MisEventos()
        {
            var userId = _userManager.GetUserId(User);

            var eventos = await _context.Eventos
                .Where(e => e.OrganizadorId == userId)
                .OrderByDescending(e => e.FechaCreacion)
                .ToListAsync();

            return View(eventos);
        }

        // =============================
        // 🔥 DETALLE (VER MÁS)
        // =============================
        public async Task<IActionResult> Detalle(int id)
        {
            var evento = await _context.Eventos.FirstOrDefaultAsync(e => e.Id == id);

            if (evento == null)
                return NotFound();

            // Los eventos no aprobados solo los ve su organizador o un administrador
            if (evento.Estado != EstadoEvento.Aprobado
                && !User.IsInRole(Roles.Administrador)
                && evento.OrganizadorId != _userManager.GetUserId(User))
                return NotFound();

            return View(evento);
        }
    }
}
