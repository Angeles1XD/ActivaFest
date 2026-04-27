using Microsoft.AspNetCore.Mvc;
using ActivaFest.Data;
using ActivaFest.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering; // 🔥 IMPORTANTE

namespace ActivaFest.Controllers
{
    public class EventosController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public EventosController(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        // =============================
        // 🔍 LISTAR + BUSCAR EVENTOS
        // =============================
        public IActionResult Index(string? buscar)
        {
            if (!string.IsNullOrEmpty(buscar))
            {
                var eventosFiltrados = _context.Eventos
                    .Include(e => e.Categoria)
                    .Where(e =>
                        e.Titulo.Contains(buscar) ||
                        (e.Categoria != null && e.Categoria.Nombre.Contains(buscar)) ||
                        e.Ubicacion.Contains(buscar))
                    .ToList();

                return View(eventosFiltrados);
            }

            if (!_cache.TryGetValue("eventos", out List<Evento>? eventos))
            {
                eventos = _context.Eventos
                    .Include(e => e.Categoria)
                    .ToList();

                _cache.Set("eventos", eventos, TimeSpan.FromMinutes(5));
            }

            return View(eventos);
        }

        // =============================
        // 📝 CREAR EVENTO
        // =============================
        public IActionResult Crear()
        {
            ViewBag.Categorias = _context.Categorias
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Nombre
                }).ToList();

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Crear(Evento evento)
        {
            if (!ModelState.IsValid)
            {
                // 🔁 IMPORTANTE repetir esto si hay error
                ViewBag.Categorias = _context.Categorias
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Nombre
                    }).ToList();

                return View(evento);
            }

            _context.Add(evento);
            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("UltimoEvento", evento.Titulo ?? "");
            _cache.Remove("eventos");

            return RedirectToAction(nameof(Index));
        }

        // =============================
        // 🔥 DETALLE (VER MÁS)
        // =============================
        public IActionResult Detalle(int id)
        {
            var evento = _context.Eventos
                .Include(e => e.Categoria)
                .FirstOrDefault(e => e.Id == id);

            if (evento == null)
                return NotFound();

            return View(evento);
        }
    }
}