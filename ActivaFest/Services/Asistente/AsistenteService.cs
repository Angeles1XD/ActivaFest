using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;
using ActivaFest.Services.Recomendaciones;

namespace ActivaFest.Services.Asistente;

public class SolicitudChat
{
    public string Mensaje { get; set; } = "";

    // Evento del que se está hablando (ficha abierta o respuesta anterior)
    public int? EventoId { get; set; }

    // Ubicación del navegador, solo si el usuario la compartió
    public double? Latitud { get; set; }
    public double? Longitud { get; set; }
}

public record EnlaceChat(string Texto, string Url);

public class RespuestaChat
{
    public string Texto { get; set; } = "";
    public List<EnlaceChat> Enlaces { get; set; } = new();
    public List<string> Sugerencias { get; set; } = new();
    public string Intencion { get; set; } = Intenciones.Desconocida;

    // Evento al que se refiere la respuesta: el chat lo reenvía para preguntas de seguimiento ("¿y a qué hora?")
    public int? EventoId { get; set; }

    // OE3: la consulta se resolvió sin necesidad de un humano
    public bool Resuelta { get; set; }
}

public interface IAsistenteService
{
    Task<RespuestaChat> ResponderAsync(SolicitudChat solicitud, string? usuarioId, CancellationToken ct = default);

    string DetectarIntencion(string mensaje);
}

// =============================
// 🤖 ASISTENTE VIRTUAL (HU14 / RF12 / OE3)
// =============================
// Híbrido: palabras clave + modelo ML.NET para entender la pregunta, y respuestas armadas con datos reales
// (base de conocimiento y base de datos). Un LLM (Ollama) es opcional y solo mejora la redacción:
// si no está disponible, el asistente responde igual (funciona en Render y sin conexión al modelo).
public class AsistenteService : IAsistenteService
{
    private const float ConfianzaMinimaMl = 0.30f;

    private static readonly string[] SugerenciasGenerales =
        { "¿Qué eventos hay este fin de semana?", "¿Cómo compro entradas?", "Recomiéndame algo", "¿Dónde veo mis entradas?" };

    private static readonly Dictionary<string, int> SinonimosCategoria = new()
    {
        ["concierto"] = 1, ["conciertos"] = 1, ["musica"] = 1, ["banda"] = 1, ["rock"] = 1, ["jazz"] = 1,
        ["teatro"] = 2, ["obra"] = 2, ["obras"] = 2,
        ["festival"] = 3, ["festivales"] = 3,
        ["feria"] = 4, ["ferias"] = 4,
        ["exposicion"] = 5, ["exposiciones"] = 5, ["expo"] = 5, ["museo"] = 5, ["arte"] = 5,
        ["deporte"] = 6, ["deportes"] = 6, ["partido"] = 6, ["maraton"] = 6,
        ["gastronomia"] = 7, ["comida"] = 7, ["gastronomico"] = 7,
        ["familia"] = 8, ["familias"] = 8, ["familiar"] = 8, ["ninos"] = 8,
        ["cultura"] = 9, ["cultural"] = 9, ["culturales"] = 9
    };

    // Palabras que no sirven para reconocer el título de un evento
    private static readonly HashSet<string> Vacias = new()
    {
        "el", "la", "los", "las", "de", "del", "en", "y", "a", "un", "una", "al", "por", "para", "con", "que", "es",
        "evento", "eventos", "donde", "queda", "cuando", "hora", "cuanto", "cuesta", "precio", "precios", "entrada", "entradas"
    };

    private readonly ApplicationDbContext _db;
    private readonly MachineLearningService _ml;
    private readonly ICatalogoService _catalogo;
    private readonly IRecomendacionService _recomendaciones;
    private readonly IRedactorLlm _redactor;
    private readonly ILogger<AsistenteService> _logger;

    public AsistenteService(ApplicationDbContext db, MachineLearningService ml, ICatalogoService catalogo,
        IRecomendacionService recomendaciones, IRedactorLlm redactor, ILogger<AsistenteService> logger)
    {
        _db = db;
        _ml = ml;
        _catalogo = catalogo;
        _recomendaciones = recomendaciones;
        _redactor = redactor;
        _logger = logger;
    }

