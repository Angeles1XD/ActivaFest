using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QRCoder;

namespace ActivaFest.Services.Entradas;

public class QrOptions
{
    // Clave secreta para firmar los QR. En producción se define por variable de entorno Qr__Secreto.
    public string Secreto { get; set; } = "";
}

public static class CodigoSeguro
{
    // 16 bytes aleatorios (128 bits) en base64url: 22 caracteres, aptos para URL y QR
    public static string Generar(int bytes = 16)
    {
        var datos = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public interface IQrEntradaService
{
    // Texto que va dentro del QR: "AF1.{codigo}.{firma}"
    string CrearContenido(string codigoEntrada);

    // Devuelve el código de la entrada si la firma es válida; null si el QR fue alterado o no es de ActivaFest
    string? LeerContenido(string contenido);

    byte[] GenerarPng(string codigoEntrada, int pixelesPorModulo = 10);
}

// =============================
// 🔐 QR FIRMADO DE LA ENTRADA (HU07)
// =============================
public class QrEntradaService : IQrEntradaService
{
    private const string Prefijo = "AF1";

    private readonly byte[] _clave;

    public QrEntradaService(IOptions<QrOptions> opciones, IWebHostEnvironment env, ILogger<QrEntradaService> logger)
    {
        var secreto = opciones.Value.Secreto;

        if (string.IsNullOrWhiteSpace(secreto))
        {
            if (!env.IsDevelopment())
                throw new InvalidOperationException("Configura Qr:Secreto (variable Qr__Secreto) para firmar las entradas en producción.");

            logger.LogWarning("Qr:Secreto no configurado: se usa una clave de desarrollo. No la uses en producción.");
            secreto = "activafest-clave-solo-desarrollo";
        }

        _clave = SHA256.HashData(Encoding.UTF8.GetBytes(secreto));
    }

    public string CrearContenido(string codigoEntrada) => $"{Prefijo}.{codigoEntrada}.{Firmar(codigoEntrada)}";

    public string? LeerContenido(string contenido)
    {
        var partes = contenido.Trim().Split('.');
        if (partes.Length != 3 || partes[0] != Prefijo || partes[1].Length == 0)
            return null;

        var esperada = Encoding.ASCII.GetBytes(Firmar(partes[1]));
        var recibida = Encoding.ASCII.GetBytes(partes[2]);

        // Comparación en tiempo constante para no filtrar información de la firma
        return CryptographicOperations.FixedTimeEquals(esperada, recibida) ? partes[1] : null;
    }

    public byte[] GenerarPng(string codigoEntrada, int pixelesPorModulo = 10)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(CrearContenido(codigoEntrada), QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(datos).GetGraphic(pixelesPorModulo);
    }

    private string Firmar(string codigo)
    {
        var hash = HMACSHA256.HashData(_clave, Encoding.UTF8.GetBytes(codigo));
        // 16 bytes de la firma bastan y mantienen el QR pequeño
        return Convert.ToBase64String(hash, 0, 16).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
