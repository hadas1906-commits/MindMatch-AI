using System.Text.Json;
using MindMatchAI.Models;
using MindMatchAI.Services.ModelServer;
using MindMatchAI.Services.Python;
using Newtonsoft.Json.Linq;
// Checks whether a candidate answer is relevant to the interview question.namespace MindMatchAI.Services.InterviewFlow;

public class RelevanceService
{
    private readonly PythonRunner _pythonRunner;
    private readonly PythonPaths _paths;
    private readonly PythonModelServerClient? _modelServerClient;
    private readonly RelevanceSettings _settings;

    public RelevanceService(PythonRunner pythonRunner, PythonPaths paths)
    {
        _pythonRunner = pythonRunner;
        _paths = paths;
        _settings = LoadSettings();
    }

    public RelevanceService(PythonRunner pythonRunner, PythonPaths paths, PythonModelServerClient modelServerClient)
    {
        _pythonRunner = pythonRunner;
        _paths = paths;
        _modelServerClient = modelServerClient;
        _settings = LoadSettings();
    }

    public RelevanceResult CheckRelevance(string question, string answer)
    {
        if (_modelServerClient != null)
        {
            try
            {
                string json = _modelServerClient.CheckRelevance(question, answer);
                RelevanceResult? serverResult = ParseModelServerRelevance(json);
                if (serverResult != null)
                {
                    Console.WriteLine("Relevance result returned from warm Python model server.");
                    return serverResult;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warm model server relevance failed. Falling back to PythonRunner. Error: {ex.Message}");
            }
        }

        return CheckRelevanceWithPythonRunner(question, answer);
    }

    private RelevanceResult CheckRelevanceWithPythonRunner(string question, string answer)
    {
        var result = _pythonRunner.Run(_paths.RobertaScript, question, answer);
        if (!result.Success)
        {
            throw new Exception($"ERROR\n{result.Error}");
        }

        foreach (var line in result.Output.Split('\n'))
        {
            string cleanLine = line.Trim();
            if (!cleanLine.StartsWith(_settings.ResultPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Console.WriteLine(cleanLine);

            if (cleanLine.StartsWith(_settings.RelevantPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new RelevanceResult { IsRelevant = true, Score = ExtractScore(cleanLine) };
            }

            if (cleanLine.StartsWith(_settings.IrrelevantPrefix, StringComparison.OrdinalIgnoreCase) ||
                cleanLine.StartsWith(_settings.NotRelevantPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new RelevanceResult { IsRelevant = false, Score = ExtractScore(cleanLine) };
            }
        }

        throw new Exception("The relevance model did not return a valid RESULT.");
    }

    private RelevanceResult? ParseModelServerRelevance(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JObject root = JObject.Parse(json);
        string status = root.Value<string>("status") ?? "";

        if (!status.Equals(_settings.SuccessStatus, StringComparison.OrdinalIgnoreCase)) return null;

        bool? isRelevant = root.Value<bool?>("isRelevant");
        if (isRelevant == null)
        {
            string relevanceStatus = root.Value<string>("relevanceStatus") ?? "";
            isRelevant = relevanceStatus.Equals(_settings.RelevantStatus, StringComparison.OrdinalIgnoreCase) ||
                         relevanceStatus.Equals(_settings.RelevantStatusUpper, StringComparison.OrdinalIgnoreCase);
        }

        return new RelevanceResult
        {
            IsRelevant = isRelevant.GetValueOrDefault(false),
            Score = root.Value<double?>("score")
        };
    }

    private double? ExtractScore(string line)
    {
        int index = line.IndexOf(_settings.ScoreMarker, StringComparison.OrdinalIgnoreCase);
        if (index == -1) return null;

        string scoreText = line.Substring(index + _settings.ScoreMarker.Length).Trim();
        if (double.TryParse(scoreText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double score))
        {
            return score;
        }

        return null;
    }

    private static RelevanceSettings LoadSettings()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "Config", "relevance-settings.json");
        if (!File.Exists(configPath)) throw new FileNotFoundException($"Relevance settings file was not found: {configPath}");

        string json = File.ReadAllText(configPath);
        var root = JsonSerializer.Deserialize<RelevanceSettingsRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return root?.Relevance ?? throw new InvalidOperationException("relevance-settings.json is missing Relevance section.");
    }

    private class RelevanceSettingsRoot { public RelevanceSettings? Relevance { get; set; } }

    private class RelevanceSettings
    {
        public string ResultPrefix { get; set; } = "RESULT:";
        public string RelevantPrefix { get; set; } = "RESULT:RELEVANT";
        public string IrrelevantPrefix { get; set; } = "RESULT:IRRELEVANT";
        public string NotRelevantPrefix { get; set; } = "RESULT:NOT_RELEVANT";
        public string ScoreMarker { get; set; } = "SCORE:";
        public string SuccessStatus { get; set; } = "success";
        public string RelevantStatus { get; set; } = "Relevant";
        public string RelevantStatusUpper { get; set; } = "RELEVANT";
    }
}
