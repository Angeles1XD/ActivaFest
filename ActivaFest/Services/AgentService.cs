using Microsoft.SemanticKernel;
using OpenAI;
using System.ClientModel;
using ActivaFest.Data;
using ActivaFest.Plugins;

namespace ActivaFest.Services;

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
}

public class AgentService
{
    private readonly IServiceProvider _sp;

    public AgentService(IServiceProvider sp)
    {
        _sp = sp;
    }

    public async Task<string> ProcessChatAsync(string userMessage)
    {
        using var scope = _sp.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mlService = scope.ServiceProvider.GetRequiredService<MachineLearningService>();

        var intencion = mlService.ClasificarIntencion(userMessage);
        var recomendacion = mlService.ObtenerRecomendacion("user_123");

        var options = new OpenAIClientOptions { Endpoint = new Uri("http://localhost:11434/v1") };
        var customClient = new OpenAIClient(new ApiKeyCredential("ollama"), options);

        var kernel = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion("llama3.2", customClient)
            .Build();

        string datosEventos = "[]";
        var plugin = new EventosPlugin(db);

        if (intencion == "Buscar_Evento")
        {
            var extractPrompt =
                $"Extrae solo la palabra clave principal (ej. 'rock', 'teatro', 'comida', 'concierto') de este texto: '{userMessage}'. Si no pide nada específico, responde exactamente la palabra 'todos'. NO redactes ninguna frase, solo devuelve una palabra.";
            var keywordResult = await kernel.InvokePromptAsync(extractPrompt);
            string palabraClave = keywordResult.GetValue<string>()?.Trim() ?? "todos";

            datosEventos = await plugin.BuscarEventosAsync(palabraClave);
        }
        else if (intencion == "Recomendacion")
        {
            datosEventos = await plugin.BuscarEventosAsync(recomendacion);
        }

        var prompt = $@"
            Eres el asistente oficial e inteligente de la plataforma ActivaFest.
            El usuario te acaba de enviar este mensaje: '{userMessage}'

            [Análisis Predictivo]
            - Intención: {intencion}

            [Contexto Recuperado (Top 3 Eventos Relevantes)]
            {datosEventos}

            Reglas de comportamiento:
            1. Si la intención es 'Soporte_Tickets', calma al usuario, dile que un agente de soporte lo contactará a su correo. No ofrezcas eventos.
            2. Si la intención es 'Buscar_Evento' o 'Recomendacion', revisa el [Contexto Recuperado]. Si está vacío, dile que no encontraste eventos con esas características. Si hay datos, nárralos de forma atractiva mencionando qué es, dónde y cuándo.
            3. Responde de forma amigable y en un máximo de 2 párrafos.
            4. NUNCA menciones que hiciste una extracción de palabras clave, ni que usaste JSON, ni hables de cómo funciona tu sistema por dentro.
        ";

        var result = await kernel.InvokePromptAsync(prompt);
        return result.GetValue<string>() ?? "Hubo un error al procesar tu consulta.";
    }
}