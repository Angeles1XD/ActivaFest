using System.ClientModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using ActivaFest.Models;
using ActivaFest.Services.Asistente;

namespace ActivaFest.Services.Analitica;

public record PeriodoConsulta(DateTime Desde, DateTime Hasta, string Etiqueta)
{
    public static PeriodoConsulta Entre(DateTime desde, DateTime hasta) =>
        new(desde.Date, hasta.Date, string.Create(CultureInfo.InvariantCulture, $"del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}"));
}

public class TablaCifras
{
    public string Titulo { get; set; } = "";
    public List<string> Columnas { get; set; } = new();
    public List<List<string>> Filas { get; set; } = new();
}

public class GraficoSugerido
{
    public string Tipo { get; set; } = "bar";
    public string Serie { get; set; } = "";
    public List<string> Etiquetas { get; set; } = new();
    public List<double> Valores { get; set; } = new();
}

public class RespuestaAnalitica
{
    public string Texto { get; set; } = "";
    public string? Funcion { get; set; }
    public string? Periodo { get; set; }
    public TablaCifras? Tabla { get; set; }
    public GraficoSugerido? Grafico { get; set; }
    public bool FueraDeAlcance { get; set; }
    public bool RedactadaPorIa { get; set; }
    public List<string> Sugerencias { get; set; } = new();
}

public record ConsultaInterpretada(string Funcion, PeriodoConsulta Periodo, int N, int Dias, double Umbral, string? Segmento);

public interface IAsistenteAnaliticoService
{
    Task<RespuestaAnalitica> ConsultarAsync(string pregunta, string administradorId, CancellationToken ct = default);
}

// =============================
// 🧩 PLUGIN DE SEMANTIC KERNEL: funciones de solo lectura de AnaliticaService (HU24)
// =============================
// La IA no calcula ni genera SQL: elige una de estas funciones, que devuelven datos reales y agregados.
// Cada función deja su resultado completo (texto base, tabla de cifras fuente y gráfico) en "Ultima".
public class FuncionesAnaliticas
{
    public const string NombrePlugin = "Analitica";

    private readonly IAnaliticaService _analitica;

    public FuncionesAnaliticas(IAnaliticaService analitica)
    {
        _analitica = analitica;
    }

    public RespuestaAnalitica? Ultima { get; private set; }

    private static readonly CultureInfo Pe = CultureInfo.GetCultureInfo("es-PE");
    private static string Pct(double? v) => v == null ? "—" : $"{v.Value * 100:0.#} %";