    // =============================
    // 1️⃣ ENTENDER LA PREGUNTA
    // =============================
    public string DetectarIntencion(string mensaje)
    {
        var texto = BaseConocimiento.Normalizar(mensaje);
        if (texto.Length == 0)
            return Intenciones.Desconocida;

        // Palabras clave: una frase más larga pesa más ("cuanto cuesta" > "concierto")
        var puntajes = BaseConocimiento.Entradas
            .Select(e => (e.Intencion, Puntaje: e.PalabrasClave
                .Where(k => BaseConocimiento.Contiene(texto, k))
                .Sum(k => BaseConocimiento.Normalizar(k).Split(' ').Length)))
            .Where(p => p.Puntaje > 0)
            .OrderByDescending(p => p.Puntaje)
            .ToList();

        var ml = _ml.Predecir(texto);

        if (puntajes.Count > 0)
        {
            var mejor = puntajes[0].Puntaje;
            var empatadas = puntajes.Where(p => p.Puntaje == mejor).Select(p => p.Intencion).ToList();

            // En caso de empate decide el modelo ML.NET; si no opina sobre ninguna, gana la primera
            return empatadas.Count == 1 || !empatadas.Contains(ml.Intencion) ? empatadas[0] : ml.Intencion;
        }

        // Sin palabras clave: se confía en el modelo si está suficientemente seguro
        return ml.Confianza >= ConfianzaMinimaMl && BaseConocimiento.TieneVocabularioConocido(texto) ? ml.Intencion : Intenciones.Desconocida;
    }

    // =============================
    // 2️⃣ RESPONDER
    // =============================
    public async Task<RespuestaChat> ResponderAsync(SolicitudChat solicitud, string? usuarioId, CancellationToken ct = default)
    {
        var mensaje = (solicitud.Mensaje ?? "").Trim();
        var intencion = DetectarIntencion(mensaje);
        var texto = BaseConocimiento.Normalizar(mensaje);

        // Si nombra un evento ("¿cuánto cuesta Rock Fest?"), la pregunta es sobre ese evento
        var evento = await IdentificarEventoAsync(texto, solicitud.EventoId, ct);
        if (evento != null && intencion is Intenciones.BuscarEventos or Intenciones.Desconocida)
            intencion = Intenciones.Instalaciones;

        RespuestaChat respuesta = intencion switch
        {
            Intenciones.BuscarEventos => await BuscarEventosAsync(texto, ct),
            Intenciones.Recomendacion => await RecomendarAsync(usuarioId, solicitud, ct),
            _ when Intenciones.SobreUnEvento.Contains(intencion) => await SobreEventoAsync(intencion, evento, ct),
            Intenciones.Desconocida => NoEntendida(),
            _ => DesdeBaseConocimiento(intencion)
        };

        respuesta.Intencion = intencion;
        respuesta.EventoId ??= evento?.Id;

        // Redacción más natural con el LLM si está disponible; los datos no cambian
        if (respuesta.Resuelta && intencion is not (Intenciones.Saludo or Intenciones.Agradecimiento))
            respuesta.Texto = await _redactor.MejorarAsync(mensaje, respuesta.Texto, ct) ?? respuesta.Texto;

        return respuesta;
    }

    private static RespuestaChat DesdeBaseConocimiento(string intencion)
    {
        var entrada = BaseConocimiento.Buscar(intencion)!;
        return new RespuestaChat
        {
            Texto = entrada.Respuesta ?? "",
            Enlaces = (entrada.Enlaces ?? Array.Empty<string>())
                .Select(e => e.Split('|'))
                .Select(p => new EnlaceChat(p[0], p[1]))
                .ToList(),
            Sugerencias = (entrada.Sugerencias ?? Array.Empty<string>()).ToList(),
            Resuelta = true
        };
    }

    private static RespuestaChat NoEntendida() => new()
    {
        Texto = "No estoy seguro de haber entendido 🤔. Puedo ayudarte a buscar eventos, decirte dónde quedan, a qué hora empiezan o cuánto cuestan, y resolver dudas sobre compras y entradas. También puedes escribir a soporte@activafest.com.",
        Sugerencias = SugerenciasGenerales.ToList(),
        Resuelta = false
    };

