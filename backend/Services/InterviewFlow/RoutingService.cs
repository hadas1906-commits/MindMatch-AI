using System.Text.Json;
using MindMatchAI.Models;
using MindMatchAI.Services.ModelServer;
using MindMatchAI.Services.Python;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MindMatchAI.Constants;
namespace MindMatchAI.Services.InterviewFlow;
// Determines which diagnostic models should run and adjusts the decision using question metadata.
public class RoutingService
{
    private readonly PythonRunner _pythonRunner;
    private readonly PythonPaths _paths;
    private readonly PythonModelServerClient? _modelServerClient;
    private readonly RoutingSettings _settings;

    public RoutingService(PythonRunner pythonRunner, PythonPaths paths)
    {
        _pythonRunner = pythonRunner;
        _paths = paths;
        _settings = LoadSettings();
    }

    public RoutingService(PythonRunner pythonRunner, PythonPaths paths, PythonModelServerClient modelServerClient)
    {
        _pythonRunner = pythonRunner;
        _paths = paths;
        _modelServerClient = modelServerClient;
        _settings = LoadSettings();
    }

    public RoutingResult? RunRouting(string question, string answer)
    {
        if (_modelServerClient != null)
        {
            try
            {
                Console.WriteLine("Running routing through a warm Python model server...");
                string json = _modelServerClient.Route(question, answer);
                RoutingResult? serverResult = JsonConvert.DeserializeObject<RoutingResult>(json);
                if (serverResult != null && serverResult.Status.Equals(_settings.SuccessStatus, StringComparison.OrdinalIgnoreCase))
                {
                    EnsurePredictedKeys(serverResult);
                    Console.WriteLine("Routing output received from the hot server.");
                    return serverResult;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warm model server routing failed. Falling back to PythonRunner. Error: {ex.Message}");
            }
        }
        return RunRoutingWithPythonRunner(question, answer);
    }

    private RoutingResult? RunRoutingWithPythonRunner(string question, string answer)
    {
        Console.WriteLine("Running a linear routing model...");
        var result = _pythonRunner.Run(_paths.RoutingScript, question, answer);
        if (!result.Success) throw new Exception($"Error running the routing model:\n{result.Error}");
        foreach (var line in result.Output.Split('\n'))
        {
            string cleanLine = line.Trim();
            if (cleanLine.StartsWith(_settings.OutputPrefix, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Routing output received.");
                string jsonStr = cleanLine.Replace(_settings.OutputPrefix, "").Trim();
                return JsonConvert.DeserializeObject<RoutingResult>(jsonStr);
            }
        }
        return null;
    }

    public RoutingResult? RunRouting(string question, string answer, string recommendedScoringModelsJson, string diagnosticCoverageJson, string contentCoverageJson, string answerSignalsJson)
    {
        RoutingResult? rawRouting = RunRouting(question, answer);
        if (rawRouting == null || !rawRouting.Status.Equals(_settings.SuccessStatus, StringComparison.OrdinalIgnoreCase)) return rawRouting;

        Console.WriteLine("\n Applying metadata-aware routing...");
        Console.WriteLine($"RecommendedScoringModelsJson: {recommendedScoringModelsJson}");
        HashSet<string> recommendedModels = ParseRecommendedModels(recommendedScoringModelsJson);
        if (recommendedModels.Count == 0)
        {
            Console.WriteLine("No recommended models found in metadata. Using raw router decision.");
            return rawRouting;
        }

        EnsurePredictedKeys(rawRouting);
        if (!HasEnoughDiagnosticSignal(answer))
        {
            Console.WriteLine("Answer is too short/vague for metadata boosting. Keeping raw router decision.");
            return rawRouting;
        }

        ApplyMetadataAwareDecision(rawRouting, recommendedModels);
        rawRouting.RawMetadataJson = JsonConvert.SerializeObject(new
        {
            routingMode = _settings.RoutingMode,
            recommendedModels = recommendedModels.ToList(),
            diagnosticCoverageJson,
            contentCoverageJson,
            answerSignalsJson,
            answerLength = answer?.Length ?? 0,
            createdAtUtc = DateTime.UtcNow
        });

        Console.WriteLine(" Metadata-aware routing applied.");
        Console.WriteLine($"{_settings.ModelKeys.Traits}: {rawRouting.Predicted.GetValueOrDefault(_settings.ModelKeys.Traits)}");
        Console.WriteLine($"{_settings.ModelKeys.ThinkingQuality}: {rawRouting.Predicted.GetValueOrDefault(_settings.ModelKeys.ThinkingQuality)}");
        Console.WriteLine($"{_settings.ModelKeys.PracticalAbilities}: {rawRouting.Predicted.GetValueOrDefault(_settings.ModelKeys.PracticalAbilities)}");
        Console.WriteLine($"{_settings.ModelKeys.Experience}: {rawRouting.Predicted.GetValueOrDefault(_settings.ModelKeys.Experience)}");
        return rawRouting;
    }

    public RoutingDecision BuildDecision(InterviewAnswer answerRecord, RoutingResult routingResult)
    {
        bool runTraits = routingResult.Predicted.TryGetValue(_settings.ModelKeys.Traits, out int traits) && traits == 1;//בודק אם צריך להריץ
        bool runThinking = routingResult.Predicted.TryGetValue(_settings.ModelKeys.ThinkingQuality, out int thinking) && thinking == 1;
        bool runAbilities = routingResult.Predicted.TryGetValue(_settings.ModelKeys.PracticalAbilities, out int abilities) && abilities == 1;
        bool runExperience = routingResult.Predicted.TryGetValue(_settings.ModelKeys.Experience, out int experience) && experience == 1;
        double? maxConfidence = routingResult.Probabilities.Count > 0 ? routingResult.Probabilities.Values.Max() : null;
        return new RoutingDecision
        {
            InterviewAnswerId = answerRecord.Id,
            RunTraitsModel = runTraits,
            RunThinkingQualityModel = runThinking,
            RunPracticalAbilitiesModel = runAbilities,
            RunExperienceModel = runExperience,
            Confidence = maxConfidence,
            RawJson = JsonConvert.SerializeObject(routingResult),
            ModelName = _settings.ModelName,
            ModelVersion = _settings.ModelVersion
        };
    }

    private void ApplyMetadataAwareDecision(RoutingResult routingResult, HashSet<string> recommendedModels)
    {
        ApplyOneModel(routingResult, _settings.ModelKeys.Traits, IsRecommended(DiagnosticTypes.Traits, recommendedModels));
        ApplyOneModel(routingResult, _settings.ModelKeys.ThinkingQuality, IsRecommended(DiagnosticTypes.ThinkingQuality, recommendedModels));
        ApplyOneModel(routingResult, _settings.ModelKeys.PracticalAbilities, IsRecommended(DiagnosticTypes.PracticalAbilities, recommendedModels));
        ApplyOneModel(routingResult, _settings.ModelKeys.Experience, IsRecommended(DiagnosticTypes.Experience, recommendedModels));
    }

    private bool IsRecommended(string groupName, HashSet<string> recommendedModels)
    {
        if (recommendedModels.Contains(groupName)) return true;
        return _settings.RecommendedModelNames.TryGetValue(groupName, out var names) && names.Any(recommendedModels.Contains);
    }

    private void ApplyOneModel(RoutingResult routingResult, string modelKey, bool isRecommended)
    { 
        int rawPrediction = routingResult.Predicted.TryGetValue(modelKey, out int value) ? value : 0;
        double confidence = GetProbability(routingResult, modelKey);
        routingResult.Predicted[modelKey] = isRecommended
            ? (rawPrediction == 1 || confidence >= _settings.BoostThreshold ? 1 : 0)
            : (rawPrediction == 1 && confidence >= _settings.SecondaryThreshold ? 1 : 0);
    }

    private double GetProbability(RoutingResult routingResult, string modelKey)
    {
        if (routingResult.Probabilities == null || routingResult.Probabilities.Count == 0) return 0.0;
        if (routingResult.Probabilities.TryGetValue(modelKey, out double direct)) return direct;
        if (!_settings.ProbabilityAliases.TryGetValue(modelKey, out var aliases)) return 0.0;
        foreach (string alias in aliases)
        {
            if (routingResult.Probabilities.TryGetValue(alias, out double aliasValue)) return aliasValue;
        }
        return 0.0;
    }

    private void EnsurePredictedKeys(RoutingResult routingResult)
    {
        routingResult.Predicted ??= new Dictionary<string, int>();
        routingResult.Probabilities ??= new Dictionary<string, double>();
        EnsurePredictedKey(routingResult, _settings.ModelKeys.Traits);
        EnsurePredictedKey(routingResult, _settings.ModelKeys.ThinkingQuality);
        EnsurePredictedKey(routingResult, _settings.ModelKeys.PracticalAbilities);
        EnsurePredictedKey(routingResult, _settings.ModelKeys.Experience);
    }

    private static void EnsurePredictedKey(RoutingResult routingResult, string key)
    {
        if (!routingResult.Predicted.ContainsKey(key)) routingResult.Predicted[key] = 0;
    }

    private HashSet<string> ParseRecommendedModels(string json)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            JToken token = JToken.Parse(json);
            if (token is JArray arr)
            {
                foreach (var item in arr)
                {
                    string value = item?.ToString() ?? "";
                    foreach (string normalized in NormalizeModelName(value)) result.Add(normalized);
                }
            }
        }
        catch
        {
            Console.WriteLine(" Could not parse RecommendedScoringModelsJson. Using raw router only.");
        }
        return result;
    }

