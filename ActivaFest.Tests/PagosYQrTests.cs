using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ActivaFest.Models;
using ActivaFest.Services.Entradas;
using ActivaFest.Services.Pagos;

namespace ActivaFest.Tests;

// =============================
// 🔐 QR FIRMADO (HU07)
// =============================
public class QrEntradaServiceTests
{
    private readonly QrEntradaService _qr = Fabrica.Qr();

    [Fact]
    public void El_contenido_firmado_se_lee_de_vuelta()
    {
        var codigo = CodigoSeguro.Generar();

        var contenido = _qr.CrearContenido(codigo);

        Assert.StartsWith("AF1.", contenido);
        Assert.Equal(codigo, _qr.LeerContenido(contenido));
    }

    [Fact]
    public void Detecta_un_codigo_alterado()
    {
        var contenido = _qr.CrearContenido("codigoOriginal");
        var alterado = contenido.Replace("codigoOriginal", "codigoFalsoXX");

        Assert.Null(_qr.LeerContenido(alterado));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hola mundo")]
    [InlineData("AF1.solo-dos-partes")]
    [InlineData("XX9.abc.def")]
    public void Rechaza_contenido_que_no_es_de_activafest(string contenido)
    {
        Assert.Null(_qr.LeerContenido(contenido));
    }

    [Fact]
    public void Un_qr_firmado_con_otra_clave_no_es_valido()
    {
        var falsificado = Fabrica.Qr("otra-clave").CrearContenido("abc123");

        Assert.Null(_qr.LeerContenido(falsificado));
    }

    [Fact]
    public void Genera_una_imagen_png()
    {
        var png = _qr.GenerarPng(CodigoSeguro.Generar());

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4));
    }

    [Fact]
    public void Los_codigos_son_unicos_y_aptos_para_url()
    {
        var codigos = Enumerable.Range(0, 2000).Select(_ => CodigoSeguro.Generar()).ToList();

        Assert.Equal(codigos.Count, codigos.Distinct().Count());
        Assert.All(codigos, c => Assert.Matches("^[A-Za-z0-9_-]{22}$", c));
    }

    [Fact]
    public void En_produccion_exige_configurar_la_clave()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new QrEntradaService(Options.Create(new QrOptions()), new EntornoFalso(), NullLogger<QrEntradaService>.Instance));
    }
}

// =============================
// 💳 MERCADO PAGO (HU06)
// =============================
public class MercadoPagoTests
{
    private class ManejadorFalso : HttpMessageHandler
    {
        public HttpRequestMessage? Ultima { get; private set; }
        public string? CuerpoEnviado { get; private set; }
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ultima = request;
            CuerpoEnviado = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return Responder(request);
        }
    }

    private static (MercadoPagoPasarela Pasarela, ManejadorFalso Http) Crear(string token = "TEST-123")
    {
        var http = new ManejadorFalso();
        var opciones = Options.Create(new PagosOptions { MercadoPago = new MercadoPagoOptions { AccessToken = token } });
        return (new MercadoPagoPasarela(new HttpClient(http), opciones, NullLogger<MercadoPagoPasarela>.Instance), http);
    }

    private static Orden OrdenDePrueba() => new()
    {
        Codigo = "ORD123",
        Total = 100m,
        FechaExpiracion = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc),
        Items = { new OrdenItem { TipoEntradaId = 7, Cantidad = 2, PrecioUnitario = 50m, TipoEntrada = new TipoEntrada { Nombre = "General" } } }
    };

    [Fact]
    public async Task Crea_la_preferencia_en_soles_y_usa_el_checkout_de_pruebas()
    {
        var (pasarela, http) = Crear();
        http.Responder = _ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":"pref1","init_point":"https://real","sandbox_init_point":"https://sandbox"}""")
        };

        var inicio = await pasarela.IniciarAsync(SolicitudCobro.DesdeOrden(OrdenDePrueba(), "Rock Fest"), "ana@test.pe",
            new UrlsRetorno("http://localhost/ok", "http://localhost/ko", "http://localhost/pend", "http://localhost/hook"));

        Assert.Equal("https://sandbox", inicio.UrlRedireccion);
        Assert.Equal("Bearer", http.Ultima!.Headers.Authorization!.Scheme);
        Assert.Contains("\"external_reference\":\"ORD123\"", http.CuerpoEnviado);
        Assert.Contains("\"currency_id\":\"PEN\"", http.CuerpoEnviado);
        Assert.Contains("\"unit_price\":50", http.CuerpoEnviado);
        // Con URLs http (local) no se pide retorno automático ni notificaciones
        Assert.DoesNotContain("auto_return", http.CuerpoEnviado);
        Assert.DoesNotContain("notification_url", http.CuerpoEnviado);
    }

    [Fact]
    public async Task Con_urls_https_pide_retorno_automatico_y_notificaciones()
    {
        var (pasarela, http) = Crear();
        http.Responder = _ => new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"init_point":"https://real"}""") };

        await pasarela.IniciarAsync(SolicitudCobro.DesdeOrden(OrdenDePrueba(), "Rock Fest"), null,
            new UrlsRetorno("https://af.pe/ok", "https://af.pe/ko", "https://af.pe/pend", "https://af.pe/hook"));

        Assert.Contains("\"auto_return\":\"approved\"", http.CuerpoEnviado);
        Assert.Contains("\"notification_url\":\"https://af.pe/hook\"", http.CuerpoEnviado);
    }

    [Fact]
    public async Task Consulta_el_estado_real_del_pago()
    {
        var (pasarela, http) = Crear();
        http.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":555,"status":"approved","external_reference":"ORD123","transaction_amount":100.0}""")
        };

        var pago = await pasarela.ConsultarPagoAsync("555");

        Assert.NotNull(pago);
        Assert.True(pago!.Aprobado);
        Assert.Equal("ORD123", pago.CodigoOrden);
        Assert.Equal(100m, pago.Monto);
        Assert.EndsWith("/v1/payments/555", http.Ultima!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Ignora_referencias_de_pago_que_no_son_numericas()
    {
        var (pasarela, http) = Crear();

        Assert.Null(await pasarela.ConsultarPagoAsync("../../otra-ruta"));
        Assert.Null(http.Ultima);
    }

    [Fact]
    public void Sin_access_token_no_esta_disponible()
    {
        Assert.False(Crear(token: "").Pasarela.Disponible);
    }

    [Fact]
    public void Valida_la_firma_de_los_webhooks()
    {
        const string secreto = "mi-secreto";
        var manifiesto = "id:123456;request-id:req-1;ts:1700000000;";
        var v1 = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), Encoding.UTF8.GetBytes(manifiesto))).ToLowerInvariant();

        Assert.True(MercadoPagoPasarela.FirmaWebhookValida(secreto, $"ts=1700000000,v1={v1}", "req-1", "123456"));
        Assert.False(MercadoPagoPasarela.FirmaWebhookValida(secreto, $"ts=1700000000,v1={v1}", "req-1", "999999"));
        Assert.False(MercadoPagoPasarela.FirmaWebhookValida("otro", $"ts=1700000000,v1={v1}", "req-1", "123456"));
        Assert.False(MercadoPagoPasarela.FirmaWebhookValida(secreto, null, "req-1", "123456"));
    }
}
