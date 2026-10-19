using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ActivaFest.Data;
using ActivaFest.Models;
using ActivaFest.Services.Catalogo;
using ActivaFest.Services.Plano;
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

    // HU28: plano con la ubicación del usuario resaltada (URL del SVG de su entrada)
    public string? Plano { get; set; }

    // OE3: la consulta se resolvió sin necesidad de un humano
    public bool Resuelta { get; set; }

    // HU14: Llama 3.2 redactó la respuesta (los datos siguen siendo los de la BD)
    public bool RedactadaPorLlm { get; set; }
}

public record IntencionMetrica(string Intencion, int Total, int Resueltas);
public record ConsultaPendiente(DateTime Fecha, string Pregunta, string Intencion);

// HU14 / OE3: el asistente debe resolver sin ayuda humana al menos el 80 % de las consultas
public class MetricasAsistente
{
    public const double Meta = 0.80;

    public int Dias { get; set; }
    public int Total { get; set; }
    public int Resueltas { get; set; }
    public int ConLlm { get; set; }
    public double? Tasa => Total == 0 ? null : (double)Resueltas / Total;
    public bool CumpleMeta => Tasa >= Meta;
    public List<IntencionMetrica> PorIntencion { get; set; } = new();
    public List<ConsultaPendiente> NoResueltas { get; set; } = new();
}

public interface IAsistenteService
{
    Task<RespuestaChat> ResponderAsync(SolicitudChat solicitud, string? usuarioId, CancellationToken ct = default);

    string DetectarIntencion(string mensaje);

    // HU14 / OE3: porcentaje de consultas resueltas sin ayuda humana y preguntas por mejorar
    Task<MetricasAsistente> MetricasAsync(int dias = 30, CancellationToken ct = default);
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
    private readonly IUbicacionService _ubicacion;
    private readonly ILogger<AsistenteService> _logger;

