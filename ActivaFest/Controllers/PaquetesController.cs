using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;

namespace ActivaFest.Controllers
{
    // =============================
    // ⭐ ADMINISTRACIÓN DE PAQUETES PUBLICITARIOS (módulo de administración)
    // =============================
    // Los cambios aplican a contratos nuevos: los vendidos conservan su precio y vigencia.
    [Authorize(Roles = Roles.Administrador)]
    public class PaquetesController : Controller
    {
        private readonly ApplicationDbContext _db;

        public PaquetesController(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(CancellationToken ct)
        {
            ViewBag.Ventas = await _db.ContratosDestacado
                .Where(c => c.Estado == EstadoContrato.Activo)
                .GroupBy(c => c.PaqueteId)
                .Select(g => new { g.Key, Cantidad = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Cantidad, ct);

            return View(await _db.PaquetesDestacado.AsNoTracking().OrderBy(p => p.Dias).ToListAsync(ct));
        }

        public async Task<IActionResult> Editar(int? id, CancellationToken ct)
        {
            if (id == null)
                return View(new PaqueteDestacado { Dias = 7, Precio = 49.90m, Prioridad = 1 });

            var paquete = await _db.PaquetesDestacado.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
            return paquete == null ? NotFound() : View(paquete);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Editar(int? id, [Bind("Nombre,Descripcion,Dias,Precio,Prioridad,Activo")] PaqueteDestacado datos, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                datos.Id = id ?? 0;
                return View(datos);
            }

            if (id == null)
            {
                _db.PaquetesDestacado.Add(datos);
            }
            else
            {
                var paquete = await _db.PaquetesDestacado.FirstOrDefaultAsync(p => p.Id == id, ct);
                if (paquete == null)
                    return NotFound();

                paquete.Nombre = datos.Nombre.Trim();
                paquete.Descripcion = datos.Descripcion?.Trim() ?? "";
                paquete.Dias = datos.Dias;
                paquete.Precio = Math.Round(datos.Precio, 2);
                paquete.Prioridad = datos.Prioridad;
                paquete.Activo = datos.Activo;
            }

            await _db.SaveChangesAsync(ct);
            TempData["Mensaje"] = $"Paquete \"{datos.Nombre}\" guardado.";
            return RedirectToAction(nameof(Index));
        }
    }
}
