using System.Buffers.Binary;
using System.IO.Compression;
using ActivaFest.Models;

namespace ActivaFest.Services.Plano;

// =============================
// 🖼️ PLANO EN PNG PARA EL CORREO (HU27)
// =============================
// Los clientes de correo no muestran SVG, así que el plano con la ubicación resaltada se envía como imagen PNG.
// Se dibuja con la misma geometría del SVG (PlanoService) y se codifica a PNG sin librerías nativas, así funciona
// igual en Windows y en el contenedor Linux de Render. Lo resaltado va en rosa; el resto, atenuado.
public static class PlanoImagen
{
    private const double Escala = 0.75;
    private const int AnchoPlano = 800;

    private static readonly (byte R, byte G, byte B) Fondo = (18, 18, 26);
    private static readonly (byte R, byte G, byte B) Escenario = (109, 91, 240);
    private static readonly (byte R, byte G, byte B) Campo = (22, 101, 52);
    private static readonly (byte R, byte G, byte B) Resaltado = (236, 72, 153);
    private static readonly (byte R, byte G, byte B) Blanco = (255, 255, 255);

    public static byte[] GenerarPng(PlantillaPlano plantilla, IReadOnlyList<ZonaPlano> zonas, IReadOnlyList<AsientoPlano> asientos,
        IReadOnlySet<int> asientosResaltados, IReadOnlySet<int> zonasResaltadas)
    {
        var alto = PlanoService.AltoPlano(plantilla, zonas.Count, asientos);
        var lienzo = new Lienzo((int)(AnchoPlano * Escala), (int)(alto * Escala));
        lienzo.Rect(0, 0, AnchoPlano, alto, 0, Fondo);

        var colores = zonas.ToDictionary(z => z.Id, z => Rgb(z.Color));

        switch (plantilla)
        {
            case PlantillaPlano.Teatro:
                lienzo.Rect(220, 30, 360, 52, 10, Escenario);
                foreach (var a in asientos.Where(a => !asientosResaltados.Contains(a.Id)))
                    lienzo.Circulo(a.X, a.Y, 9, Atenuar(colores.GetValueOrDefault(a.ZonaId, Escenario)));
                foreach (var a in asientos.Where(a => asientosResaltados.Contains(a.Id)))
                {
                    lienzo.Circulo(a.X, a.Y, 12.5, Blanco);
                    lienzo.Circulo(a.X, a.Y, 10.5, Resaltado);
                }
                break;

            case PlantillaPlano.Estadio:
                lienzo.Rect(250, 150, 300, 220, 14, Campo);
                ZonasDe(lienzo, plantilla, zonas, colores, zonasResaltadas);
                break;

            case PlantillaPlano.Feria:
                lienzo.Rect(60, 30, 680, 300, 14, (30, 30, 40));
                for (var f = 0; f < 3; f++)
                    for (var c = 0; c < 6; c++)
                        lienzo.Rect(90 + c * 105, 55 + f * 90, 85, 60, 6, (45, 45, 58));
                ZonasDe(lienzo, plantilla, zonas, colores, zonasResaltadas);
                break;

            default:
                lienzo.Rect(250, 30, 300, 52, 10, Escenario);
                ZonasDe(lienzo, plantilla, zonas, colores, zonasResaltadas);
                break;
        }

        return lienzo.Png();
    }

    private static void ZonasDe(Lienzo lienzo, PlantillaPlano plantilla, IReadOnlyList<ZonaPlano> zonas,
        Dictionary<int, (byte, byte, byte)> colores, IReadOnlySet<int> resaltadas)
    {
        for (var i = 0; i < zonas.Count && i < PlanoService.MaxZonas[plantilla]; i++)
        {
            var (x, y, w, h) = PlanoService.RectZona(plantilla, i, zonas.Count);
            if (resaltadas.Contains(zonas[i].Id))
            {
                lienzo.Rect(x - 5, y - 5, w + 10, h + 10, 14, Blanco);
                lienzo.Rect(x, y, w, h, 10, Resaltado);
            }
            else
            {
                lienzo.Rect(x, y, w, h, 10, Atenuar(colores[zonas[i].Id]));
            }
        }
    }