    private static PeriodoConsulta Periodo(string? desde, string? hasta)
    {
        var hoy = DateTime.Today;
        var d = DateTime.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var x) ? x : hoy.AddDays(-29);
        var h = DateTime.TryParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var y) ? y : hoy;
        return h < d ? PeriodoConsulta.Entre(h, d) : PeriodoConsulta.Entre(d, h);
    }

    private string Guardar(string funcion, string? periodo, string texto, TablaCifras tabla, GraficoSugerido? grafico)
    {
        Ultima = new RespuestaAnalitica { Funcion = funcion, Periodo = periodo, Texto = texto, Tabla = tabla, Grafico = grafico };
        // Lo que recibe el modelo: el texto y la tabla (datos agregados, sin nombres ni correos)
        return JsonSerializer.Serialize(new { periodo, resumen = texto, columnas = tabla.Columnas, filas = tabla.Filas });
    }

    [KernelFunction("ObtenerTopEventos")]
    [Description("Eventos con más entradas vendidas en un período (fechas yyyy-MM-dd) y cuántos devolver.")]
    public async Task<string> ObtenerTopEventosAsync(string? desde = null, string? hasta = null, int n = 5, CancellationToken ct = default)
    {
        var p = Periodo(desde, hasta);
        var top = await _analitica.TopEventosAsync(p.Desde, p.Hasta, n, ct);
        var tabla = new TablaCifras
        {
            Titulo = $"Eventos más vendidos {p.Etiqueta}",
            Columnas = new() { "#", "Evento", "Categoría", "Entradas", "Ingresos", "Ocupación" },
            Filas = top.Select((e, i) => new List<string> { $"{i + 1}", e.Evento, e.Categoria, e.Entradas.ToString("N0", Pe), Formato.Monto(e.Ingresos), Pct(e.Ocupacion) }).ToList()
        };
        var texto = top.Count == 0
            ? $"No hubo ventas pagadas {p.Etiqueta}."
            : $"El evento más vendido {p.Etiqueta} fue «{top[0].Evento}» con {top[0].Entradas:N0} entradas ({Formato.Monto(top[0].Ingresos)}). " +
              (top.Count > 1 ? $"La tabla muestra los {top.Count} primeros." : "Fue el único con ventas en ese período.");
        return Guardar("ObtenerTopEventos", p.Etiqueta, texto, tabla,
            new GraficoSugerido { Serie = "Entradas vendidas", Etiquetas = top.Select(e => e.Evento).ToList(), Valores = top.Select(e => (double)e.Entradas).ToList() });
    }

    [KernelFunction("IngresosPorCategoria")]
    [Description("Ingresos y entradas vendidas por categoría de evento en un período (fechas yyyy-MM-dd).")]
    public async Task<string> IngresosPorCategoriaAsync(string? desde = null, string? hasta = null, CancellationToken ct = default)
    {
        var p = Periodo(desde, hasta);
        var cats = await _analitica.IngresosPorCategoriaAsync(p.Desde, p.Hasta, ct);
        var total = cats.Sum(c => c.Ingresos);
        var tabla = new TablaCifras
        {
            Titulo = $"Ingresos por categoría {p.Etiqueta}",
            Columnas = new() { "Categoría", "Eventos", "Entradas", "Ingresos", "% del total" },
            Filas = cats.Select(c => new List<string> { c.Categoria, $"{c.Eventos}", c.Entradas.ToString("N0", Pe), Formato.Monto(c.Ingresos), total == 0 ? "—" : $"{c.Ingresos / total * 100:0.#} %" }).ToList()
        };
        var texto = cats.Count == 0
            ? $"No hubo ventas pagadas {p.Etiqueta}."
            : $"La categoría que generó más ingresos {p.Etiqueta} fue {cats[0].Categoria} con {Formato.Monto(cats[0].Ingresos)}" +
              (total == 0 ? "." : $" ({cats[0].Ingresos / total * 100:0.#} % del total de {Formato.Monto(total)}).");
        return Guardar("IngresosPorCategoria", p.Etiqueta, texto, tabla,
            new GraficoSugerido { Tipo = "doughnut", Serie = "Ingresos (S/)", Etiquetas = cats.Select(c => c.Categoria).ToList(), Valores = cats.Select(c => (double)c.Ingresos).ToList() });
    }

    [KernelFunction("VariabilidadPorEvento")]
    [Description("Variabilidad de las ventas semanales de cada evento (media, desviación estándar y coeficiente de variación) en un período.")]
    public async Task<string> VariabilidadPorEventoAsync(string? desde = null, string? hasta = null, CancellationToken ct = default)
    {
        var p = Periodo(desde, hasta);
        var lista = await _analitica.VariabilidadPorEventoAsync(p.Desde, p.Hasta, ct);
        var tabla = new TablaCifras
        {
            Titulo = $"Variabilidad de las ventas semanales {p.Etiqueta}",
            Columnas = new() { "Evento", "Entradas por semana", "Media (μ)", "Desv. estándar (σ)", "CV", "Demanda" },
            Filas = lista.Select(v => new List<string>
            {
                v.Evento, string.Join(" · ", v.Semanas), v.Media.ToString("0.##", Pe), v.Desviacion.ToString("0.##", Pe),
                v.Cv == null ? "—" : $"{v.Cv:0.#} %", v.Nivel
            }).ToList()
        };
        var inestables = lista.Where(v => v.Nivel == "Inestable").ToList();
        var texto = lista.Count == 0
            ? $"No hay eventos con al menos dos semanas de ventas {p.Etiqueta} para medir su variabilidad."
            : inestables.Count == 0
                ? $"Ninguno de los {lista.Count} eventos con ventas {p.Etiqueta} tiene demanda inestable (CV mayor a 50 %)."
                : $"{inestables.Count} de {lista.Count} eventos con ventas {p.Etiqueta} tienen demanda inestable (CV mayor a 50 %). " +
                  $"El más inestable es «{inestables[0].Evento}» con un CV de {inestables[0].Cv:0.#} %: conviene destacarlo o promocionarlo en las semanas de baja venta.";
        return Guardar("VariabilidadPorEvento", p.Etiqueta, texto, tabla,
            new GraficoSugerido { Serie = "Coeficiente de variación (%)", Etiquetas = lista.Take(10).Select(v => v.Evento).ToList(), Valores = lista.Take(10).Select(v => Math.Round(v.Cv ?? 0, 1)).ToList() });
    }

    [KernelFunction("ResumenSegmentos")]
    [Description("Cantidad de clientes y gasto total y promedio de cada segmento RFM (VIP, Frecuente, En riesgo, Nuevo, Ocasional).")]
    public async Task<string> ResumenSegmentosAsync([Description("Segmento del que se pregunta (opcional)")] string? segmento = null, CancellationToken ct = default)
    {
        var lista = await _analitica.ResumenSegmentosAsync(ct);
        var tabla = new TablaCifras
        {
            Titulo = "Clientes por segmento RFM (historial completo)",
            Columnas = new() { "Segmento", "Clientes", "Gasto total", "Gasto promedio", "Aceptan promociones" },
            Filas = lista.Select(s => new List<string> { s.Segmento, $"{s.Clientes}", Formato.Monto(s.GastoTotal), Formato.Monto(s.GastoPromedio), $"{s.ConConsentimiento}" }).ToList()
        };
        var foco = lista.FirstOrDefault(s => s.Segmento == segmento);
        var totalClientes = lista.Sum(s => s.Clientes);
        var texto = foco != null
            ? $"Hay {foco.Clientes} {(foco.Clientes == 1 ? "cliente" : "clientes")} en el segmento {foco.Segmento}" +
              (foco.Clientes == 0 ? "." : $", con un gasto promedio de {Formato.Monto(foco.GastoPromedio)} y {Formato.Monto(foco.GastoTotal)} en total; {foco.ConConsentimiento} aceptan recibir promociones.")
            : $"Hay {totalClientes} clientes con compras: " + string.Join(", ", lista.Where(s => s.Clientes > 0).Select(s => $"{s.Clientes} {s.Segmento}")) + ".";
        return Guardar("ResumenSegmentos", "historial completo", texto, tabla,
            new GraficoSugerido { Tipo = "doughnut", Serie = "Clientes", Etiquetas = lista.Select(s => s.Segmento).ToList(), Valores = lista.Select(s => (double)s.Clientes).ToList() });
    }

    [KernelFunction("TopClientes")]
    [Description("Clientes que más gastaron en un período, identificados solo con un código anónimo.")]
    public async Task<string> TopClientesAsync(string? desde = null, string? hasta = null, int n = 10, CancellationToken ct = default)
    {
        var p = Periodo(desde, hasta);
        var top = (await _analitica.FiltrarClientesAsync(new FiltroClientes { Desde = p.Desde, Hasta = p.Hasta }, ct)).Take(Math.Clamp(n, 1, 50)).ToList();
        var tabla = new TablaCifras
        {
            Titulo = $"Clientes que más gastaron {p.Etiqueta}",
            Columnas = new() { "#", "Cliente", "Segmento", "Gasto", "Entradas", "Compras" },
            Filas = top.Select((c, i) => new List<string> { $"{i + 1}", $"Cliente {c.Codigo}", c.Segmento, Formato.Monto(c.Gasto), $"{c.Entradas}", $"{c.Compras}" }).ToList()
        };
        var texto = top.Count == 0
            ? $"No hubo compras pagadas {p.Etiqueta}."
            : $"El cliente que más gastó {p.Etiqueta} es el {top[0].Codigo} ({top[0].Segmento}) con {Formato.Monto(top[0].Gasto)}. " +
              "Por privacidad se muestran con un código anónimo; su ficha está en Clientes.";
        return Guardar("TopClientes", p.Etiqueta, texto, tabla,
            new GraficoSugerido { Serie = "Gasto (S/)", Etiquetas = top.Select(c => c.Codigo).ToList(), Valores = top.Select(c => (double)c.Gasto).ToList() });
    }

    [KernelFunction("EventosBajaOcupacion")]
    [Description("Eventos de los próximos días con ocupación por debajo de un umbral (0 a 1).")]
    public async Task<string> EventosBajaOcupacionAsync(int dias = 30, double umbral = 0.4, CancellationToken ct = default)
    {
        dias = Math.Clamp(dias, 1, 366);
        umbral = Math.Clamp(umbral > 1 ? umbral / 100 : umbral, 0.01, 1);
        var lista = await _analitica.EventosBajaOcupacionAsync(dias, umbral, ct);
        var periodo = $"próximos {dias} días";
        var tabla = new TablaCifras
        {
            Titulo = $"Eventos de los {periodo} con menos del {umbral * 100:0.#} % de ocupación",
            Columnas = new() { "Evento", "Categoría", "Fecha", "Vendidas / aforo", "Ocupación" },
            Filas = lista.Select(e => new List<string> { e.Evento, e.Categoria, e.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), $"{e.Vendidas:N0} / {e.Aforo:N0}", Pct(e.Ocupacion) }).ToList()
        };
        var texto = lista.Count == 0
            ? $"Ningún evento de los {periodo} está por debajo del {umbral * 100:0.#} % de ocupación."
            : $"{lista.Count} {(lista.Count == 1 ? "evento" : "eventos")} de los {periodo} {(lista.Count == 1 ? "está" : "están")} por debajo del {umbral * 100:0.#} % de ocupación. " +
              $"El más bajo es «{lista[0].Evento}» con {Pct(lista[0].Ocupacion)}: conviene una promoción o un paquete destacado.";
        return Guardar("EventosBajaOcupacion", periodo, texto, tabla,
            new GraficoSugerido { Serie = "Ocupación (%)", Etiquetas = lista.Take(10).Select(e => e.Evento).ToList(), Valores = lista.Take(10).Select(e => Math.Round(e.Ocupacion * 100, 1)).ToList() });
    }

    [KernelFunction("ResumenVentas")]
    [Description("Ingresos, entradas vendidas, compras, ticket promedio y comisiones de la plataforma en un período (fechas yyyy-MM-dd).")]
    public async Task<string> ResumenVentasAsync(string? desde = null, string? hasta = null, CancellationToken ct = default)
    {
        var p = Periodo(desde, hasta);
        var d = await _analitica.DashboardAsync(new FiltroAnalitica { Desde = p.Desde, Hasta = p.Hasta }, ct);
        var tabla = new TablaCifras
        {
            Titulo = $"Resumen de ventas {p.Etiqueta}",
            Columnas = new() { "Indicador", "Valor" },
            Filas = new()
            {
                new() { "Ingresos", Formato.Monto(d.Ingresos) },
                new() { "Entradas vendidas", d.EntradasVendidas.ToString("N0", Pe) },
                new() { "Compras", d.Compras.ToString("N0", Pe) },
                new() { "Ticket promedio", Formato.Monto(d.TicketPromedio) },
                new() { "Comisiones de la plataforma", Formato.Monto(d.Comisiones) }
            }
        };
        var texto = d.Compras == 0
            ? $"No hubo ventas pagadas {p.Etiqueta}."
            : $"{char.ToUpper(p.Etiqueta[0])}{p.Etiqueta[1..]} se vendieron {d.EntradasVendidas:N0} entradas por {Formato.Monto(d.Ingresos)} en {d.Compras:N0} compras " +
              $"(ticket promedio de {Formato.Monto(d.TicketPromedio)}); la plataforma cobró {Formato.Monto(d.Comisiones)} de comisión.";
        return Guardar("ResumenVentas", p.Etiqueta, texto, tabla, null);
    }
}

