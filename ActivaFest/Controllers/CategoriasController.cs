using Microsoft.AspNetCore.Mvc;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;

namespace ActivaFest.Controllers
{
    // =============================
    // 🗂️ EXPLORA POR CATEGORÍAS (RF02)
    // =============================
    public class CategoriasController : Controller
    {
        private readonly ICatalogoService _catalogo;

        public CategoriasController(ICatalogoService catalogo)
        {
            _catalogo = catalogo;
        }

        public async Task<IActionResult> Index([FromQuery] CatalogoFiltro filtro, CancellationToken ct)
        {
            if (!ModelState.IsValid)
                filtro = new CatalogoFiltro();

            var categorias = await _catalogo.ObtenerCategoriasAsync(ct);
            var conteo = await _catalogo.ContarPorCategoriaAsync(ct);

            // Una categoría inexistente en la URL se ignora en lugar de mostrar una página vacía
            var seleccionada = categorias.FirstOrDefault(c => c.Id == filtro.CategoriaId);
            if (seleccionada == null)
                filtro.CategoriaId = null;

            var modelo = new CategoriasViewModel
            {
                Filtro = filtro,
                Categorias = categorias,
                Seleccionada = seleccionada,
                Resultado = await _catalogo.BuscarAsync(filtro, ct),
                TotalEventos = conteo.Values.Sum(),
                Resumen = categorias
                    .Select(c => new CategoriaResumen { Categoria = c, Cantidad = conteo.GetValueOrDefault(c.Id) })
                    .ToList()
            };

            return View(modelo);
        }
    }
}
