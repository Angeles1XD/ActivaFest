using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Analitica;
using ActivaFest.Services.Entradas;
using static ActivaFest.Tests.Formularios;

namespace ActivaFest.Tests;

// Escenario de ventas controlado para la analítica (HU21, HU22, HU29)
public class EscenarioVentas : IDisposable
{
    public readonly SqliteConnection Conexion;
    public readonly ApplicationDbContext Db;
    public readonly DateTime Desde = new(2026, 11, 1), Hasta = new(2026, 11, 30);

    public EscenarioVentas()
    {
        Conexion = new SqliteConnection("Data Source=:memory:");
        Conexion.Open();
        Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(Conexion).Options);
        Db.Database.EnsureCreated();
        CompraServiceTests.CrearUsuarios(Db, "org", "ana", "beto", "carla");
    }

    public void Dispose()
    {
        Db.Dispose();
        Conexion.Dispose();
    }

    public Evento Evento(string titulo, int categoria, DateTime fecha, int capacidad, decimal precio, int visitas = 0)
    {
        var e = new Evento
        {
            Titulo = titulo, Descripcion = "Evento de analítica.", CategoriaId = categoria, Ubicacion = "Arequipa", Fecha = fecha,
            Estado = EstadoEvento.Aprobado, OrganizadorId = "org", Aforo = capacidad, Visitas = visitas,
            TiposEntrada = { new TipoEntrada { Nombre = "General", Precio = precio, Capacidad = capacidad } }
        };
        Db.Eventos.Add(e);
        Db.SaveChanges();
        return e;
    }

    // Compra pagada en una fecha local, con entradas emitidas (las primeras "usadas" quedan validadas en puerta)
    public Orden Compra(string usuario, Evento evento, int cantidad, DateTime pagoLocal, int usadas = 0, EstadoOrden estado = EstadoOrden.Pagada)
    {
        var zona = evento.TiposEntrada.First();
        var orden = new Orden
        {
            Codigo = Guid.NewGuid().ToString("N")[..12], UsuarioId = usuario, EventoId = evento.Id, Estado = estado,
            Total = zona.Precio * cantidad, PorcentajeComision = 8, Comision = Math.Round(zona.Precio * cantidad * 0.08m, 2),
            FechaPago = pagoLocal.ToUniversalTime(), FechaCreacion = pagoLocal.ToUniversalTime(), FechaExpiracion = pagoLocal.ToUniversalTime(),
            Items = { new OrdenItem { TipoEntradaId = zona.Id, Cantidad = cantidad, PrecioUnitario = zona.Precio } }
        };
        Db.Ordenes.Add(orden);
        Db.SaveChanges();
        if (estado == EstadoOrden.Pagada)
        {
            for (var i = 0; i < cantidad; i++)
                Db.Entradas.Add(new Entrada
                {
                    Codigo = CodigoSeguro.Generar(), OrdenId = orden.Id, EventoId = evento.Id, TipoEntradaId = zona.Id, UsuarioId = usuario,
                    Estado = i < usadas ? EstadoEntrada.Usada : EstadoEntrada.Activa, FechaEmision = pagoLocal.ToUniversalTime()
                });
            Db.SaveChanges();
        }
        return orden;
    }
}

// =============================
// 📊 HU21: DASHBOARD DE INDICADORES (pruebas del servicio)
// =============================
public class HU21DashboardTests : IDisposable
{
    private readonly EscenarioVentas _e = new();
    private readonly AnaliticaService _analitica;