// =============================
// 🤖 CONSULTAS EN LENGUAJE NATURAL PARA EL ADMINISTRADOR (HU24 / OE6)
// =============================
// Con Llama 3.2 (Ollama) activado, el modelo elige la función (function calling de Semantic Kernel) y redacta.
// Sin LLM (o si no responde), un intérprete de la pregunta elige la función y sus parámetros: el panel funciona
// igual y las cifras son las mismas, porque siempre salen de las funciones de solo lectura.
public class AsistenteAnaliticoService : IAsistenteAnaliticoService
{
    public static readonly string[] Ejemplos =
    {
        "¿Cuáles fueron los 5 eventos más vendidos este mes?",
        "¿Qué categoría generó más ingresos este año?",
        "¿Qué eventos tienen la demanda más inestable?",
        "¿Cuántos clientes VIP hay y cuánto gastan en promedio?",
        "¿Quiénes son los 10 clientes que más gastaron este año?",
        "¿Qué eventos de la próxima semana tienen menos del 40 % de ocupación?"
    };

    private readonly IAnaliticaService _analitica;
    private readonly AsistenteOptions _opciones;
    private readonly ILogger<AsistenteAnaliticoService> _logger;

    public AsistenteAnaliticoService(IAnaliticaService analitica, IOptions<AsistenteOptions> opciones, ILogger<AsistenteAnaliticoService> logger)
    {
        _analitica = analitica;
        _opciones = opciones.Value;
        _logger = logger;
    }

