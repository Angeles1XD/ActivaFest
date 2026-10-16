using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ActivaFest.Controllers;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;

namespace ActivaFest.Tests;

// =============================
// ⏱️ EVENTOS PASADOS: reglas y moderación
// =============================
public class EventosPasadosTests
{
    private static readonly DateTime Inicio = new(2026, 11, 20, 20, 0, 0);

    [Fact]
    public void Un_evento_ocurre_al_llegar_su_fecha()
    {
        var evento = new Evento { Fecha = Inicio };

        Assert.False(evento.YaOcurrio(Inicio.AddMinutes(-1)));
        Assert.True(evento.YaOcurrio(Inicio));
        Assert.True(evento.YaOcurrio(Inicio.AddDays(3)));
    }

    [Theory]
    [InlineData(-48, true)]   // comprada con anticipación
    [InlineData(0, true)]     // al empezar el evento
    [InlineData(11, true)]    // rezagados / eventos largos
    [InlineData(12, true)]    // límite exacto
    [InlineData(13, false)]   // el evento ya terminó
    public void La_entrada_activa_vale_hasta_unas_horas_despues_del_inicio(int horasDesdeInicio, bool vigente)
    {
        var evento = new Evento { Fecha = Inicio };
        var entrada = new Entrada { Estado = EstadoEntrada.Activa };

        Assert.Equal(vigente, entrada.VigenteParaIngreso(evento, Inicio.AddHours(horasDesdeInicio)));
    }

    [Theory]
    [InlineData(EstadoEntrada.Usada)]
    [InlineData(EstadoEntrada.Anulada)]
    public void Una_entrada_usada_o_anulada_nunca_es_vigente(EstadoEntrada estado)
    {
        var evento = new Evento { Fecha = Inicio };

        Assert.False(new Entrada { Estado = estado }.VigenteParaIngreso(evento, Inicio.AddHours(-1)));
    }
}

public class ModeracionEventosPasadosTests : IDisposable
{
    private class TempDataFalso : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private readonly SqliteConnection _conexion;
    private readonly ApplicationDbContext _db;
    private readonly EmailFalso _email = new();
    private readonly AdminController _admin;

    public ModeracionEventosPasadosTests()
    {
        _conexion = new SqliteConnection("Data Source=:memory:");
        _conexion.Open();
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_conexion).Options);
        _db.Database.EnsureCreated();

        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var catalogo = new CatalogoService(_db, cache, NullLogger<CatalogoService>.Instance);

        var http = new DefaultHttpContext();
        _admin = new AdminController(_db, catalogo, _email)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new TempDataFalso())
        };
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexion.Dispose();
    }

    [Fact]
    public async Task No_se_puede_aprobar_un_evento_cuya_fecha_ya_paso()
    {
        var evento = new Evento
        {
            Titulo = "Fiesta de ayer",
            CategoriaId = 1,
            Ubicacion = "Lima",
            Fecha = DateTime.Now.AddDays(-1),
            Estado = EstadoEvento.Pendiente
        };
        _db.Eventos.Add(evento);
        _db.SaveChanges();

        var resultado = await _admin.Aprobar(evento.Id);

        Assert.IsType<RedirectToActionResult>(resultado);
        Assert.Equal(EstadoEvento.Pendiente, _db.Eventos.AsNoTracking().Single(e => e.Id == evento.Id).Estado);
        Assert.Contains("ya pasó", (string)_admin.TempData["Error"]!);
        Assert.Empty(_email.Enviados);
    }
}
