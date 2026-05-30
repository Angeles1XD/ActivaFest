using Microsoft.ML;
using ActivaFest.Models.ML;

namespace ActivaFest.Services;

public class MachineLearningService
{
    private readonly MLContext _mlContext;
    private ITransformer _intentModel;
    private PredictionEngine<UserInput, IntentPrediction> _intentEngine;

    public MachineLearningService()
    {
        _mlContext = new MLContext(seed: 0);
        TrainModels();
    }

    private void TrainModels()
    {
        var trainingData = new[]
        {
            new UserInput { Text = "¿Qué eventos hay esta semana?", Intent = "Buscar_Evento" },
            new UserInput { Text = "Busco un concierto o festival", Intent = "Buscar_Evento" },
            new UserInput { Text = "¿Dónde puedo ir a bailar?", Intent = "Buscar_Evento" },

            new UserInput { Text = "Tengo un problema con mi entrada", Intent = "Soporte_Tickets" },
            new UserInput { Text = "Ayuda, mi pago no procesa", Intent = "Soporte_Tickets" },
            new UserInput { Text = "¿Cómo cancelo mi reserva?", Intent = "Soporte_Tickets" },

            new UserInput { Text = "Recomiéndame algo para hoy", Intent = "Recomendacion" },
            new UserInput { Text = "¿Qué me sugieres hacer?", Intent = "Recomendacion" },
            new UserInput { Text = "No sé a dónde ir", Intent = "Recomendacion" }
        };

        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        var pipeline = _mlContext.Transforms.Conversion
            .MapValueToKey("Label", nameof(UserInput.Intent))
            .Append(_mlContext.Transforms.Text.FeaturizeText("Features", nameof(UserInput.Text)))
            .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label",
                "Features"))
            .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

        _intentModel = pipeline.Fit(dataView);
        
        _intentEngine =
            _mlContext.Model.CreatePredictionEngine<UserInput, IntentPrediction>(_intentModel);
    }
    
    public string ClasificarIntencion(string mensaje)
    {
        var input = new UserInput { Text = mensaje };
        var prediction = _intentEngine.Predict(input);

        return prediction.PredictedIntent;
    }
    
    public string ObtenerRecomendacion(string userId)
    {
        return "Eventos culturales o musicales cercanos a su ubicación";
    }
}