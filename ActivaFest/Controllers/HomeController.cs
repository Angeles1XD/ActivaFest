using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;

namespace ActivaFest.Controllers;

public class HomeController : Controller
{
    private const int EventosEnPortada = 4;
    private const int CategoriasEnPortada = 6;

    private readonly ICatalogoService _catalogo;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(ICatalogoService catalogo, UserManager<ApplicationUser> userManager)
    {
        _catalogo = catalogo;
        _userManager = userManager;
    }

    // =============================
    // 🏠 PÁGINA DE BIENVENIDA
    // =============================
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var categorias = await _catalogo.ObtenerCategoriasAsync(ct);
        var conteo = await _catalogo.ContarPorCategoriaAsync(ct);
        var proximos = await _catalogo.BuscarAsync(new CatalogoFiltro(), ct);

        var modelo = new InicioViewModel
        {
            Proximos = proximos.Eventos.Take(EventosEnPortada).ToList(),
            Destacados = await _catalogo.DestacadosAsync(EventosEnPortada, ct),
            TotalEventos = conteo.Values.Sum(),
            // Primero las categorías con más eventos; con la base vacía se mantiene el orden alfabético
            Categorias = categorias
                .Select(c => new CategoriaResumen { Categoria = c, Cantidad = conteo.GetValueOrDefault(c.Id) })
                .OrderByDescending(c => c.Cantidad)
                .Take(CategoriasEnPortada)
                .ToList()
        };

        if (User.Identity?.IsAuthenticated == true)
            modelo.NombreUsuario = (await _userManager.GetUserAsync(User))?.NombreCompleto;

        return View(modelo);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