    public HU21DashboardTests()
    {
        _analitica = new AnaliticaService(_e.Db, new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
    }

    public void Dispose() => _e.Dispose();

    private void Escenario()
    {
        // Noviembre: un concierto ya realizado (con asistencia) y una obra; octubre: ventas del período anterior
        var concierto = _e.Evento("Rock", 1, new DateTime(2026, 11, 10, 20, 0, 0), capacidad: 100, precio: 50, visitas: 40);
        var obra = _e.Evento("Hamlet", 2, new DateTime(2026, 11, 25, 20, 0, 0), capacidad: 50, precio: 80, visitas: 10);
        _e.Compra("ana", concierto, 4, new DateTime(2026, 11, 2, 12, 0, 0), usadas: 3);   // 200
        _e.Compra("beto", concierto, 2, new DateTime(2026, 11, 5, 9, 0, 0), usadas: 2);   // 100
        _e.Compra("carla", obra, 3, new DateTime(2026, 11, 20, 18, 0, 0));               // 240
        _e.Compra("ana", obra, 1, new DateTime(2026, 10, 15, 10, 0, 0));                 // octubre: 80
        _e.Compra("beto", concierto, 5, new DateTime(2026, 11, 3, 10, 0, 0), estado: EstadoOrden.Expirada); // no cuenta
    }

    // Criterio: muestra ingresos, entradas vendidas, ticket promedio, ocupación, asistencia y comisiones,
    // y las cifras coinciden con consultas de control sobre la base de datos
    [Fact]
    public async Task Los_indicadores_coinciden_con_las_consultas_de_control()
    {
        Escenario();
        var d = await _analitica.DashboardAsync(new FiltroAnalitica { Desde = _e.Desde, Hasta = _e.Hasta });

        // Consultas de control (independientes del servicio)
        var pagadas = _e.Db.Ordenes.AsNoTracking().Where(o => o.Estado == EstadoOrden.Pagada).AsEnumerable()
            .Where(o => o.FechaPago!.Value.ToLocalTime() >= _e.Desde && o.FechaPago.Value.ToLocalTime() < _e.Hasta.AddDays(1)).ToList();
        Assert.Equal(pagadas.Sum(o => o.Total), d.Ingresos);                                    // 540
        Assert.Equal(_e.Db.OrdenItems.AsEnumerable().Where(i => pagadas.Any(o => o.Id == i.OrdenId)).Sum(i => i.Cantidad), d.EntradasVendidas); // 9
        Assert.Equal(Math.Round(pagadas.Sum(o => o.Total) / pagadas.Count, 2), d.TicketPromedio); // 180
        Assert.Equal(pagadas.Sum(o => o.Comision), d.Comisiones);                                // 43.2
        Assert.Equal((540m, 9, 180m, 43.2m), (d.Ingresos, d.EntradasVendidas, d.TicketPromedio, d.Comisiones));

        // Ocupación: vendidas / aforo de los eventos de noviembre = 10 / 150
        Assert.Equal(Math.Round(100.0 * 10 / 150, 1), d.Ocupacion);
        // Asistencia: solo eventos ya realizados (el concierto del 10/11, si ya pasó): 5 usadas de 6
        if (new DateTime(2026, 11, 10, 20, 0, 0) <= DateTime.Now)
            Assert.Equal(Math.Round(100.0 * 5 / 6, 1), d.Asistencia);
        // Conversión: compras pagadas / visitas a las fichas = 4 / 50
        Assert.Equal(8.0, d.Conversion);

        // Período anterior (octubre): 80 soles, 1 entrada
        Assert.Equal((80m, 1), (d.IngresosAnterior, d.EntradasAnterior));
        Assert.Equal(575.0, DashboardAnalitica.Variacion(d.Ingresos, d.IngresosAnterior));
    }

    [Fact]
    public async Task Se_filtra_por_categoria()
    {
        Escenario();

        var teatro = await _analitica.DashboardAsync(new FiltroAnalitica { Desde = _e.Desde, Hasta = _e.Hasta, CategoriaId = 2 });

        Assert.Equal((240m, 3, 1), (teatro.Ingresos, teatro.EntradasVendidas, teatro.Compras));
        Assert.Equal(8.0, teatro.Ocupacion); // 4 vendidas (3 en noviembre + 1 en octubre) / 50 lugares
    }

    [Fact]
    public async Task Ventas_por_categoria_e_ingresos_por_semana_suman_lo_mismo_que_el_total()
    {
        Escenario();

        var d = await _analitica.DashboardAsync(new FiltroAnalitica { Desde = _e.Desde, Hasta = _e.Hasta });

        Assert.Equal(new[] { ("Conciertos", 6, 300m), ("Teatro", 3, 240m) }, d.VentasPorCategoria.Select(c => (c.Categoria, c.Entradas, c.Ingresos)));
        Assert.Equal(AnaliticaService.SemanasTendencia, d.IngresosPorSemana.Count);
        Assert.Equal(540m + 80m, d.IngresosPorSemana.Sum(s => s.Ingresos)); // las 10 semanas incluyen la venta de octubre
        Assert.Equal(3, d.UsuariosActivos);
    }

    // Caché de 5 minutos mientras los datos no cambian; una compra nueva se ve al instante (tiempo real)
    [Fact]
    public async Task El_dashboard_usa_cache_pero_una_compra_nueva_se_ve_al_instante()
    {
        Escenario();
        var filtro = new FiltroAnalitica { Desde = _e.Desde, Hasta = _e.Hasta };
        var primero = await _analitica.DashboardAsync(filtro);

        var repetido = await _analitica.DashboardAsync(filtro);
        Assert.Equal(primero.CalculadoEn, repetido.CalculadoEn);   // sin cambios: sale de la caché

        _e.Compra("carla", _e.Db.Eventos.First(), 1, new DateTime(2026, 11, 6, 10, 0, 0));
        var despues = await _analitica.DashboardAsync(filtro);

        Assert.Equal(primero.Ingresos + 50m, despues.Ingresos);
        Assert.Contains(despues.UltimasCompras, c => c.Fecha == new DateTime(2026, 11, 6, 10, 0, 0) && c.Evento == "Rock");
    }

    [Fact]
    public async Task Muestra_las_ultimas_compras_del_periodo_con_codigo_anonimo()
    {
        Escenario();

        var d = await _analitica.DashboardAsync(new FiltroAnalitica { Desde = _e.Desde, Hasta = _e.Hasta });

        Assert.Equal(new[] { ("Hamlet", 3, 240m), ("Rock", 2, 100m), ("Rock", 4, 200m) }, d.UltimasCompras.Select(c => (c.Evento, c.Entradas, c.Total)));
        Assert.Equal(ClienteAnalitica.CodigoDe("carla"), d.UltimasCompras[0].Cliente);
        Assert.DoesNotContain(d.UltimasCompras, c => c.Cliente.Contains("carla"));
    }

    [Fact]
    public async Task La_ocupacion_incluye_eventos_futuros_que_vendieron_en_el_periodo()
    {
        // Compra en noviembre para un evento de enero: antes no contaba en la ocupación de noviembre
        var futuro = _e.Evento("Año Nuevo", 1, new DateTime(2027, 1, 10, 22, 0, 0), capacidad: 20, precio: 40);
        _e.Compra("ana", futuro, 5, new DateTime(2026, 11, 8, 12, 0, 0));

        var d = await _analitica.DashboardAsync(new FiltroAnalitica { Desde = _e.Desde, Hasta = _e.Hasta });

        Assert.Equal(1, d.EventosConsiderados);
        Assert.Equal(25.0, d.Ocupacion);   // 5 de 20 lugares
        Assert.Equal(0, d.EventosRealizados);
        Assert.Null(d.Asistencia);
    }

    [Theory]
    [InlineData("hoy", "2026-10-27", "2026-10-27")]
    [InlineData("7d", "2026-10-21", "2026-10-27")]
    [InlineData("30d", "2026-09-28", "2026-10-27")]
    [InlineData("mes", "2026-10-01", "2026-10-27")]
    [InlineData("mespasado", "2026-09-01", "2026-09-30")]
    [InlineData("anio", "2026-01-01", "2026-10-27")]
    [InlineData(null, "2026-10-01", "2026-10-27")]
    public void Los_periodos_rapidos_dan_fechas_claras(string? rango, string desde, string hasta)
    {
        var (f, r) = FiltroAnalitica.DeRango(rango, null, null, null, new DateTime(2026, 10, 27));
        Assert.Equal((DateTime.Parse(desde), DateTime.Parse(hasta)), (f.Desde, f.Hasta));
        Assert.Equal(rango ?? "mes", r);

        var (p, rp) = FiltroAnalitica.DeRango(null, new DateTime(2026, 5, 1), new DateTime(2026, 5, 15), 2, new DateTime(2026, 10, 27));
        Assert.Equal(("personalizado", new DateTime(2026, 5, 1), 2), (rp, p.Desde, p.CategoriaId));
    }

    // RNF02: carga en menos de 3 segundos (sin caché, con miles de compras)
    [Fact]
    public async Task Carga_en_menos_de_3_segundos_con_miles_de_compras()
    {
        var evento = _e.Evento("Masivo", 1, new DateTime(2026, 11, 20, 20, 0, 0), capacidad: 10000, precio: 30, visitas: 50000);
        var zona = evento.TiposEntrada.First();
        _e.Db.Ordenes.AddRange(Enumerable.Range(0, 3000).Select(i => new Orden
        {
            Codigo = $"M{i:D6}", UsuarioId = new[] { "ana", "beto", "carla" }[i % 3], EventoId = evento.Id, Estado = EstadoOrden.Pagada,
            Total = 30, Comision = 2.4m, FechaPago = new DateTime(2026, 11, 1 + i % 28, 12, 0, 0).ToUniversalTime(), FechaExpiracion = DateTime.UtcNow,
            Items = { new OrdenItem { TipoEntradaId = zona.Id, Cantidad = 1, PrecioUnitario = 30 } }
        }));
        _e.Db.SaveChanges();

        var reloj = Stopwatch.StartNew();
        var d = await _analitica.DashboardAsync(new FiltroAnalitica { Desde = _e.Desde, Hasta = _e.Hasta });
        reloj.Stop();

        Assert.Equal(90000m, d.Ingresos);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(3), $"Tardó {reloj.Elapsed.TotalMilliseconds:N0} ms");
    }
}

