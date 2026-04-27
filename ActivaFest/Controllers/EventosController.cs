using Microsoft.AspNetCore.Mvc;
using ActivaFest.Data;
using ActivaFest.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace ActivaFest.Controllers
{
    public class EventosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IDistributedCache _cache;

        public EventosController(ApplicationDbContext context, IDistributedCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // =============================
        // 🔍 LISTAR + BUSCAR EVENTOS
        // =============================
        public async Task<IActionResult> Index(string? buscar)
        {
            if (!string.IsNullOrEmpty(buscar))
            {
                var eventosFiltrados = _context.Eventos
                    .Where(e =>
                        e.Titulo!.Contains(buscar) ||
                        e.Categoria!.Contains(buscar) ||
                        e.Ubicacion!.Contains(buscar))
                    .ToList();

                return View(eventosFiltrados);
            }

            string cacheKey = "eventos_lista";

            var cachedData = await _cache.GetStringAsync(cacheKey);

            List<Evento> eventos;

            if (!string.IsNullOrEmpty(cachedData))
            {
                Console.WriteLine("🔥 CACHE HIT");
                eventos = JsonSerializer.Deserialize<List<Evento>>(cachedData)!;
            }
            else
            {
                Console.WriteLine("💾 CACHE MISS");

                eventos = _context.Eventos.ToList();

                var options = new DistributedCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                    .SetSlidingExpiration(TimeSpan.FromMinutes(2));

                await _cache.SetStringAsync(
                    cacheKey,
                    JsonSerializer.Serialize(eventos),
                    options
                );
            }

            return View(eventos);
        }

        // =============================
        // 📝 CREAR EVENTO
        // =============================
        public IActionResult Crear()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Crear(Evento evento)
        {
            if (!ModelState.IsValid)
                return View(evento);

            _context.Add(evento);
            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("UltimoEvento", evento.Titulo ?? "");

            string cacheKey = "eventos_lista";

            var eventos = _context.Eventos.ToList();

            var options = new DistributedCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                .SetSlidingExpiration(TimeSpan.FromMinutes(2));

            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(eventos),
                options
            );

            return RedirectToAction(nameof(Index));
        }

        // =============================
        // 🔥 DETALLE (VER MÁS)
        // =============================
        public IActionResult Detalle(int id)
        {
            var evento = _context.Eventos.FirstOrDefault(e => e.Id == id);

            if (evento == null)
                return NotFound();

            return View(evento);
        }
    }
}