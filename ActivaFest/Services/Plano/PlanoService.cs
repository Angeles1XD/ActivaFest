using System.Globalization;
using System.Net;
using System.Text;
using ActivaFest.Models;

namespace ActivaFest.Services.Plano;

// Datos mínimos para dibujar (sirven tanto para un evento guardado como para la vista previa del formulario)
public record ZonaPlano(int Id, string Nombre, string Color, string? Puerta, decimal Precio, int Capacidad, int Disponibles,
    int? Filas = null, int? AsientosPorFila = null);

public record AsientoPlano(int Id, int ZonaId, string Fila, int Numero, int X, int Y, EstadoAsiento Estado);

// =============================
// 🗺️ PLANO DEL RECINTO DESDE PLANTILLAS (HU25 / RF21)
// =============================
// El organizador no dibuja: elige una plantilla (teatro, estadio, campo abierto o feria) y configura sus zonas.
// Este servicio genera los asientos numerados (teatro) y el plano en SVG. Es puro (sin BD) para poder probarlo
// y usarlo en la vista previa del formulario.
public static class PlanoService
{
    // Límites por plantilla para que el plano sea legible
    public const int MaxFilasTeatro = 40;
    public const int MaxAsientosPorFila = 24; // con pasillo central, cabe en el ancho del plano
    public static readonly IReadOnlyDictionary<PlantillaPlano, int> MaxZonas = new Dictionary<PlantillaPlano, int>
    {
        [PlantillaPlano.Teatro] = 6,
        [PlantillaPlano.Estadio] = 4,
        [PlantillaPlano.CampoAbierto] = 5,
        [PlantillaPlano.Feria] = 3
    };

    public static readonly string[] Paleta = { "#6366f1", "#a855f7", "#0d9488", "#f59e0b", "#ec4899", "#22c55e" };

    public static string ColorPorDefecto(int indice) => Paleta[indice % Paleta.Length];