    private static (byte R, byte G, byte B) Rgb(string hex) =>
        (Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));

    // Mezcla con el fondo para que lo resaltado destaque
    private static (byte R, byte G, byte B) Atenuar((byte R, byte G, byte B) c) =>
        ((byte)((c.R + Fondo.R * 2) / 3), (byte)((c.G + Fondo.G * 2) / 3), (byte)((c.B + Fondo.B * 2) / 3));

    // Lienzo RGB mínimo: rectángulos con esquinas redondeadas y círculos, en coordenadas del plano (escaladas)
    private sealed class Lienzo
    {
        private readonly int _ancho, _alto;
        private readonly byte[] _pixeles;

        public Lienzo(int ancho, int alto)
        {
            _ancho = ancho;
            _alto = alto;
            _pixeles = new byte[ancho * alto * 3];
        }

        private void Pintar(int px, int py, (byte R, byte G, byte B) c)
        {
            if (px < 0 || py < 0 || px >= _ancho || py >= _alto) return;
            var i = (py * _ancho + px) * 3;
            _pixeles[i] = c.R;
            _pixeles[i + 1] = c.G;
            _pixeles[i + 2] = c.B;
        }

        public void Circulo(double cx, double cy, double r, (byte, byte, byte) color)
        {
            double x0 = cx * Escala, y0 = cy * Escala, rr = r * Escala;
            for (var py = (int)(y0 - rr); py <= (int)(y0 + rr); py++)
                for (var px = (int)(x0 - rr); px <= (int)(x0 + rr); px++)
                    if ((px + 0.5 - x0) * (px + 0.5 - x0) + (py + 0.5 - y0) * (py + 0.5 - y0) <= rr * rr)
                        Pintar(px, py, color);
        }

        public void Rect(double x, double y, double w, double h, double radio, (byte, byte, byte) color)
        {
            double x0 = x * Escala, y0 = y * Escala, x1 = (x + w) * Escala, y1 = (y + h) * Escala, r = radio * Escala;
            for (var py = (int)y0; py < (int)Math.Ceiling(y1); py++)
                for (var px = (int)x0; px < (int)Math.Ceiling(x1); px++)
                {
                    // Esquinas redondeadas: fuera del radio en las esquinas no se pinta
                    var dx = Math.Max(Math.Max(x0 + r - (px + 0.5), px + 0.5 - (x1 - r)), 0);
                    var dy = Math.Max(Math.Max(y0 + r - (py + 0.5), py + 0.5 - (y1 - r)), 0);
                    if (dx * dx + dy * dy <= r * r)
                        Pintar(px, py, color);
                }
        }

        // Codificación PNG estándar: firma + IHDR + IDAT (zlib) + IEND
        public byte[] Png()
        {
            using var crudo = new MemoryStream();
            using (var zlib = new ZLibStream(crudo, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var y = 0; y < _alto; y++)
                {
                    zlib.WriteByte(0); // sin filtro
                    zlib.Write(_pixeles, y * _ancho * 3, _ancho * 3);
                }
            }

            using var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var cabecera = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(cabecera, _ancho);
            BinaryPrimitives.WriteInt32BigEndian(cabecera.AsSpan(4), _alto);
            cabecera[8] = 8;  // 8 bits por canal
            cabecera[9] = 2;  // RGB
            Bloque(png, "IHDR", cabecera);
            Bloque(png, "IDAT", crudo.ToArray());
            Bloque(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        private static void Bloque(Stream salida, string tipo, byte[] datos)
        {
            Span<byte> numero = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(numero, datos.Length);
            salida.Write(numero);

            var tipoYDatos = new byte[4 + datos.Length];
            System.Text.Encoding.ASCII.GetBytes(tipo, tipoYDatos);
            datos.CopyTo(tipoYDatos, 4);
            salida.Write(tipoYDatos);

            BinaryPrimitives.WriteUInt32BigEndian(numero, Crc32(tipoYDatos));
            salida.Write(numero);
        }

        private static readonly uint[] TablaCrc = Enumerable.Range(0, 256).Select(n =>
        {
            var c = (uint)n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        private static uint Crc32(byte[] datos)
        {
            var c = 0xFFFFFFFF;
            foreach (var b in datos)
                c = TablaCrc[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }
    }
}