    // ---------- Eventos ----------
    private async Task<Evento?> IdentificarEventoAsync(string texto, int? eventoContexto, CancellationToken ct)
    {
        var ahora = DateTime.Now.AddHours(-Evento.HorasValidezEntradas);
        var eventos = await _db.Eventos.AsNoTracking()
            .Where(e => e.Estado == EstadoEvento.Aprobado && e.Fecha >= ahora)
            .Select(e => new { e.Id, e.Titulo })
            .ToListAsync(ct);

        var palabras = texto.Split(' ').ToHashSet();

        // El título (sin palabras vacías) aparece en el mensaje: coincidencia de al menos el 60 % de sus palabras
        var mejor = eventos
            .Select(e =>
            {
                var clave = BaseConocimiento.Normalizar(e.Titulo).Split(' ').Where(p => p.Length > 2 && !Vacias.Contains(p)).Distinct().ToList();
                var coincidencia = clave.Count == 0 ? 0 : (double)clave.Count(palabras.Contains) / clave.Count;
                return (e.Id, Coincidencia: coincidencia, Palabras: clave.Count);
            })
            .Where(x => x.Coincidencia >= 0.6)
            .OrderByDescending(x => x.Coincidencia).ThenByDescending(x => x.Palabras)
            .FirstOrDefault();

        var id = mejor.Id != 0 ? mejor.Id : eventoContexto;
        if (id == null)
            return null;

        return await _db.Eventos.AsNoTracking()
            .Include(e => e.TiposEntrada)
            .Include(e => e.Categoria)
            .Include(e => e.Imagenes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(e => e.Id == id && e.Estado == EstadoEvento.Aprobado, ct);
    }

    private async Task<RespuestaChat> SobreEventoAsync(string intencion, Evento? evento, CancellationToken ct)
    {
        if (evento == null)
        {
            // Pide aclarar con los próximos eventos como sugerencias
            var proximos = await _catalogo.BuscarAsync(new CatalogoFiltro(), ct);
            return new RespuestaChat
            {
                Texto = "¿Sobre qué evento me preguntas? Escribe su nombre o ábrelo en el catálogo y pregúntame desde su ficha.",
                Sugerencias = proximos.Eventos.Take(3).Select(e => $"{PreguntaPara(intencion)} {e.Titulo}?").ToList(),
                Enlaces = new() { new EnlaceChat("Ver eventos", "/Eventos") },
                Resuelta = false
            };
        }

        var culture = CultureInfo.GetCultureInfo("es-PE");
        var inv = CultureInfo.InvariantCulture;
        var ficha = new EnlaceChat($"Ver {evento.Titulo}", $"/Eventos/Detalle/{evento.Id}");
        var pasado = evento.YaOcurrio(DateTime.Now);

        var respuesta = new RespuestaChat { EventoId = evento.Id, Resuelta = true };

        switch (intencion)
        {
            case Intenciones.Ubicacion:
                respuesta.Texto = $"📍 «{evento.Titulo}» será en {evento.Ubicacion}. En la ficha del evento tienes el mapa y el botón para trazar la ruta desde tu ubicación.";
                respuesta.Enlaces.Add(new EnlaceChat("Ver mapa y ruta", $"/Eventos/Detalle/{evento.Id}"));
                if (evento.TieneUbicacion)
                    respuesta.Enlaces.Add(new EnlaceChat("Abrir en Google Maps",
                        $"https://www.google.com/maps/dir/?api=1&destination={evento.Latitud!.Value.ToString(inv)},{evento.Longitud!.Value.ToString(inv)}"));
                respuesta.Sugerencias.AddRange(new[] { "¿A qué hora empieza?", "¿Cuánto cuesta?" });
                break;

            case Intenciones.Horario:
                respuesta.Texto = pasado
                    ? $"«{evento.Titulo}» fue el {Formato.Fecha(evento.Fecha, "dddd dd 'de' MMMM")} a las {evento.Fecha:HH:mm}; ese evento ya se realizó."
                    : $"📅 «{evento.Titulo}» es el {Formato.Fecha(evento.Fecha, "dddd dd 'de' MMMM 'de' yyyy").ToLower(culture)} a las {evento.Fecha:HH:mm}. " +
                      $"El ingreso con QR se habilita desde las {evento.InicioIngreso:HH:mm}" +
                      (evento.InicioIngreso.Date != evento.Fecha.Date ? $" del {evento.InicioIngreso:dd/MM}" : "") + ".";
                respuesta.Enlaces.Add(ficha);
                respuesta.Sugerencias.AddRange(new[] { "¿Dónde queda?", "¿Cuánto cuesta?" });
                break;

            case Intenciones.Precio:
                var zonas = evento.TiposEntrada.OrderBy(t => t.Precio).ToList();
                if (zonas.Count == 0)
                {
                    respuesta.Texto = $"El organizador de «{evento.Titulo}» aún no publicó los precios.";
                }
                else
                {
                    var lineas = zonas.Select(z => $"• {z.Nombre}: {Formato.Precio(z.Precio)}" +
                        (z.Disponibles == 0 ? " (agotado)" : z.Disponibles <= 10 ? $" (¡quedan {z.Disponibles}!)" : ""));
                    var agotado = zonas.All(z => z.Disponibles == 0);
                    respuesta.Texto = $"🎟️ Entradas para «{evento.Titulo}»:\n{string.Join("\n", lineas)}" +
                                      (agotado ? "\nLas entradas están agotadas." : pasado ? "" : "\nPuedes comprarlas en la ficha del evento.");
                }
                respuesta.Enlaces.Add(new EnlaceChat(pasado ? $"Ver {evento.Titulo}" : "Comprar entradas", $"/Eventos/Detalle/{evento.Id}#comprar"));
                respuesta.Sugerencias.AddRange(new[] { "¿Cómo compro entradas?", "¿A qué hora empieza?" });
                break;

            default: // Instalaciones / información general del evento
                var descripcion = evento.Descripcion.Length > 220 ? evento.Descripcion[..220].TrimEnd() + "…" : evento.Descripcion;
                respuesta.Texto = $"«{evento.Titulo}» ({evento.Categoria?.Nombre}) · 📍 {evento.Ubicacion} · 📅 {evento.Fecha:dd/MM} a las {evento.Fecha:HH:mm}" +
                                  (evento.Aforo > 0 ? $" · 👥 aforo de {evento.Aforo:N0} personas" : "") + "." +
                                  (descripcion.Length > 0 ? $"\n{descripcion}" : "") +
                                  (evento.Imagenes.Count > 0 ? $"\nEn la ficha hay {evento.Imagenes.Count} fotos del recinto." : "") +
                                  "\nPara detalles como estacionamiento o accesibilidad, revisa la descripción o consulta al organizador.";
                respuesta.Enlaces.Add(ficha);
                respuesta.Sugerencias.AddRange(new[] { "¿Dónde queda?", "¿Cuánto cuesta?", "¿A qué hora empieza?" });
                break;
        }

        return respuesta;
    }

    private static string PreguntaPara(string intencion) => intencion switch
    {
        Intenciones.Ubicacion => "¿Dónde queda",
        Intenciones.Horario => "¿A qué hora empieza",
        Intenciones.Precio => "¿Cuánto cuesta",
        _ => "¿Qué sabes de"
    };

    // ---------- Búsqueda ----------
    private async Task<RespuestaChat> BuscarEventosAsync(string texto, CancellationToken ct)
    {
        var filtro = InterpretarBusqueda(texto, DateTime.Today);
        var resultado = await _catalogo.BuscarAsync(filtro, ct);

        // Si el texto libre no dio resultados, se reintenta solo con los filtros reconocidos
        if (resultado.Total == 0 && filtro.Buscar != null)
        {
            filtro.Buscar = null;
            resultado = await _catalogo.BuscarAsync(filtro, ct);
        }

        if (resultado.Total == 0)
        {
            return new RespuestaChat
            {
                Texto = "No encontré eventos próximos con esas características 😕. Prueba con otra fecha o categoría, o mira todo el catálogo.",
                Enlaces = new() { new EnlaceChat("Ver todos los eventos", "/Eventos") },
                Sugerencias = new() { "Recomiéndame algo", "¿Hay eventos gratis?" },
                Resuelta = true
            };
        }

        var top = resultado.Eventos.Take(3).ToList();
        return new RespuestaChat
        {
            Texto = $"Encontré {resultado.Total} {(resultado.Total == 1 ? "evento" : "eventos")}." + (resultado.Total > 3 ? " Estos son los más próximos:" : "") + "\n" +
                    string.Join("\n", top.Select(e => $"• {e.Titulo} — {Formato.Fecha(e.Fecha, "ddd dd MMM, HH:mm")} · {e.Ubicacion} · {Formato.PrecioDesde(e.PrecioDesde)}")),
            Enlaces = top.Select(e => new EnlaceChat(e.Titulo, $"/Eventos/Detalle/{e.Id}"))
                .Append(new EnlaceChat("Ver todos con estos filtros", UrlCatalogo(filtro)))
                .ToList(),
            EventoId = top.Count == 1 ? top[0].Id : null,
            Resuelta = true
        };
    }

    // Convierte lenguaje natural en filtros del catálogo: categoría, fechas y precio
    public static CatalogoFiltro InterpretarBusqueda(string textoNormalizado, DateTime hoy)
    {
        var filtro = new CatalogoFiltro();
        var palabras = textoNormalizado.Split(' ');

        var categoria = palabras.Select(p => SinonimosCategoria.TryGetValue(p, out var id) ? id : (int?)null).FirstOrDefault(c => c.HasValue);
        filtro.CategoriaId = categoria;

        if (BaseConocimiento.Contiene(textoNormalizado, "hoy"))
        {
            filtro.Desde = hoy; filtro.Hasta = hoy;
        }
        else if (BaseConocimiento.Contiene(textoNormalizado, "manana"))
        {
            filtro.Desde = hoy.AddDays(1); filtro.Hasta = hoy.AddDays(1);
        }
        else if (BaseConocimiento.Contiene(textoNormalizado, "fin de semana") || BaseConocimiento.Contiene(textoNormalizado, "finde"))
        {
            // Sábado y domingo próximos (si hoy es domingo, solo hoy)
            var sabado = hoy.DayOfWeek == DayOfWeek.Sunday ? hoy : hoy.AddDays(((int)DayOfWeek.Saturday - (int)hoy.DayOfWeek + 7) % 7);
            filtro.Desde = sabado;
            filtro.Hasta = hoy.DayOfWeek == DayOfWeek.Sunday ? hoy : sabado.AddDays(1);
        }
        else if (BaseConocimiento.Contiene(textoNormalizado, "esta semana"))
        {
            // Hasta el domingo de esta semana
            filtro.Desde = hoy; filtro.Hasta = hoy.AddDays((7 - (int)hoy.DayOfWeek) % 7);
        }
        else if (BaseConocimiento.Contiene(textoNormalizado, "este mes"))
        {
            filtro.Desde = hoy; filtro.Hasta = new DateTime(hoy.Year, hoy.Month, DateTime.DaysInMonth(hoy.Year, hoy.Month));
        }

        if (BaseConocimiento.Contiene(textoNormalizado, "gratis") || BaseConocimiento.Contiene(textoNormalizado, "gratuito") || BaseConocimiento.Contiene(textoNormalizado, "gratuitos"))
            filtro.SoloGratis = true;
        else if (BaseConocimiento.Contiene(textoNormalizado, "barato") || BaseConocimiento.Contiene(textoNormalizado, "baratos") || BaseConocimiento.Contiene(textoNormalizado, "economico"))
            filtro.Orden = OrdenCatalogo.PrecioAsc;

        // "en arequipa" → texto libre sobre la ubicación
        var indiceEn = Array.LastIndexOf(palabras, "en");
        if (indiceEn >= 0 && indiceEn < palabras.Length - 1)
        {
            var lugar = palabras[indiceEn + 1];
            if (lugar.Length > 2 && !SinonimosCategoria.ContainsKey(lugar) && lugar is not ("la" or "el" or "los" or "las"))
                filtro.Buscar = lugar;
        }

        return filtro;
    }

    private static string UrlCatalogo(CatalogoFiltro f)
    {
        var partes = f.RutaConPagina(1).Where(p => p.Key != "Pagina").Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}");
        var query = string.Join("&", partes);
        return query.Length == 0 ? "/Eventos" : $"/Eventos?{query}";
    }