    public AsistenteService(ApplicationDbContext db, MachineLearningService ml, ICatalogoService catalogo,
        IRecomendacionService recomendaciones, IRedactorLlm redactor, IUbicacionService ubicacion, ILogger<AsistenteService> logger)
    {
        _db = db;
        _ml = ml;
        _catalogo = catalogo;
        _recomendaciones = recomendaciones;
        _redactor = redactor;
        _ubicacion = ubicacion;
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
    // Responde y registra la consulta (diagrama de secuencia de HU14, paso 11: métrica del OE3)
    public async Task<RespuestaChat> ResponderAsync(SolicitudChat solicitud, string? usuarioId, CancellationToken ct = default)
    {
        var respuesta = await ResolverAsync(solicitud, usuarioId, ct);

        var pregunta = (solicitud.Mensaje ?? "").Trim();
        _db.ConsultasAsistente.Add(new ConsultaAsistente
        {
            Fecha = DateTime.UtcNow,
            Pregunta = pregunta.Length > 200 ? pregunta[..200] : pregunta,
            Intencion = respuesta.Intencion,
            Resuelta = respuesta.Resuelta,
            EventoId = respuesta.EventoId,
            ConSesion = usuarioId != null,
            RedactadaPorLlm = respuesta.RedactadaPorLlm
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // La métrica nunca debe impedir que el usuario reciba su respuesta
            _logger.LogWarning(ex, "No se pudo registrar la consulta al asistente");
        }
        return respuesta;
    }

    private static void Redactar(RespuestaChat respuesta, string? redactado)
    {
        if (string.IsNullOrWhiteSpace(redactado))
            return;
        respuesta.Texto = redactado;
        respuesta.RedactadaPorLlm = true;
    }

    public async Task<MetricasAsistente> MetricasAsync(int dias = 30, CancellationToken ct = default)
    {
        var desde = DateTime.UtcNow.AddDays(-Math.Clamp(dias, 1, 365));
        var consultas = _db.ConsultasAsistente.AsNoTracking().Where(c => c.Fecha >= desde);

        var porIntencion = await consultas
            .GroupBy(c => c.Intencion)
            .Select(g => new IntencionMetrica(g.Key, g.Count(), g.Count(c => c.Resuelta)))
            .ToListAsync(ct);

        return new MetricasAsistente
        {
            Dias = dias,
            Total = porIntencion.Sum(i => i.Total),
            Resueltas = porIntencion.Sum(i => i.Resueltas),
            ConLlm = await consultas.CountAsync(c => c.RedactadaPorLlm, ct),
            PorIntencion = porIntencion.OrderByDescending(i => i.Total).ToList(),
            NoResueltas = await consultas.Where(c => !c.Resuelta).OrderByDescending(c => c.Fecha).Take(20)
                .Select(c => new ConsultaPendiente(c.Fecha, c.Pregunta, c.Intencion)).ToListAsync(ct)
        };
    }

    private async Task<RespuestaChat> ResolverAsync(SolicitudChat solicitud, string? usuarioId, CancellationToken ct = default)
    {
        var mensaje = (solicitud.Mensaje ?? "").Trim();
        var intencion = DetectarIntencion(mensaje);
        var texto = BaseConocimiento.Normalizar(mensaje);

        // HU28: "¿dónde está mi asiento?" se responde con las entradas del propio usuario
        if (intencion == Intenciones.MiUbicacion)
        {
            var ubicacion = await MiUbicacionAsync(texto, solicitud.EventoId, usuarioId, ct);
            ubicacion.Intencion = intencion;
            if (ubicacion.Resuelta)
                Redactar(ubicacion, await _redactor.MejorarAsync(mensaje, ubicacion.Texto, ct));
            return ubicacion;
        }

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
            Redactar(respuesta, await _redactor.MejorarAsync(mensaje, respuesta.Texto, ct));

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
        Texto = "No estoy seguro de haber entendido 🤔. Puedo ayudarte a buscar eventos, decirte dónde quedan, a qué hora empiezan o cuánto cuestan, y resolver dudas sobre compras y entradas (prueba con las preguntas frecuentes de abajo). " +
                "Si tu duda es sobre un evento en particular, ábrelo y pregúntame desde su ficha, o escribe a soporte@activafest.com con el nombre del evento y te ponemos en contacto con su organizador.",
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
            .Include(e => e.Organizador)
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
                                  $"\nPara detalles como estacionamiento o accesibilidad, revisa la descripción o consulta a su organizador{(evento.Organizador != null ? $", {evento.Organizador.NombreCompleto}," : "")} escribiendo a soporte@activafest.com.";
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

    // =============================
    // 🧭 ¿DÓNDE ESTÁ MI ASIENTO? (HU28)
    // =============================
    // La ubicación sale de la función de Semantic Kernel ObtenerUbicacionDeMiEntrada (solo entradas del usuario
    // autenticado). Tres caminos, como en el diagrama: una entrada, varias para esa fecha o ninguna.
    private async Task<RespuestaChat> MiUbicacionAsync(string texto, int? eventoContexto, string? usuarioId, CancellationToken ct)
    {
        if (usuarioId == null)
        {
            return new RespuestaChat
            {
                Texto = "Para decirte dónde está tu asiento necesito ver tus entradas: inicia sesión con la cuenta con la que compraste y vuelve a preguntarme.",
                Enlaces = new() { new EnlaceChat("Iniciar sesión", "/Auth/Login") },
                Resuelta = true
            };
        }

        var fecha = FechaMencionada(texto, DateTime.Today);
        var kernel = PluginUbicacion.KernelPara(_ubicacion, usuarioId);
        var argumentos = new Microsoft.SemanticKernel.KernelArguments();
        if (fecha != null)
            argumentos["fecha"] = fecha.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var resultado = await kernel.InvokeAsync(PluginUbicacion.NombrePlugin, PluginUbicacion.NombreFuncion, argumentos, ct);
        var entradas = resultado.GetValue<List<UbicacionMiEntrada>>() ?? new();

        // Si nombra un evento, solo ese; si pregunta desde la ficha de un evento para el que tiene entradas, ese
        var nombrado = await IdentificarEventoAsync(texto, null, ct);
        if (nombrado != null)
            entradas = entradas.Where(e => e.EventoId == nombrado.Id).ToList();
        else if (eventoContexto != null && entradas.Any(e => e.EventoId == eventoContexto))
            entradas = entradas.Where(e => e.EventoId == eventoContexto).ToList();

        if (entradas.Count == 0)
        {
            var cuando = fecha == null ? "" : fecha == DateTime.Today ? " para hoy" : $" para el {Formato.Fecha(fecha.Value, "dddd dd 'de' MMMM")}";
            return new RespuestaChat
            {
                Texto = $"No encontré entradas activas{cuando}{(nombrado != null ? $" para «{nombrado.Titulo}»" : "")} en tu cuenta. " +
                        "Si ya compraste, revisa que hayas iniciado sesión con la misma cuenta; si no, puedes buscar un evento en el catálogo.",
                Enlaces = new() { new EnlaceChat("Mis entradas", "/Entradas"), new EnlaceChat("Ver eventos", "/Eventos") },
                Resuelta = true
            };
        }

        var eventos = entradas.GroupBy(e => e.EventoId).ToList();
        if (eventos.Count > 1)
        {
            // Varias entradas para esa fecha (o próximas): pregunta a cuál se refiere
            return new RespuestaChat
            {
                Texto = $"Tienes entradas para {eventos.Count} eventos{(fecha != null ? " ese día" : "")}. ¿A cuál te refieres?\n" +
                        string.Join("\n", eventos.Select(g => $"• {g.First().Evento} — {Formato.Fecha(g.First().Fecha, "ddd dd MMM, HH:mm")}")),
                Sugerencias = eventos.Take(4).Select(g => $"¿Dónde está mi asiento para {g.First().Evento}?").ToList(),
                Resuelta = false
            };
        }

        var mias = eventos[0].ToList();
        var primera = mias[0];
        var lineas = new List<string>
        {
            $"🎟️ {(mias.Count == 1 ? "Tu entrada" : $"Tus {mias.Count} entradas")} para «{primera.Evento}» ({Formato.Fecha(primera.Fecha, "ddd dd/MM, HH:mm")}, {primera.Recinto}):"
        };
        lineas.AddRange(mias.Take(10).Select(e => $"• {e.Resumen}"));
        lineas.Add(mias.Count == 1 ? "Cómo llegar:" : $"Cómo llegar a {(primera.Asiento != null ? $"la fila {primera.Fila}, asiento {primera.Asiento}" : $"la zona {primera.Zona}")}:");
        lineas.AddRange(primera.Indicaciones.Select((p, i) => $"{i + 1}. {p}"));

        return new RespuestaChat
        {
            Texto = string.Join("\n", lineas),
            EventoId = primera.EventoId,
            Plano = $"/Entradas/Plano/{primera.EntradaId}",
            Enlaces = new() { new EnlaceChat("Ver mi entrada", "/Entradas"), new EnlaceChat("Ver mapa y ruta al recinto", $"/Eventos/Detalle/{primera.EventoId}") },
            Sugerencias = new() { "¿A qué hora empieza?", "¿Cómo ingreso con el QR?" },
            Resuelta = true
        };
    }

    // "hoy", "mañana", "pasado mañana", "el sábado" o "28/11" → fecha; null si no menciona ninguna
    public static DateTime? FechaMencionada(string textoNormalizado, DateTime hoy)
    {
        if (BaseConocimiento.Contiene(textoNormalizado, "pasado manana"))
            return hoy.AddDays(2);
        if (BaseConocimiento.Contiene(textoNormalizado, "manana"))
            return hoy.AddDays(1);
        if (BaseConocimiento.Contiene(textoNormalizado, "hoy") || BaseConocimiento.Contiene(textoNormalizado, "esta noche"))
            return hoy;

        var dias = new[] { "domingo", "lunes", "martes", "miercoles", "jueves", "viernes", "sabado" };
        for (var d = 0; d < dias.Length; d++)
            if (BaseConocimiento.Contiene(textoNormalizado, dias[d]))
                return hoy.AddDays(((d - (int)hoy.DayOfWeek) + 7) % 7);

        // "28 11" (la normalización convierte "28/11" en "28 11")
        var m = System.Text.RegularExpressions.Regex.Match(textoNormalizado, @"\b(\d{1,2}) (\d{1,2})\b");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var dia) && int.TryParse(m.Groups[2].Value, out var mes) && mes is >= 1 and <= 12 && dia >= 1 && dia <= DateTime.DaysInMonth(hoy.Year, mes))
        {
            var f = new DateTime(hoy.Year, mes, dia);
            return f < hoy ? f.AddYears(1) : f;
        }
        return null;
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
