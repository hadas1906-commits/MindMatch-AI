using System.Text.Json;
using MindMatchAI.Models;
using MindMatchAI.Services.ModelServer;
using MindMatchAI.Services.Python;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MindMatchAI.Services.InterviewFlow;
//Running the failure-reason model when needed
public class DiagnosisService
{
    private readonly PythonRunner _pythonRunner;
    private readonly PythonPaths _paths;
    private readonly PythonModelServerClient? _modelServerClient;
    private readonly DiagnosisSettings _settings;

    public DiagnosisService(PythonRunner pythonRunner, PythonPaths paths)
    {
        _pythonRunner = pythonRunner;
        _paths = paths;
        _settings = LoadSettings();
    }

    public DiagnosisService(
        PythonRunner pythonRunner,
        PythonPaths paths,
        PythonModelServerClient modelServerClient)
    {
        _pythonRunner = pythonRunner;
        _paths = paths;
        _modelServerClient = modelServerClient;
        _settings = LoadSettings();
    }

    public DiagnosisResult? RunDiagnosis(string question, string answer)
    {
        if (_modelServerClient != null)
        {
            try
            {
            
                string json = _modelServerClient.Diagnose(question, answer);
                DiagnosisResult? serverResult = ParseModelServerDiagnosis(json);

                if (serverResult != null)
                {
                    return serverResult;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warm model server diagnosis failed. Falling back to PythonRunner. Error: {ex.Message}");
            }
        }

        return RunDiagnosisWithPythonRunner(question, answer);
    }

    private DiagnosisResult? RunDiagnosisWithPythonRunner(string question, string answer)
    {
        

        var result = _pythonRunner.Run(_paths.ReasonsScript, question, answer);

        if (!result.Success)
        {
            throw new Exception($"\n{result.Error}");
        }

        foreach (var line in result.Output.Split('\n'))
        {
            string cleanLine = line.Trim();

            if (cleanLine.StartsWith(_settings.OutputPrefix))
            {

                string jsonStr = cleanLine
                    .Replace(_settings.OutputPrefix, "")
                    .Trim();

                return JsonConvert.DeserializeObject<DiagnosisResult>(jsonStr);
            }
        }

        return null;
    }

    private DiagnosisResult? ParseModelServerDiagnosis(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JObject root = JObject.Parse(json);

        string status = root.Value<string>("status") ?? "";

        if (!status.Equals(_settings.SuccessStatus, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new DiagnosisResult
        {
            Status = status,
            Reason = root.Value<string>("reason") ?? _settings.DefaultReason,
            Feedback = root.Value<string>("feedback") ??
                       root.Value<string>("feedbackText") ??
                       _settings.DefaultFeedback,
            Why = root.Value<string>("why")
        };
    }

    public DiagnosisFeedback SaveDiagnosis(InterviewAnswer answerRecord, DiagnosisResult diagnosis)
    {
        string diagnosisJson = JsonConvert.SerializeObject(diagnosis);

        answerRecord.DiagnosisJson = diagnosisJson;
        answerRecord.GuidanceMessage = diagnosis.Feedback;

        return new DiagnosisFeedback
        {
            InterviewAnswerId = answerRecord.Id,
            Status = diagnosis.Status,
            Reason = diagnosis.Reason,
            FeedbackText = diagnosis.Feedback,
            Why = diagnosis.Why,
            RawJson = diagnosisJson,
            ModelName = _modelServerClient != null
                ? _settings.WarmModelName
                : _settings.FallbackModelName,
            ModelVersion = _modelServerClient != null
                ? _settings.WarmModelVersion
                : _settings.FallbackModelVersion
        };
    }

    private static DiagnosisSettings LoadSettings()
    {
        string configPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "diagnosis-settings.json"
        );

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Diagnosis settings file was not found: {configPath}"
            );
        }

        string json = File.ReadAllText(configPath);

        var root = System.Text.Json.JsonSerializer.Deserialize<DiagnosisSettingsRoot>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );

        if (root?.Diagnosis == null)
        {
            throw new InvalidOperationException(
                "diagnosis-settings.json is missing Diagnosis section."
            );
        }

        return root.Diagnosis;
    }

    private class DiagnosisSettingsRoot
    {
        public DiagnosisSettings? Diagnosis { get; set; }
    }

    private class DiagnosisSettings
    {
        public string DefaultReason { get; set; } = "too_general";

        public string DefaultFeedback { get; set; } =
            "Please answer with a specific example and clear actions.";

        public string WarmModelName { get; set; } = "WarmModelServer Diagnosis";
        public string FallbackModelName { get; set; } = "MNLI Diagnosis";

        public string WarmModelVersion { get; set; } = "warm-server-v1";
        public string FallbackModelVersion { get; set; } = "current";

        public string OutputPrefix { get; set; } = "DIAGNOSIS_JSON:";
        public string SuccessStatus { get; set; } = "success";
    }
}