// =============================
// 📊 HU21: ACCESO Y VISTA DEL DASHBOARD (pruebas de integración)
// =============================
public class HU21DashboardVistaTests : IClassFixture<AppDePrueba>
{
    private readonly AppDePrueba _app;

    public HU21DashboardVistaTests(AppDePrueba app)
    {
        _app = app;
    }

    [Fact]
    public async Task Solo_el_administrador_accede_al_dashboard()
    {
        var organizador = _app.Cliente();
        await RegistrarAsync(organizador, Correo(), rol: Roles.Organizador);
        Assert.StartsWith("/Auth/AccesoDenegado", (await organizador.GetAsync("/Analitica")).Headers.Location!.PathAndQuery);
        Assert.StartsWith("/Auth/Login", (await _app.Cliente().GetAsync("/Analitica")).Headers.Location!.PathAndQuery);

        var admin = _app.Cliente();
        await IniciarSesionAsync(admin, "admin@test.com", "Admin123!");
        await admin.GetAsync("/Analitica");   // calentamiento: la primera visita compila la vista (no es lo que ve el usuario después)
        var reloj = Stopwatch.StartNew();
        var html = await TextoAsync(await admin.GetAsync("/Analitica?desde=2026-11-01&hasta=2026-11-30"));
        reloj.Stop();

        foreach (var indicador in new[] { "Ingresos", "Entradas vendidas", "Ticket promedio", "Ocupación", "Asistencia", "Comisiones" })
            Assert.Contains($"<span class=\"kpi-etiqueta\">{indicador}</span>", html);
        Assert.Contains("chart.js@4.4.1", html);
        Assert.Contains("🧾 Últimas compras", html);
        Assert.Contains("Mostrando <b>personalizado</b>: compras pagadas del <b>01/11/2026</b> al <b>30/11/2026</b>", html);

        // Datos en vivo (los pide la página cada 20 s): solo el administrador
        var datos = await admin.GetAsync("/Analitica/Datos?rango=mes");
        Assert.Equal("application/json", datos.Content.Headers.ContentType!.MediaType);
        var json = System.Text.Json.JsonDocument.Parse(await datos.Content.ReadAsStringAsync()).RootElement;
        foreach (var campo in new[] { "ingresos", "entradas", "ticket", "ocupacion", "asistencia", "comisiones", "semanas", "categorias", "ultimas" })
            Assert.True(json.TryGetProperty(campo, out _), campo);
        Assert.StartsWith("/Auth/AccesoDenegado", (await organizador.GetAsync("/Analitica/Datos")).Headers.Location!.PathAndQuery);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(3));

        var csv = await admin.GetAsync("/Analitica/Exportar?desde=2026-11-01&hasta=2026-11-30");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
    }
}
