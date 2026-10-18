using System.Net;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ActivaFest.Models;
using ActivaFest.Services.Email;
using ActivaFest.Services.Imagenes;

namespace ActivaFest.Tests;

// Buzón falso: guarda los correos en memoria en lugar de enviarlos
public class CorreoDePrueba : IEmailService
{
    private readonly List<(string Para, string Asunto, string Html)> _enviados = new();
    private readonly Dictionary<string, int> _adjuntos = new();

    // Cantidad de adjuntos del último correo con ese asunto
    public int AdjuntosDe(string asunto) { lock (_enviados) return _adjuntos.GetValueOrDefault(asunto); }

    public IReadOnlyList<(string Para, string Asunto, string Html)> Enviados
    {
        get { lock (_enviados) return _enviados.ToList(); }
    }

    public Task EnviarAsync(string destinatario, string asunto, string html, IReadOnlyList<AdjuntoCorreo>? adjuntos = null)
    {
        lock (_enviados)
        {
            _enviados.Add((destinatario, asunto, html));
            _adjuntos[asunto] = adjuntos?.Count ?? 0;
        }
        return Task.CompletedTask;
    }

    public List<(string Para, string Asunto, string Html)> Para(string email) =>
        Enviados.Where(c => c.Para.Equals(email, StringComparison.OrdinalIgnoreCase)).ToList();
}

// Imágenes en memoria: las pruebas no escriben en wwwroot/uploads
public class ImagenesDePrueba : IAlmacenamientoImagenes
{
    public Task<string> GuardarAsync(IFormFile archivo, string carpeta, CancellationToken ct = default) =>
        Task.FromResult($"/uploads/{carpeta}/prueba-{Guid.NewGuid():N}{Path.GetExtension(archivo.FileName)}");

    public Task EliminarAsync(string url, CancellationToken ct = default) => Task.CompletedTask;
}

// Aplicación completa (Identity, cookies, antiforgery, vistas Razor) sobre una BD SQLite temporal
public class AppDePrueba : WebApplicationFactory<Program>
{
    private readonly string _archivoBd = Path.Combine(Path.GetTempPath(), $"activafest-test-{Guid.NewGuid():N}.db");

    public CorreoDePrueba Correos { get; } = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_archivoBd};Pooling=False");
        builder.UseSetting("SeedAdmin:Email", "admin@test.com");
        builder.UseSetting("SeedAdmin:Password", "Admin123!");
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IEmailService>(Correos);
            s.AddScoped<IAlmacenamientoImagenes, ImagenesDePrueba>();
        });
    }

    public HttpClient Cliente() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_archivoBd); } catch (IOException) { /* lo limpia el sistema */ }
    }
}

// Acciones de formulario reutilizadas por las pruebas de integración
public static class Formularios
{
    public static async Task<string> TokenAsync(HttpClient cliente, string url)
    {
        var html = await cliente.GetStringAsync(url);
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    public static async Task<HttpResponseMessage> RegistrarAsync(HttpClient cliente, string email, string password = "Clave1234",
        string rol = Roles.Asistente, bool consiente = true, bool promociones = false)
    {
        var datos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(cliente, "/Auth/Registro"),
            ["NombreCompleto"] = "Ana Pérez",
            ["Email"] = email,
            ["Password"] = password,
            ["ConfirmarPassword"] = password,
            ["Rol"] = rol,
            ["AceptaTratamientoDatos"] = consiente ? "true" : "false",
            ["AceptaPromociones"] = promociones ? "true" : "false"
        };
        return await cliente.PostAsync("/Auth/Registro", new FormUrlEncodedContent(datos));
    }

    public static async Task<HttpResponseMessage> IniciarSesionAsync(HttpClient cliente, string email, string password)
    {
        var datos = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await TokenAsync(cliente, "/Auth/Login"),
            ["Email"] = email,
            ["Password"] = password
        };
        return await cliente.PostAsync("/Auth/Login", new FormUrlEncodedContent(datos));
    }
    // Razor codifica las tildes (&#243;): se compara el texto tal como lo ve el usuario
    public static async Task<string> TextoAsync(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());

    public static string Correo() => $"u{Guid.NewGuid():N}@test.com";
}
