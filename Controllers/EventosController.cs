using ActivaFest.Data;
using ActivaFest.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

public class EventosController : Controller
{
    private readonly ApplicationDbContext _context;

    public EventosController(ApplicationDbContext context)
    {
        _context = context;
    }

    // 🟢 LISTA + CACHE
    /*/public async Task<IActionResult> Index()
    {
        var cache = HttpContext.RequestServices.GetService<IDistributedCache>();

        var data = await cache.GetStringAsync("eventos");

        List<Evento> eventos;

        if (data == null)
        {
            eventos = await _context.Eventos
                .Include(e => e.Categoria)
                .ToListAsync();

            data = JsonSerializer.Serialize(eventos);

            await cache.SetStringAsync("eventos", data,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
                });
        }
        else
        {
            eventos = JsonSerializer.Deserialize<List<Evento>>(data);
        }

        return View(eventos);
    }
/*/
public async Task<IActionResult> Index()
{
    var eventos = await _context.Eventos
        .Include(e => e.Categoria)
        .ToListAsync();

    return View(eventos);
}
    // 🟢 CREATE (GET)
    public IActionResult Create()
    {
        ViewBag.Categorias = new SelectList(_context.Categorias, "Id", "Nombre");
        return View();
    }

    // 🟢 CREATE (POST)
    [HttpPost]
    public async Task<IActionResult> Create(Evento evento)
    {
        if (evento.Fecha < DateTime.Now)
        {
            ModelState.AddModelError("", "No puedes crear eventos en el pasado");
        }

        if (evento.Precio <= 0)
        {
            ModelState.AddModelError("", "El precio debe ser mayor a 0");
        }

        if (ModelState.IsValid)
        {
            _context.Add(evento);
            await _context.SaveChangesAsync();

            // 🔴 limpiar cache para que se actualice la lista
            var cache = HttpContext.RequestServices.GetService<IDistributedCache>();
            await cache.RemoveAsync("eventos");

            return RedirectToAction(nameof(Index));
        }

        // 🔁 volver a cargar categorías si falla
        ViewBag.Categorias = new SelectList(_context.Categorias, "Id", "Nombre", evento.CategoriaId);

        return View(evento);
    }

    // 🟢 DETAILS
    public async Task<IActionResult> Details(int id)
    {
        var evento = await _context.Eventos
            .Include(e => e.Categoria)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (evento == null)
            return NotFound();

        // 🧠 guardar último evento visto
        HttpContext.Session.SetInt32("UltimoEvento", evento.Id);

        return View(evento);
    }

    // 🟢 DELETE (BÁSICO)
    public async Task<IActionResult> Delete(int id)
    {
        var evento = await _context.Eventos.FindAsync(id);

        if (evento != null)
        {
            _context.Eventos.Remove(evento);
            await _context.SaveChangesAsync();

            var cache = HttpContext.RequestServices.GetService<IDistributedCache>();
            await cache.RemoveAsync("eventos");
        }

        return RedirectToAction(nameof(Index));
    }
    [Authorize]
public async Task<IActionResult> Comprar(int id)
{
    var evento = await _context.Eventos.FindAsync(id);

    if (evento == null)
        return NotFound();

    return View(evento);
}
[HttpPost]
[Authorize]
public async Task<IActionResult> Comprar(int id, int cantidad)
{
    var evento = await _context.Eventos.FindAsync(id);

    if (evento == null || evento.CuposDisponibles < cantidad)
        return BadRequest("No hay cupos suficientes");

    evento.CuposDisponibles -= cantidad;

    var compra = new Compra
    {
        EventoId = id,
        Cantidad = cantidad,
        Total = evento.Precio * cantidad,
        FechaCompra = DateTime.Now,
        UsuarioId = User.FindFirstValue(ClaimTypes.NameIdentifier)
    };

    _context.Compras.Add(compra);
    await _context.SaveChangesAsync();

    return RedirectToAction("Index");
}
}