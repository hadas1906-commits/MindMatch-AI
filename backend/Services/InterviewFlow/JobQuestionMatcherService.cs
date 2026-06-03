using System.Text.Json;
using MindMatchAI.Models;
using MindMatchAI.Services.ModelServer;
using MindMatchAI.Services.Python;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MindMatchAI.Services.InterviewFlow;
//Selecting and ranking questions from the question bank
public class JobQuestionMatcherService
{
    private readonly PythonRunner _pythonRunner;
    private readonly PythonPaths _paths;
    private readonly PythonModelServerClient _modelServerClient;
    private readonly JobQuestionMatcherSettings _settings;

    public JobQuestionMatcherService(PythonRunner pythonRunner, PythonPaths paths, PythonModelServerClient modelServerClient)
    {
        _pythonRunner = pythonRunner;
        _paths = paths;
        _modelServerClient = modelServerClient;
        _settings = LoadSettings();
    }

    public List<JobQuestionMatchResult> RankQuestions(string jobTitle, string jobDescription, List<QuestionBankV2Item> questions, int count = 0)
    {
        if (count <= 0) count = _settings.DefaultQuestionCount;
        if (questions.Count == 0) return new List<JobQuestionMatchResult>();

        var questionPayload = questions.Select(q => new
        {
            questionBankItemId = q.Id,
            questionCode = q.Code,
            questionText = q.QuestionText,
            questionDiagnosticTarget = q.DiagnosticTarget,
            questionContentTags = SafeParseJsonObject(q.ContentCoverageJson),
            questionRecommendedScoringModels = SafeParseJsonArray(q.RecommendedScoringModelsJson)
        }).ToList();

        Console.WriteLine("\n Running JobQuestionMatcher model...");
        Console.WriteLine($"JobTitle: {jobTitle}");
        Console.WriteLine($"Questions count: {questions.Count}");

        List<JobQuestionMatchResult>? warmResult = TryRankQuestionsWithWarmServer(jobTitle, jobDescription, questionPayload, count);
        if (warmResult != null)
        {
            Console.WriteLine("JobQuestionMatcher returned ranked questions from warm server.");
            return warmResult;
        }

        Console.WriteLine("Warm JobQuestionMatcher unavailable. Falling back to PythonRunner.");

        string questionsJson = JsonConvert.SerializeObject(questionPayload);
        var result = _pythonRunner.Run(_paths.JobQuestionMatcherScript, jobTitle, jobDescription, questionsJson, count.ToString());

        if (!result.Success) throw new Exception($"JobQuestionMatcher failed:\n{result.Error}\n{result.Output}");

        foreach (var line in result.Output.Split('\n'))
        {
            string cleanLine = line.Trim();
            if (!cleanLine.StartsWith(_settings.OutputPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            string jsonText = cleanLine.Replace(_settings.OutputPrefix, "").Trim();
            JObject obj = JObject.Parse(jsonText);
            return ParseRankedQuestions(obj);
        }

        throw new Exception(_settings.MissingOutputError);
    }

    private List<JobQuestionMatchResult>? TryRankQuestionsWithWarmServer(string jobTitle, string jobDescription, object questionPayload, int count)
    {
        try
        {
            string json = _modelServerClient.RankQuestions(jobTitle, jobDescription, questionPayload, count);
            JObject obj = JObject.Parse(json);
            string status = obj["status"]?.ToString() ?? "";

            if (!status.Equals(_settings.SuccessStatus, StringComparison.OrdinalIgnoreCase))
            {
                string message = obj["message"]?.ToString() ?? obj["error"]?.ToString() ?? _settings.UnknownWarmServerError;
                Console.WriteLine($"Warm JobQuestionMatcher returned error: {message}");
                return null;
            }

            return ParseRankedQuestions(obj);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warm JobQuestionMatcher failed: {ex.Message}");
            return null;
        }
    }

    private List<JobQuestionMatchResult> ParseRankedQuestions(JObject obj)
    {
        string status = obj["status"]?.ToString() ?? "";
        if (!status.Equals(_settings.SuccessStatus, StringComparison.OrdinalIgnoreCase))
        {
            string message = obj["message"]?.ToString() ?? _settings.UnknownWarmServerError;
            throw new Exception(message);
        }

        JArray arr = obj["questions"] as JArray ?? new JArray();
        var ranked = new List<JobQuestionMatchResult>();

        foreach (var item in arr)
        {
            Guid questionId = ParseGuid(item["questionBankItemId"]?.ToString());
            if (questionId == Guid.Empty) continue;

            ranked.Add(new JobQuestionMatchResult
            {
                QuestionBankItemId = questionId,
                QuestionCode = item["questionCode"]?.ToString() ?? "",
                QuestionText = item["questionText"]?.ToString() ?? "",
                DiagnosticTarget = item["diagnosticTarget"]?.ToString() ?? "",
                MatchScore = item["matchScore"]?.ToObject<double>() ?? 0
            });
        }

        return ranked;
    }

    private static JObject SafeParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JObject();
        try { return JObject.Parse(json); } catch { return new JObject(); }
    }

    private static JArray SafeParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JArray();
        try { return JArray.Parse(json); } catch { return new JArray(); }
    }

    private static Guid ParseGuid(string? value)
    {
        return Guid.TryParse(value, out Guid id) ? id : Guid.Empty;
    }

    private static JobQuestionMatcherSettings LoadSettings()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "Config", "job-question-matcher-settings.json");
        if (!File.Exists(configPath)) throw new FileNotFoundException($"Job question matcher settings file was not found: {configPath}");

        string json = File.ReadAllText(configPath);
        var root = System.Text.Json.JsonSerializer.Deserialize<JobQuestionMatcherSettingsRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return root?.JobQuestionMatcher ?? throw new InvalidOperationException("job-question-matcher-settings.json is missing JobQuestionMatcher section.");
    }

    private class JobQuestionMatcherSettingsRoot { public JobQuestionMatcherSettings? JobQuestionMatcher { get; set; } }

    private class JobQuestionMatcherSettings
    {
        public int DefaultQuestionCount { get; set; } = 5;
        public string OutputPrefix { get; set; } = "RANKED_QUESTIONS_JSON:";
        public string SuccessStatus { get; set; } = "success";
        public string UnknownWarmServerError { get; set; } = "Unknown warm question matcher error.";
        public string MissingOutputError { get; set; } = "JobQuestionMatcher did not return RANKED_QUESTIONS_JSON.";
    }
}

public class JobQuestionMatchResult
{
    public Guid QuestionBankItemId { get; set; }
    public string QuestionCode { get; set; } = "";
    public string QuestionText { get; set; } = "";
    public string DiagnosticTarget { get; set; } = "";
    public double MatchScore { get; set; }
}