    // ---------- Recomendación ----------
    private async Task<RespuestaChat> RecomendarAsync(string? usuarioId, SolicitudChat solicitud, CancellationToken ct)
    {
        var ubicacion = solicitud.Latitud.HasValue && solicitud.Longitud.HasValue ? new Ubicacion(solicitud.Latitud.Value, solicitud.Longitud.Value) : null;
        var recomendados = await _recomendaciones.RecomendarAsync(usuarioId, ubicacion, 3, ct);

        if (recomendados.Count == 0)
        {
            return new RespuestaChat
            {
                Texto = "Por ahora no hay eventos próximos para recomendarte. ¡Vuelve pronto!",
                Enlaces = new() { new EnlaceChat("Ver catálogo", "/Eventos") },
                Resuelta = true
            };
        }

        var intro = usuarioId == null
            ? "Te recomiendo estos eventos (inicia sesión para recomendaciones según tus gustos):"
            : "Según lo que te gusta, te recomiendo:";

        return new RespuestaChat
        {
            Texto = intro + "\n" + string.Join("\n", recomendados.Select(e =>
                $"• {e.Titulo} — {Formato.Fecha(e.Fecha, "ddd dd MMM")} · {Formato.PrecioDesde(e.PrecioDesde)} ({e.Motivo})")),
            Enlaces = recomendados.Select(e => new EnlaceChat(e.Titulo, $"/Eventos/Detalle/{e.Id}")).ToList(),
            Resuelta = true
        };
    }
}
