using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ActivaFest.Models;
using ActivaFest.Services.Analitica;
using ActivaFest.Services.Catalogo;

namespace ActivaFest.Controllers
{
    // =============================
    // 📊 ANALÍTICA DEL ADMINISTRADOR (HU21 dashboard)
    // =============================
    // Solo el rol Administrador (criterio de HU21 y RNF07)
    [Authorize(Roles = Roles.Administrador)]
    public class AnaliticaController : Controller
    {
        private readonly IAnaliticaService _analitica;
        private readonly ICatalogoService _catalogo;
        private readonly IAsistenteAnaliticoService _ia;

        public AnaliticaController(IAnaliticaService analitica, ICatalogoService catalogo, IAsistenteAnaliticoService ia)
        {
            _analitica = analitica;
            _catalogo = catalogo;
            _ia = ia;
        }

        private static FiltroAnalitica Filtro(DateTime? desde, DateTime? hasta, int? categoriaId)
        {
            var mes = FiltroAnalitica.MesActual(DateTime.Today);
            return new FiltroAnalitica { Desde = desde ?? mes.Desde, Hasta = hasta ?? mes.Hasta, CategoriaId = categoriaId };
        }

        public async Task<IActionResult> Index(DateTime? desde, DateTime? hasta, int? categoriaId, CancellationToken ct)
        {
            ViewBag.Categorias = await _catalogo.ObtenerCategoriasAsync(ct);
            return View(await _analitica.DashboardAsync(Filtro(desde, hasta, categoriaId), ct));
        }

        // CSV para Excel en español: separador ";", coma decimal y BOM UTF-8
        public async Task<IActionResult> Exportar(DateTime? desde, DateTime? hasta, int? categoriaId, CancellationToken ct)
        {
            var d = await _analitica.DashboardAsync(Filtro(desde, hasta, categoriaId), ct);
            string Num(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
            string Pct(double? v) => v?.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') ?? "";

            var sb = new StringBuilder();
            sb.AppendLine($"Dashboard ActivaFest;{d.Desde:dd/MM/yyyy} - {d.Hasta:dd/MM/yyyy}");
            sb.AppendLine("Indicador;Valor");
            sb.AppendLine($"Ingresos (S/);{Num(d.Ingresos)}");
            sb.AppendLine($"Entradas vendidas;{d.EntradasVendidas}");
            sb.AppendLine($"Compras;{d.Compras}");
            sb.AppendLine($"Ticket promedio (S/);{Num(d.TicketPromedio)}");
            sb.AppendLine($"Ocupación (%);{Pct(d.Ocupacion)}");
            sb.AppendLine($"Asistencia (%);{Pct(d.Asistencia)}");
            sb.AppendLine($"Conversión (%);{Pct(d.Conversion)}");
            sb.AppendLine($"Comisiones (S/);{Num(d.Comisiones)}");
            sb.AppendLine($"Usuarios activos (90 días);{d.UsuariosActivos}");
            sb.AppendLine();
            sb.AppendLine("Semana;Ingresos (S/)");
            foreach (var s in d.IngresosPorSemana)
                sb.AppendLine($"{s.Etiqueta};{Num(s.Ingresos)}");
            sb.AppendLine();
            sb.AppendLine("Categoría;Entradas;Ingresos (S/)");
            foreach (var c in d.VentasPorCategoria)
                sb.AppendLine($"{Services.Comisiones.ComisionService.Celda(c.Categoria)};{c.Entradas};{Num(c.Ingresos)}");

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"dashboard-{d.Desde:yyyyMMdd}-{d.Hasta:yyyyMMdd}.csv");
        }

        // =============================
        // 👥 CLIENTES (HU22): ranking RFM y ficha por usuario
        // =============================
        private string AdministradorId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Ranking seudonimizado (código en lugar de nombre) con los filtros de HU29; la consulta también se audita (Ley 29733)
        public async Task<IActionResult> Clientes([FromQuery] FiltroClientes filtro, CancellationToken ct)
        {
            var clientes = await _analitica.FiltrarClientesAsync(filtro, ct);
            await _analitica.RegistrarAuditoriaAsync(AdministradorId, $"Filtro de clientes ({filtro.Descripcion()})", "Ordenes, Eventos", ct);

            ViewBag.Filtro = filtro;
            ViewBag.Categorias = await _catalogo.ObtenerCategoriasAsync(ct);
            return View(clientes);
        }