    // Colores aceptados: #RRGGBB (todo lo demás usa la paleta; evita inyectar CSS/SVG)
    public static string ColorValido(string? color, int indice) =>
        color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit) ? color.ToLowerInvariant() : ColorPorDefecto(indice);

    public static string Nombre(PlantillaPlano p) => p switch
    {
        PlantillaPlano.Teatro => "Teatro / auditorio",
        PlantillaPlano.Estadio => "Estadio / coliseo",
        PlantillaPlano.Feria => "Feria / exposición",
        _ => "Campo abierto"
    };

    public static string Descripcion(PlantillaPlano p) => p switch
    {
        PlantillaPlano.Teatro => "Asientos numerados: cada zona tiene sus filas y asientos por fila.",
        PlantillaPlano.Estadio => "Tribunas alrededor del campo (hasta 4 zonas).",
        PlantillaPlano.Feria => "Entrada general con stands, puertas y servicios.",
        _ => "Zonas delante del escenario (VIP, Preferencial, General...)."
    };

    // Copia a la zona los datos del plano del formulario (color, puerta, filas y asientos según la plantilla)
    public static TipoEntrada AplicarZona(TipoEntrada zona, TipoEntradaInput datos, PlantillaPlano plantilla, int indice)
    {
        zona.Color = ColorValido(datos.Color, indice);
        zona.PuertaIngreso = string.IsNullOrWhiteSpace(datos.Puerta) ? null : datos.Puerta.Trim();
        zona.AsientosNumerados = plantilla == PlantillaPlano.Teatro;
        zona.Filas = zona.AsientosNumerados ? datos.Filas : null;
        zona.AsientosPorFila = zona.AsientosNumerados ? datos.AsientosPorFila : null;
        if (zona.AsientosNumerados)
            zona.Capacidad = (zona.Filas ?? 0) * (zona.AsientosPorFila ?? 0);
        return zona;
    }

    // Filas A..Z, luego AA, AB...
    public static string LetraFila(int indice) =>
        indice < 26 ? ((char)('A' + indice)).ToString() : LetraFila(indice / 26 - 1) + (char)('A' + indice % 26);

    // =============================
    // 🪑 ASIENTOS NUMERADOS (plantilla de teatro)
    // =============================
    private const int Ancho = 800;
    private const int Separacion = 26;   // distancia entre asientos
    private const int Pasillo = 44;      // pasillo central
    private const int InicioFilas = 128;

    // Las filas siguen el orden de las zonas (la primera zona queda más cerca del escenario) y las letras continúan
    // de una zona a otra, así fila + número es único en todo el teatro. Cada fila se parte en dos bloques con pasillo.
    public static List<Asiento> GenerarAsientos(IReadOnlyList<TipoEntrada> zonas)
    {
        var asientos = new List<Asiento>();
        var fila = 0;

        foreach (var zona in zonas)
        {
            var filas = zona.Filas ?? 0;
            var porFila = zona.AsientosPorFila ?? 0;

            for (var f = 0; f < filas; f++, fila++)
            {
                var y = InicioFilas + fila * Separacion;
                for (var n = 1; n <= porFila; n++)
                {
                    asientos.Add(new Asiento
                    {
                        TipoEntrada = zona,
                        TipoEntradaId = zona.Id,
                        Fila = LetraFila(fila),
                        Numero = n,
                        PosX = XAsiento(n, porFila),
                        PosY = y,
                        Estado = EstadoAsiento.Disponible
                    });
                }
            }
        }

        return asientos;
    }

    // Bloque izquierdo: asientos 1..mitad; bloque derecho: el resto, separados por el pasillo central
    private static int XAsiento(int numero, int porFila)
    {
        var izquierda = (porFila + 1) / 2;
        var centro = Ancho / 2;
        return numero <= izquierda
            ? centro - Pasillo / 2 - (izquierda - numero) * Separacion - Separacion / 2
            : centro + Pasillo / 2 + (numero - izquierda - 1) * Separacion + Separacion / 2;
    }

    // Plano de un evento guardado (ficha del evento); las zonas se dibujan en el orden en que se crearon
    public static string SvgDeEvento(Evento evento, IEnumerable<Asiento> asientos, int? asientoResaltado = null, int? zonaResaltada = null)
    {
        var (zonas, lista) = DatosDeEvento(evento, asientos);
        return GenerarSvg(evento.PlantillaPlano, zonas, lista, asientoResaltado, zonaResaltada);
    }

    // HU27: imagen PNG del plano con varias ubicaciones resaltadas (para el correo de la compra)
    public static byte[] PngDeEvento(Evento evento, IEnumerable<Asiento> asientos, IReadOnlySet<int> asientosResaltados, IReadOnlySet<int> zonasResaltadas)
    {
        var (zonas, lista) = DatosDeEvento(evento, asientos);
        return PlanoImagen.GenerarPng(evento.PlantillaPlano, zonas, lista, asientosResaltados, zonasResaltadas);
    }

    private static (List<ZonaPlano> Zonas, List<AsientoPlano> Asientos) DatosDeEvento(Evento evento, IEnumerable<Asiento> asientos) =>
    (
        evento.TiposEntrada.OrderBy(t => t.Id)
            .Select((t, i) => new ZonaPlano(t.Id, t.Nombre, ColorValido(t.Color, i), t.PuertaIngreso, t.Precio, t.Capacidad, t.Disponibles, t.Filas, t.AsientosPorFila))
            .ToList(),
        asientos.Select(a => new AsientoPlano(a.Id, a.TipoEntradaId, a.Fila, a.Numero, a.PosX, a.PosY, a.Estado)).ToList()
    );

    // Vista previa del formulario: el plano tal como quedaría con los datos escritos (sin guardar nada)
    public static string SvgVistaPrevia(PlantillaPlano plantilla, IReadOnlyList<TipoEntradaInput> datos)
    {
        var zonas = datos.Take(MaxZonas.GetValueOrDefault(plantilla, 5)).Select((d, i) =>
        {
            var z = AplicarZona(new TipoEntrada { Id = i + 1, Nombre = string.IsNullOrWhiteSpace(d.Nombre) ? $"Zona {i + 1}" : d.Nombre.Trim(),
                                                  Precio = Math.Max(0, d.Precio), Capacidad = Math.Max(0, d.Capacidad) }, d, plantilla, i);
            if (z.AsientosNumerados)
            {
                z.Filas = Math.Clamp(z.Filas ?? 0, 0, MaxFilasTeatro);
                z.AsientosPorFila = Math.Clamp(z.AsientosPorFila ?? 0, 0, MaxAsientosPorFila);
            }
            return z;
        }).ToList();

        // Límite total de filas para que la vista previa no crezca sin control
        var filasRestantes = MaxFilasTeatro;
        foreach (var z in zonas.Where(z => z.AsientosNumerados))
        {
            z.Filas = Math.Min(z.Filas ?? 0, filasRestantes);
            filasRestantes -= z.Filas.Value;
        }

        var asientos = plantilla == PlantillaPlano.Teatro ? GenerarAsientos(zonas) : new List<Asiento>();
        var id = 0;
        var planoAsientos = asientos.Select(a => new AsientoPlano(++id, a.TipoEntradaId, a.Fila, a.Numero, a.PosX, a.PosY, a.Estado)).ToList();
        var planoZonas = zonas.Select((z, i) => new ZonaPlano(z.Id, z.Nombre, z.Color!, z.PuertaIngreso, z.Precio, z.Capacidad, z.Capacidad, z.Filas, z.AsientosPorFila)).ToList();
        return GenerarSvg(plantilla, planoZonas, planoAsientos);
    }

    // Geometría compartida por el SVG y la imagen PNG del correo (HU27)
    public static int AltoPlano(PlantillaPlano plantilla, int zonas, IReadOnlyCollection<AsientoPlano>? asientos) => plantilla switch
    {
        PlantillaPlano.Teatro => Math.Max(300, (asientos?.Count > 0 ? asientos.Max(a => a.Y) : InicioFilas) + 110),
        PlantillaPlano.Estadio => 540,
        PlantillaPlano.Feria => 500,
        _ => 120 + zonas * 78 + 110
    };

    // Rectángulo de la zona i (de "total") en las plantillas por zonas
    public static (int X, int Y, int W, int H) RectZona(PlantillaPlano plantilla, int i, int total)
    {
        switch (plantilla)
        {
            case PlantillaPlano.Estadio:
                var tribunas = new (int, int, int, int)[] { (60, 150, 170, 220), (570, 150, 170, 220), (250, 30, 300, 100), (250, 390, 300, 100) };
                return tribunas[Math.Min(i, tribunas.Length - 1)];
            case PlantillaPlano.Feria:
                var ancho = total == 0 ? 0 : 680 / total;
                return (60 + i * ancho, 345, ancho - (i < total - 1 ? 10 : 0), 64);
            default:
                // Campo abierto: la primera zona pegada al escenario y cada siguiente más ancha
                var w = 340 + i * 90;
                return ((Ancho - w) / 2, 100 + i * 78, w, 66);
        }
    }

    // =============================
    // 🖼️ SVG
    // =============================
    public static string GenerarSvg(PlantillaPlano plantilla, IReadOnlyList<ZonaPlano> zonas, IReadOnlyList<AsientoPlano>? asientos = null,
        int? asientoResaltado = null, int? zonaResaltada = null)
    {
        var sb = new StringBuilder();
        var alto = AltoPlano(plantilla, zonas.Count, asientos);

        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Ancho} {alto}\" class=\"plano-svg plano-{plantilla.ToString().ToLowerInvariant()}\" role=\"img\" aria-label=\"Plano del recinto: {E(Nombre(plantilla))}\">");

        switch (plantilla)
        {
            case PlantillaPlano.Teatro: Teatro(sb, zonas, asientos ?? Array.Empty<AsientoPlano>(), asientoResaltado); break;
            case PlantillaPlano.Estadio: Estadio(sb, zonas, zonaResaltada); break;
            case PlantillaPlano.Feria: Feria(sb, zonas, zonaResaltada); break;
            default: CampoAbierto(sb, zonas, zonaResaltada); break;
        }

        Puertas(sb, zonas, alto);
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Escenario(StringBuilder sb, int y, int ancho = 360, string texto = "Escenario")
    {
        var x = (Ancho - ancho) / 2;
        sb.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"{ancho}\" height=\"52\" rx=\"10\" class=\"plano-escenario\"/>");
        sb.Append($"<text x=\"{Ancho / 2}\" y=\"{y + 33}\" text-anchor=\"middle\" class=\"plano-escenario-texto\">{E(texto)}</text>");
    }

    private static void Teatro(StringBuilder sb, IReadOnlyList<ZonaPlano> zonas, IReadOnlyList<AsientoPlano> asientos, int? resaltado)
    {
        Escenario(sb, 30);
        sb.Append($"<text x=\"{Ancho / 2}\" y=\"110\" text-anchor=\"middle\" class=\"plano-nota\">pasillo central</text>");

        var colores = zonas.ToDictionary(z => z.Id, z => z.Color);
        var nombres = zonas.ToDictionary(z => z.Id, z => z.Nombre);

        // Letra de cada fila a la izquierda
        foreach (var fila in asientos.GroupBy(a => (a.Fila, a.Y)))
        {
            var x = fila.Min(a => a.X) - 24;
            sb.Append($"<text x=\"{x}\" y=\"{fila.Key.Y + 4}\" text-anchor=\"middle\" class=\"plano-fila\">{E(fila.Key.Fila)}</text>");
        }

        foreach (var a in asientos)
        {
            var libre = a.Estado == EstadoAsiento.Disponible;
            var clase = a.Id == resaltado ? "asiento asiento-tuyo" : libre ? "asiento" : "asiento asiento-ocupado";
            var color = libre ? colores.GetValueOrDefault(a.ZonaId, Paleta[0]) : "#3f3f46";
            var zona = nombres.GetValueOrDefault(a.ZonaId, "");
            sb.Append($"<circle cx=\"{a.X}\" cy=\"{a.Y}\" r=\"{(a.Id == resaltado ? 12 : 9)}\" fill=\"{color}\" class=\"{clase}\" data-asiento=\"{a.Id}\" data-zona=\"{a.ZonaId}\" data-fila=\"{E(a.Fila)}\" data-numero=\"{a.Numero}\" data-libre=\"{(libre ? 1 : 0)}\">");
            sb.Append($"<title>{E(zona)} · Fila {E(a.Fila)} · Asiento {a.Numero}{(libre ? "" : " (no disponible)")}</title></circle>");
            if (a.Id == resaltado)
                sb.Append($"<text x=\"{a.X + 18}\" y=\"{a.Y + 4}\" class=\"plano-etiqueta-tuya\">{E(a.Fila)}-{a.Numero}</text>");
        }
    }

    private static void CampoAbierto(StringBuilder sb, IReadOnlyList<ZonaPlano> zonas, int? resaltada)
    {
        Escenario(sb, 30, 300);
        for (var i = 0; i < zonas.Count; i++)
            ZonaEn(sb, PlantillaPlano.CampoAbierto, zonas, i, resaltada);

        // Servicios a los costados
        Servicio(sb, 24, 110, "🚻 Baños");
        Servicio(sb, Ancho - 124, 110, "🍔 Comida");
    }

    private static void Estadio(StringBuilder sb, IReadOnlyList<ZonaPlano> zonas, int? resaltada)
    {
        // Campo de juego al centro y hasta 4 tribunas alrededor
        sb.Append("<rect x=\"250\" y=\"150\" width=\"300\" height=\"220\" rx=\"14\" class=\"plano-campo\"/>");
        sb.Append("<line x1=\"400\" y1=\"150\" x2=\"400\" y2=\"370\" class=\"plano-campo-linea\"/>");
        sb.Append("<circle cx=\"400\" cy=\"260\" r=\"36\" class=\"plano-campo-linea\" fill=\"none\"/>");

        for (var i = 0; i < zonas.Count && i < MaxZonas[PlantillaPlano.Estadio]; i++)
            ZonaEn(sb, PlantillaPlano.Estadio, zonas, i, resaltada);
    }

    private static void Feria(StringBuilder sb, IReadOnlyList<ZonaPlano> zonas, int? resaltada)
    {
        // Pabellón con stands en cuadrícula y la zona de entrada abajo
        sb.Append("<rect x=\"60\" y=\"30\" width=\"680\" height=\"300\" rx=\"14\" class=\"plano-pabellon\"/>");
        for (var fila = 0; fila < 3; fila++)
            for (var col = 0; col < 6; col++)
            {
                var x = 90 + col * 105;
                var y = 55 + fila * 90;
                sb.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"85\" height=\"60\" rx=\"6\" class=\"plano-stand\"/>");
                sb.Append($"<text x=\"{x + 42}\" y=\"{y + 35}\" text-anchor=\"middle\" class=\"plano-nota\">Stand {fila * 6 + col + 1}</text>");
            }

        for (var i = 0; i < zonas.Count; i++)
            ZonaEn(sb, PlantillaPlano.Feria, zonas, i, resaltada);

        Servicio(sb, 60, 420, "🚻 Baños");
        Servicio(sb, 640, 420, "ℹ️ Informes");
    }

    private static void ZonaEn(StringBuilder sb, PlantillaPlano plantilla, IReadOnlyList<ZonaPlano> zonas, int i, int? resaltada)
    {
        var (x, y, w, h) = RectZona(plantilla, i, zonas.Count);
        Zona(sb, zonas[i], x, y, w, h, resaltada);
    }

    private static void Zona(StringBuilder sb, ZonaPlano z, int x, int y, int ancho, int alto, int? resaltada)
    {
        var agotada = z.Disponibles <= 0;
        var clase = z.Id == resaltada ? "plano-zona plano-zona-tuya" : agotada ? "plano-zona plano-zona-agotada" : "plano-zona";
        sb.Append($"<g class=\"{clase}\" data-zona=\"{z.Id}\">");
        sb.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"{ancho}\" height=\"{alto}\" rx=\"10\" fill=\"{(agotada ? "#3f3f46" : z.Color)}\"/>");
        sb.Append($"<text x=\"{x + ancho / 2}\" y=\"{y + alto / 2 - 2}\" text-anchor=\"middle\" class=\"plano-zona-nombre\">{E(z.Nombre)}</text>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<text x=\"{x + ancho / 2}\" y=\"{y + alto / 2 + 16}\" text-anchor=\"middle\" class=\"plano-zona-dato\">{(agotada ? "Agotado" : z.Precio == 0 ? "Gratis" : $"S/ {z.Precio:0.00}")}</text>");
        sb.Append($"<title>{E(z.Nombre)}: {z.Disponibles} de {z.Capacidad} disponibles</title></g>");
    }

    private static void Servicio(StringBuilder sb, int x, int y, string texto)
    {
        sb.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"100\" height=\"34\" rx=\"8\" class=\"plano-servicio\"/>");
        sb.Append($"<text x=\"{x + 50}\" y=\"{y + 22}\" text-anchor=\"middle\" class=\"plano-nota\">{E(texto)}</text>");
    }

    // Las puertas de las zonas se reparten a lo largo del borde inferior
    private static void Puertas(StringBuilder sb, IReadOnlyList<ZonaPlano> zonas, int alto)
    {
        var puertas = zonas.Select(z => z.Puerta?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        for (var i = 0; i < puertas.Count; i++)
        {
            var x = (int)(Ancho * (i + 1) / (double)(puertas.Count + 1));
            sb.Append($"<g class=\"plano-puerta\"><rect x=\"{x - 55}\" y=\"{alto - 46}\" width=\"110\" height=\"30\" rx=\"7\"/>");
            sb.Append($"<text x=\"{x}\" y=\"{alto - 26}\" text-anchor=\"middle\">🚪 {E(puertas[i]!)}</text></g>");
        }
    }

    private static string E(string texto) => WebUtility.HtmlEncode(texto);
}
