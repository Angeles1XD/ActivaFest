using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ActivaFest.Data;
using ActivaFest.Models;
using static ActivaFest.Tests.Formularios;

namespace ActivaFest.Tests;

// =============================
// 📝 HU09: PUBLICAR UN EVENTO (pruebas de integración)
// =============================
public class HU09PublicarEventoTests : IClassFixture<AppDePrueba>
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    private readonly AppDePrueba _app;

    public HU09PublicarEventoTests(AppDePrueba app)
    {
        _app = app;
    }

    private async Task<(HttpClient Cliente, string Email)> OrganizadorAsync()
    {
        var cliente = _app.Cliente();
        var email = Correo();
        await RegistrarAsync(cliente, email, rol: Roles.Organizador);
        return (cliente, email);
    }

    // Formulario válido; cada prueba cambia solo el dato que quiere romper
    private static Dictionary<string, string> Datos(string titulo) => new()
    {
        ["Titulo"] = titulo,
        ["Descripcion"] = "Gran concierto de rock al aire libre con bandas locales.",
        ["CategoriaId"] = "1",
        ["Fecha"] = DateTime.Now.AddDays(10).ToString("yyyy-MM-ddTHH:mm"),
        ["Ubicacion"] = "Estadio Monumental, Arequipa",
        ["Latitud"] = "-16.4",
        ["Longitud"] = "-71.53",
        ["TiposEntrada[0].Nombre"] = "General",
        ["TiposEntrada[0].Precio"] = "50",
        ["TiposEntrada[0].Capacidad"] = "300",
        ["TiposEntrada[1].Nombre"] = "VIP",
        ["TiposEntrada[1].Precio"] = "120",
        ["TiposEntrada[1].Capacidad"] = "50"
    };

    private static async Task<HttpResponseMessage> PublicarAsync(HttpClient cliente, Dictionary<string, string> datos, int fotos = 3)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(await TokenAsync(cliente, "/Eventos/Crear")), "__RequestVerificationToken");
        foreach (var (clave, valor) in datos)
            form.Add(new StringContent(valor), clave);

        for (var i = 0; i < fotos; i++)
        {
            var foto = new ByteArrayContent(Png);
            foto.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(foto, "Fotos", $"recinto{i}.png");
        }

        return await cliente.PostAsync("/Eventos/Crear", form);
    }

    private async Task<Evento?> BuscarAsync(string titulo)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Eventos
            .Include(e => e.TiposEntrada).Include(e => e.Imagenes)
            .FirstOrDefaultAsync(e => e.Titulo == titulo);
    }

    [Fact]
    public async Task El_evento_se_crea_pendiente_con_aforo_precios_y_fotos_y_no_es_visible_hasta_su_aprobacion()
    {
        var (organizador, email) = await OrganizadorAsync();
        var titulo = $"Rock {Guid.NewGuid():N}";

        var r = await PublicarAsync(organizador, Datos(titulo));

        Assert.Equal("/Eventos/MisEventos", r.Headers.Location!.OriginalString);
        var evento = await BuscarAsync(titulo);
        Assert.NotNull(evento);
        Assert.Equal(EstadoEvento.Pendiente, evento.Estado);
        Assert.Equal(350, evento.Aforo);
        Assert.Equal(new[] { ("General", 50m), ("VIP", 120m) }, evento.TiposEntrada.OrderBy(t => t.Precio).Select(t => (t.Nombre, t.Precio)));
        Assert.Equal(3, evento.Imagenes.Count);
        Assert.Contains(_app.Correos.Para(email), c => c.Asunto.Contains(titulo));

        // No visible para el público: ni en el catálogo ni en su ficha
        var publico = _app.Cliente();
        Assert.DoesNotContain($"/Eventos/Detalle/{evento.Id}\"", await publico.GetStringAsync($"/Eventos?Buscar={titulo}"));
        Assert.Equal(HttpStatusCode.NotFound, (await publico.GetAsync($"/Eventos/Detalle/{evento.Id}")).StatusCode);

        // Su organizador sí puede revisarlo
        Assert.Equal(HttpStatusCode.OK, (await organizador.GetAsync($"/Eventos/Detalle/{evento.Id}")).StatusCode);
    }

    public static TheoryData<string, string, string, int> DatosInvalidos => new()
    {
        { "Fecha", DateTime.Now.AddDays(-1).ToString("yyyy-MM-ddTHH:mm"), "La fecha del evento debe ser futura", 3 },
        { "Titulo", "", "El título es obligatorio", 3 },
        { "Ubicacion", "", "La dirección es obligatoria", 3 },
        { "Latitud", "", "Marca la ubicación del recinto en el mapa", 3 },
        { "TiposEntrada[0].Capacidad", "0", "La capacidad debe ser al menos 1", 3 },
        { "TiposEntrada[0].Precio", "-5", "El precio debe estar entre 0 y 100 000", 3 },
        { "Titulo", "Válido", "Sube al menos 3 fotos del recinto", 2 }
    };

    [Theory]
    [MemberData(nameof(DatosInvalidos))]
    public async Task Valida_campos_obligatorios_aforo_mayor_que_cero_y_fecha_futura(string campo, string valor, string mensaje, int fotos)
    {
        var (organizador, _) = await OrganizadorAsync();
        var titulo = $"Invalido {Guid.NewGuid():N}";
        var datos = Datos(titulo);
        datos[campo] = campo == "Titulo" && valor == "Válido" ? titulo : valor;

        var r = await PublicarAsync(organizador, datos, fotos);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(mensaje, await TextoAsync(r));
        Assert.Null(await BuscarAsync(titulo));
    }

    [Fact]
    public async Task Rechaza_archivos_que_no_son_imagenes()
    {
        var (organizador, _) = await OrganizadorAsync();
        var titulo = $"Falso {Guid.NewGuid():N}";

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(await TokenAsync(organizador, "/Eventos/Crear")), "__RequestVerificationToken");
        foreach (var (clave, valor) in Datos(titulo))
            form.Add(new StringContent(valor), clave);
        for (var i = 0; i < 3; i++)
            form.Add(new ByteArrayContent("MZ ejecutable disfrazado"u8.ToArray()), "Fotos", $"foto{i}.png");

        var r = await organizador.PostAsync("/Eventos/Crear", form);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Null(await BuscarAsync(titulo));
    }

    [Fact]
    public async Task Un_usuario_sin_rol_de_organizador_no_puede_publicar()
    {
        var usuario = _app.Cliente();
        await RegistrarAsync(usuario, Correo());

        Assert.StartsWith("/Auth/AccesoDenegado", (await usuario.GetAsync("/Eventos/Crear")).Headers.Location!.PathAndQuery);
    }
}