    public async Task<RespuestaAnalitica> ConsultarAsync(string pregunta, string administradorId, CancellationToken ct = default)
    {
        pregunta = (pregunta ?? "").Trim();
        if (pregunta.Length > 300)
            pregunta = pregunta[..300];

        var respuesta = await ResolverAsync(pregunta, ct);

        // Cada consulta a la IA queda en la auditoría (Ley N.° 29733), también las que no se pudieron responder
        await _analitica.RegistrarAuditoriaAsync(administradorId, $"IA: {pregunta}",
            respuesta.Funcion != null ? $"{respuesta.Funcion} ({respuesta.Periodo})" : "Fuera de alcance", ct);
        return respuesta;
    }

    private async Task<RespuestaAnalitica> ResolverAsync(string pregunta, CancellationToken ct)
    {
        var texto = BaseConocimiento.Normalizar(pregunta);
        if (texto.Length == 0)
            return FueraDeAlcance("Escribe una pregunta sobre los eventos o las ventas.");

        // La IA solo lee datos agregados: nunca entrega datos personales ni modifica nada
        if (PideDatosPersonales(texto))
            return FueraDeAlcance("Por privacidad (Ley N.° 29733) no puedo entregar nombres, correos ni datos de contacto de los clientes. " +
                                  "En los resultados se identifican con un código anónimo; su ficha, con registro de auditoría, está en Clientes.");
        if (PideModificar(texto))
            return FueraDeAlcance("Solo puedo consultar datos, no crear, modificar ni eliminar nada. Para eso usa las pantallas de administración.");

        var funciones = new FuncionesAnaliticas(_analitica);
        var kernel = Kernel.CreateBuilder().Build();
        kernel.Plugins.AddFromObject(funciones, FuncionesAnaliticas.NombrePlugin);

        if (_opciones.UsarLlm)
        {
            var conLlm = await ConLlmAsync(pregunta, funciones, ct);
            if (conLlm != null)
                return conLlm;
        }

        var consulta = Interpretar(texto, DateTime.Today);
        if (consulta == null)
            return FueraDeAlcance("Esa pregunta está fuera de lo que puedo consultar. Puedo responder sobre eventos más vendidos, ingresos por categoría, " +
                                  "variabilidad de la demanda, segmentos y mejores clientes, ocupación de los próximos eventos y resumen de ventas. " +
                                  "También puedes usar los filtros del dashboard.");

        var argumentos = new KernelArguments
        {
            ["desde"] = consulta.Periodo.Desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["hasta"] = consulta.Periodo.Hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["n"] = consulta.N,
            ["dias"] = consulta.Dias,
            ["umbral"] = consulta.Umbral,
            ["segmento"] = consulta.Segmento
        };
        await kernel.InvokeAsync(FuncionesAnaliticas.NombrePlugin, consulta.Funcion, argumentos, ct);
        return funciones.Ultima!;
    }