        // CSV del listado filtrado (solo códigos, sin nombres ni correos)
        public async Task<IActionResult> ExportarClientes([FromQuery] FiltroClientes filtro, CancellationToken ct)
        {
            var clientes = await _analitica.FiltrarClientesAsync(filtro, ct);
            await _analitica.RegistrarAuditoriaAsync(AdministradorId, $"Exportación de clientes ({filtro.Descripcion()})", "Ordenes, Eventos", ct);

            string Num(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');
            var sb = new StringBuilder();
            sb.AppendLine($"Clientes ActivaFest;{filtro.Descripcion()}");
            sb.AppendLine("Posición;Cliente;Segmento;Gasto (S/);Entradas;Compras;Última compra;Acepta promociones");
            for (var i = 0; i < clientes.Count; i++)
            {
                var c = clientes[i];
                sb.AppendLine($"{i + 1};{c.Codigo};{c.Segmento};{Num(c.Gasto)};{c.Entradas};{c.Compras};{c.UltimaCompra:dd/MM/yyyy};{(c.AceptaPromociones ? "Sí" : "No")}");
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"clientes-{DateTime.Now:yyyyMMdd}.csv");
        }

        public async Task<IActionResult> Cliente(string id, CancellationToken ct)
        {
            var ficha = await _analitica.FichaClienteAsync(id, AdministradorId, ct);
            return ficha == null ? NotFound() : View(ficha);
        }

        public async Task<IActionResult> Auditoria(CancellationToken ct) =>
            View(await _analitica.AuditoriaAsync(100, ct));

        // =============================
        // 📈 VARIABILIDAD Y SEMÁFORO DE DECISIÓN (HU23)
        // =============================
        public async Task<IActionResult> Variabilidad(DateTime? desde, DateTime? hasta, int? categoriaId, CancellationToken ct)
        {
            // Por defecto, las últimas 12 semanas: con un solo mes hay muy pocas semanas para medir la variabilidad
            var filtro = new FiltroAnalitica { Desde = desde ?? DateTime.Today.AddDays(-83), Hasta = hasta ?? DateTime.Today, CategoriaId = categoriaId };
            var reporte = await _analitica.VariabilidadAsync(filtro, ct);

            // Muestra clientes con gasto atípico (con código): la consulta se audita como el resto de la analítica individual
            await _analitica.RegistrarAuditoriaAsync(AdministradorId,
                $"Variabilidad y atípicos ({reporte.Desde:dd/MM/yyyy} - {reporte.Hasta:dd/MM/yyyy}{(categoriaId != null ? $", categoría {categoriaId}" : "")})",
                "Ordenes, Entradas, Eventos", ct);

            ViewBag.Categorias = await _catalogo.ObtenerCategoriasAsync(ct);
            return View(reporte);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Umbrales(UmbralesVariabilidad umbrales, DateTime? desde, DateTime? hasta, int? categoriaId, CancellationToken ct)
        {
            var error = await _analitica.GuardarUmbralesAsync(umbrales, AdministradorId, ct);
            if (error != null)
                TempData["Error"] = error;
            else
                TempData["Mensaje"] = "Umbrales del semáforo actualizados.";
            return RedirectToAction(nameof(Variabilidad), new { desde = desde?.ToString("yyyy-MM-dd"), hasta = hasta?.ToString("yyyy-MM-dd"), categoriaId });
        }

        // =============================
        // 🤖 CONSULTAS A LA IA (HU24): solo lectura, cifras fuente y auditoría
        // =============================
        public record PreguntaIa(string Pregunta);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConsultarIA([FromBody] PreguntaIa datos, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(datos?.Pregunta))
                return BadRequest("Escribe una pregunta.");

            var r = await _ia.ConsultarAsync(datos.Pregunta, AdministradorId, ct);
            return Ok(new
            {
                texto = r.Texto,
                funcion = r.Funcion,
                periodo = r.Periodo,
                fueraDeAlcance = r.FueraDeAlcance,
                redactadaPorIa = r.RedactadaPorIa,
                tabla = r.Tabla == null ? null : new { titulo = r.Tabla.Titulo, columnas = r.Tabla.Columnas, filas = r.Tabla.Filas },
                grafico = r.Grafico == null || r.Grafico.Valores.Count == 0 ? null : new { tipo = r.Grafico.Tipo, serie = r.Grafico.Serie, etiquetas = r.Grafico.Etiquetas, valores = r.Grafico.Valores },
                sugerencias = r.Sugerencias
            });
        }
    }
}