    private IEnumerable<string> NormalizeModelName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        string clean = value.Trim();
        foreach (var group in _settings.RecommendedModelNames)
        {
            if (group.Key.Equals(clean, StringComparison.OrdinalIgnoreCase) || group.Value.Any(v => v.Equals(clean, StringComparison.OrdinalIgnoreCase)))
            {
                yield return group.Key;
                foreach (string alias in group.Value) yield return alias;
                yield break;
            }
        }
        yield return clean;
    }

    private bool HasEnoughDiagnosticSignal(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return false;
        string trimmed = answer.Trim();
        if (trimmed.Length < _settings.MinimumAnswerLength) return false;
        string lower = trimmed.ToLowerInvariant();
        return !_settings.VagueAnswers.Any(v => lower.Contains(v)) || trimmed.Length >= _settings.ShortVagueAnswerLength;
    }

    private static RoutingSettings LoadSettings()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "Config", "routing-settings.json");
        if (!File.Exists(configPath)) throw new FileNotFoundException($"Routing settings file was not found: {configPath}");
        string json = File.ReadAllText(configPath);
        var root = System.Text.Json.JsonSerializer.Deserialize<RoutingSettingsRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return root?.Routing ?? throw new InvalidOperationException("routing-settings.json is missing Routing section.");
    }

    private class RoutingSettingsRoot { public RoutingSettings? Routing { get; set; } }
    private class RoutingSettings
    {
        public string OutputPrefix { get; set; } = "ROUTING_JSON:";
        public string SuccessStatus { get; set; } = "success";
        public string RoutingMode { get; set; } = "metadata_aware";
        public string ModelName { get; set; } = "LinearRouter+QuestionMetadata";
        public string ModelVersion { get; set; } = "router_model_22k_full_metadata_aware";
        public double BoostThreshold { get; set; } = 0.25;
        public double SecondaryThreshold { get; set; } = 0.65;
        public int MinimumAnswerLength { get; set; } = 40;
        public int ShortVagueAnswerLength { get; set; } = 120;
        public List<string> VagueAnswers { get; set; } = new();
        public ModelKeySettings ModelKeys { get; set; } = new();
        public Dictionary<string, List<string>> RecommendedModelNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> ProbabilityAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private class ModelKeySettings
    {
        public string Traits { get; set; } = "personality";
        public string ThinkingQuality { get; set; } = "thinking";
        public string PracticalAbilities { get; set; } = "abilities";
        public string Experience { get; set; } = "experience";
    }
}