    // Llama 3.2 elige la función y redacta; si no llama a ninguna, falla o cambia alguna cifra, se usa el intérprete
    private async Task<RespuestaAnalitica?> ConLlmAsync(string pregunta, FuncionesAnaliticas funciones, CancellationToken ct)
    {
        try
        {
            var cliente = new OpenAIClient(new ApiKeyCredential("ollama"), new OpenAIClientOptions { Endpoint = new Uri(_opciones.UrlLlm) });
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(_opciones.Modelo, cliente);
            var kernel = builder.Build();
            kernel.Plugins.AddFromObject(funciones, FuncionesAnaliticas.NombrePlugin);

            var historia = new ChatHistory(
                $"Eres el analista de datos de ActivaFest. Hoy es {DateTime.Today:yyyy-MM-dd}. Responde en español en máximo 3 oraciones. " +
                "Para cualquier cifra DEBES llamar a una de las funciones disponibles y usar solo los datos que devuelva; nunca calcules ni inventes números. " +
                "Si ninguna función sirve para la pregunta, responde exactamente: FUERA_DE_ALCANCE.");
            historia.AddUserMessage(pregunta);

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(_opciones.TiempoMaximoSegundos * 3));
            var ajustes = new OpenAIPromptExecutionSettings { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(), Temperature = 0 };
            var mensaje = await kernel.GetRequiredService<IChatCompletionService>().GetChatMessageContentAsync(historia, ajustes, kernel, limite.Token);

            var resultado = funciones.Ultima;
            if (resultado == null)
                return null;

            var redactado = mensaje.Content?.Trim();
            if (!string.IsNullOrEmpty(redactado) && SoloCifrasFuente(redactado, resultado))
            {
                resultado.Texto = redactado;
                resultado.RedactadaPorIa = true;
            }
            return resultado;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("La IA analítica no respondió ({Motivo}); se usa el intérprete de preguntas.", ex.GetBaseException().Message);
            return null;
        }
    }

    // Toda cifra del texto de la IA debe aparecer en el texto base o en la tabla: si inventó un número, se descarta
    public static bool SoloCifrasFuente(string redactado, RespuestaAnalitica fuente)
    {
        var permitido = fuente.Texto + " " + string.Join(" ", fuente.Tabla?.Filas.SelectMany(f => f) ?? Enumerable.Empty<string>()) + " " + fuente.Periodo;
        return Regex.Matches(redactado, @"\d+(?:[.,]\d+)*").All(m => permitido.Contains(m.Value));
    }

    private static RespuestaAnalitica FueraDeAlcance(string texto) => new()
    {
        Texto = texto,
        FueraDeAlcance = true,
        Sugerencias = Ejemplos.Take(4).ToList()
    };

    private static readonly string[] Personales =
        { "correo", "correos", "email", "e mail", "telefono", "celular", "dni", "direccion de", "como se llama", "como se llaman", "nombre del cliente", "nombres de los clientes", "nombre de los clientes", "datos personales", "datos de contacto", "quien es el cliente", "nombre completo" };

    private static readonly string[] Modificar =
        { "borra", "borrar", "elimina", "eliminar", "modifica", "modificar", "cambia", "cambiar", "actualiza", "actualizar", "crea", "crear", "aprueba", "aprobar", "cancela", "cancelar" };

    public static bool PideDatosPersonales(string t) => Personales.Any(p => BaseConocimiento.Contiene(t, p));
    public static bool PideModificar(string t) => Modificar.Any(p => BaseConocimiento.Contiene(t, p));

    // =============================
    // 🧠 INTÉRPRETE DE PREGUNTAS (sin LLM)
    // =============================
    public static ConsultaInterpretada? Interpretar(string t, DateTime hoy)
    {
        bool Hay(params string[] frases) => frases.Any(f => BaseConocimiento.Contiene(t, f));

        var periodo = PeriodoMencionado(t, hoy);
        var segmento = SegmentoRfm.Todos.FirstOrDefault(s => Hay(BaseConocimiento.Normalizar(s), BaseConocimiento.Normalizar(s) + "s"))
                       ?? (Hay("en riesgo") ? SegmentoRfm.EnRiesgo : Hay("frecuentes") ? SegmentoRfm.Frecuente : Hay("nuevos") ? SegmentoRfm.Nuevo : Hay("ocasionales") ? SegmentoRfm.Ocasional : null);

        string? funcion =
            Hay("ocupacion", "poca venta", "baja venta", "bajas ventas", "vendiendo poco", "se venden poco", "medio vacios", "vacios", "llenos")
                ? "EventosBajaOcupacion"
            : Hay("inestable", "inestables", "variabilidad", "variacion", "estable", "estables", "coeficiente", "volatil", "volatiles", "demanda")
                ? "VariabilidadPorEvento"
            : Hay("clientes") && Hay("mas gastaron", "mas gastan", "mas gasto", "mejores", "top", "mas compraron", "mas compran", "ranking", "mas valiosos", "que mas")
                ? "TopClientes"
            : segmento != null || Hay("segmento", "segmentos", "rfm", "cuantos clientes", "tipos de clientes")
                ? "ResumenSegmentos"
            : Hay("categoria", "categorias", "tipo de evento", "tipos de evento", "rubro")
                ? "IngresosPorCategoria"
            : Hay("eventos", "evento", "conciertos", "obras") && Hay("mas vendidos", "mas vendido", "mas vendieron", "vendieron mas", "vendio mas", "top", "mejores", "ranking", "mas exitosos", "mas entradas", "mas populares", "mas vendidas")
                ? "ObtenerTopEventos"
            : Hay("ingresos", "ingreso", "cuanto se vendio", "cuanto vendimos", "cuanto vendio", "ventas", "vendimos", "recaudado", "recaudamos", "facturamos", "facturado", "comision", "comisiones", "ticket promedio", "entradas vendidas", "cuantas entradas", "ganamos", "ganancia")
                ? "ResumenVentas"
            : null;

        if (funcion == null)
            return null;

        var n = NumeroDe(t, funcion == "TopClientes" ? 10 : 5);
        var dias = DiasMencionados(t);
        var umbral = UmbralMencionado(t);
        return new ConsultaInterpretada(funcion, periodo, n, dias, umbral, funcion == "ResumenSegmentos" ? segmento : null);
    }

    private static readonly string[] Meses = { "enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre" };

    // Período de la pregunta (fechas de pago); si no menciona ninguno, los últimos 30 días
    public static PeriodoConsulta PeriodoMencionado(string t, DateTime hoy)
    {
        bool Hay(string f) => BaseConocimiento.Contiene(t, f);
        var anio = Regex.Match(t, @"\b(20\d{2})\b") is { Success: true } a ? int.Parse(a.Groups[1].Value) : (int?)null;

        var ultimos = Regex.Match(t, @"\bultim[oa]s (\d{1,3}) (dias|semanas|meses)\b");
        if (ultimos.Success)
        {
            var k = int.Parse(ultimos.Groups[1].Value);
            var desde = ultimos.Groups[2].Value switch { "dias" => hoy.AddDays(-(k - 1)), "semanas" => hoy.AddDays(-(7 * k - 1)), _ => hoy.AddMonths(-k).AddDays(1) };
            return new PeriodoConsulta(desde, hoy, $"de los últimos {k} {ultimos.Groups[2].Value.Replace("dias", "días")}");
        }
        if (Hay("ultima semana")) return new PeriodoConsulta(hoy.AddDays(-6), hoy, "de la última semana");
        if (Hay("ultimo mes")) return new PeriodoConsulta(hoy.AddDays(-29), hoy, "del último mes");
        if (Hay("ultimo ano")) return new PeriodoConsulta(hoy.AddYears(-1).AddDays(1), hoy, "del último año");
        if (Hay("hoy")) return new PeriodoConsulta(hoy, hoy, "de hoy");
        if (Hay("ayer")) return new PeriodoConsulta(hoy.AddDays(-1), hoy.AddDays(-1), "de ayer");

        var lunes = hoy.AddDays(-(((int)hoy.DayOfWeek + 6) % 7));
        if (Hay("semana pasada")) return new PeriodoConsulta(lunes.AddDays(-7), lunes.AddDays(-1), "de la semana pasada");
        if (Hay("esta semana")) return new PeriodoConsulta(lunes, hoy, "de esta semana");

        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);
        if (Hay("mes pasado")) return new PeriodoConsulta(inicioMes.AddMonths(-1), inicioMes.AddDays(-1), $"de {Meses[inicioMes.AddMonths(-1).Month - 1]} de {inicioMes.AddMonths(-1).Year}");
        if (Hay("este mes")) return new PeriodoConsulta(inicioMes, hoy, $"de {Meses[hoy.Month - 1]} de {hoy.Year}");

        for (var m = 0; m < Meses.Length; m++)
        {
            if (!Hay(Meses[m]) && !(m == 8 && Hay("setiembre")))
                continue;
            // Sin año: la ocurrencia más reciente de ese mes que no sea futura
            var y = anio ?? (m + 1 > hoy.Month ? hoy.Year - 1 : hoy.Year);
            var desde = new DateTime(y, m + 1, 1);
            var hasta = desde.AddMonths(1).AddDays(-1);
            return new PeriodoConsulta(desde, hasta > hoy && y == hoy.Year ? hoy : hasta, $"de {Meses[m]} de {y}");
        }

        if (Hay("ano pasado")) return new PeriodoConsulta(new DateTime(hoy.Year - 1, 1, 1), new DateTime(hoy.Year - 1, 12, 31), $"de {hoy.Year - 1}");
        if (anio != null) return new PeriodoConsulta(new DateTime(anio.Value, 1, 1), anio == hoy.Year ? hoy : new DateTime(anio.Value, 12, 31), $"de {anio}");
        if (Hay("este ano") || Hay("en el ano") || Hay("del ano") || Hay("en lo que va del ano"))
            return new PeriodoConsulta(new DateTime(hoy.Year, 1, 1), hoy, $"de {hoy.Year}");

        return new PeriodoConsulta(hoy.AddDays(-29), hoy, "de los últimos 30 días");
    }

    // "los 5 eventos", "top 10", "10 clientes"
    private static int NumeroDe(string t, int porDefecto)
    {
        var m = Regex.Match(t, @"\b(?:top )?(\d{1,2}) (?:eventos|evento|clientes|cliente|mejores|primeros|mas|conciertos|obras)\b");
        if (!m.Success)
            m = Regex.Match(t, @"\btop (\d{1,2})\b");
        return m.Success ? Math.Clamp(int.Parse(m.Groups[1].Value), 1, 50) : porDefecto;
    }

    private static int DiasMencionados(string t)
    {
        var m = Regex.Match(t, @"\bproxim[oa]s (\d{1,3}) (dias|semanas)\b");
        if (m.Success)
            return int.Parse(m.Groups[1].Value) * (m.Groups[2].Value == "semanas" ? 7 : 1);
        if (BaseConocimiento.Contiene(t, "proxima semana") || BaseConocimiento.Contiene(t, "siguiente semana") || BaseConocimiento.Contiene(t, "esta semana"))
            return 7;
        if (BaseConocimiento.Contiene(t, "proximo mes") || BaseConocimiento.Contiene(t, "siguiente mes"))
            return 30;
        return 30;
    }

    // "menos del 40 %" (la normalización quita el signo %), "40 por ciento"; por defecto 40 %
    private static double UmbralMencionado(string t)
    {
        var m = Regex.Match(t, @"\b(?:menos de(?:l)?|menor(?: a(?:l)?| que el)?|debajo de(?:l)?|bajo el|inferior a(?:l)?|por debajo de(?:l)?)\s+(\d{1,3})\b");
        if (!m.Success)
            m = Regex.Match(t, @"\b(\d{1,3}) por ciento\b");
        return m.Success ? Math.Clamp(int.Parse(m.Groups[1].Value), 1, 100) / 100.0 : 0.40;
    }
}
