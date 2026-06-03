using System.Text.Json;
using MindMatchAI.Data;
using MindMatchAI.Models;
using Newtonsoft.Json.Linq;
using MindMatchAI.Constants;
namespace MindMatchAI.Services.InterviewFlow;
// Loads a job and active questions from the database, ranks them, and maps the result.
public class QuestionSuggestionService
{
    private readonly InterviewDbContext _db;
    private readonly JobQuestionMatcherService _jobQuestionMatcherService;
    private readonly QuestionSuggestionSettings _settings;

    public QuestionSuggestionService(InterviewDbContext db, JobQuestionMatcherService jobQuestionMatcherService)
    {
        _db = db;
        _jobQuestionMatcherService = jobQuestionMatcherService;
        _settings = LoadSettings();
    }

    public List<SuggestedQuestionResult> SuggestQuestionsForJob(Guid jobId, int count = 0)
    {
        if (count <= 0) count = _settings.DefaultSuggestionCount;

        var job = _db.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job == null) throw new Exception("Job not found.");

        var v2Questions = _db.QuestionBankV2.Where(q => q.IsActive).ToList();
        if (!v2Questions.Any()) throw new Exception("No active questions found in QuestionBankV2.");

        var ranked = _jobQuestionMatcherService.RankQuestions(job.Title ?? "", job.Description ?? "", v2Questions, count);
        if (!ranked.Any()) throw new Exception("JobQuestionMatcher did not return any ranked questions.");

        var selected = ranked
            .Select(r => new { Ranked = r, Question = v2Questions.FirstOrDefault(q => q.Id == r.QuestionBankItemId) })
            .Where(x => x.Question != null)
            .ToList();

        if (!selected.Any()) throw new Exception("JobQuestionMatcher returned question IDs that were not found in QuestionBankV2.");

        Console.WriteLine("\nSuggested questions by JobQuestionMatcher only:");
        foreach (var item in selected) Console.WriteLine($"{item.Ranked.QuestionCode} | {item.Ranked.MatchScore}");

        return selected.Take(count)
            .Select(x => MapV2ToSuggestedQuestion(x.Question!, x.Ranked.MatchScore))
            .ToList();
    }

    private SuggestedQuestionResult MapV2ToSuggestedQuestion(QuestionBankV2Item q, double? matchScore)
    {
        string tagsJson = q.ContentCoverageJson;

        if (matchScore.HasValue)
        {
            try
            {
                var obj = JObject.Parse(string.IsNullOrWhiteSpace(q.ContentCoverageJson) ? _settings.DefaultTagsJson : q.ContentCoverageJson);
                obj[_settings.MatchScoreJsonField] = Math.Round(matchScore.Value, _settings.MatchScoreRoundingDigits);
                obj[_settings.MatchedByJsonField] = _settings.MatchedByValue;
                tagsJson = obj.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                tagsJson = q.ContentCoverageJson;
            }
        }

        return new SuggestedQuestionResult
        {
            Id = q.Id,
            QuestionText = q.QuestionText,
            DiagnosticTarget = q.DiagnosticTarget,
            Level = _settings.DefaultLevel,
            RoleFamily = _settings.DefaultRoleFamily,
            TagsJson = tagsJson,
            IsActive = q.IsActive,
            CreatedAtUtc = q.CreatedAt
        };
    }

    private static QuestionSuggestionSettings LoadSettings()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "Config", "job-question-matcher-settings.json");
        if (!File.Exists(configPath)) throw new FileNotFoundException($"Job question matcher settings file was not found: {configPath}");

        string json = File.ReadAllText(configPath);
        var root = JsonSerializer.Deserialize<JobQuestionMatcherSettingsRoot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return root?.QuestionSuggestions ?? throw new InvalidOperationException("job-question-matcher-settings.json is missing QuestionSuggestions section.");
    }

    private class JobQuestionMatcherSettingsRoot { public QuestionSuggestionSettings? QuestionSuggestions { get; set; } }

    private class QuestionSuggestionSettings
    {
        public int DefaultSuggestionCount { get; set; } = 5;
        public int MatchScoreRoundingDigits { get; set; } = 4;
        public string MatchScoreJsonField { get; set; } = "_matchScore";
        public string MatchedByJsonField { get; set; } = "_matchedBy";
        public string MatchedByValue { get; set; } = "JobQuestionMatcherV2";
        public string DefaultLevel { get; set; } = "General";
        public string DefaultRoleFamily { get; set; } = "General";
        public string DefaultTagsJson { get; set; } = "{}";
    }
}

public class SuggestedQuestionResult
{
    public Guid Id { get; set; }
    public string QuestionText { get; set; } = "";
    public string DiagnosticTarget { get; set; } = "";
    public string Level { get; set; } = "";
    public string RoleFamily { get; set; } = "";
    public string TagsJson { get; set; } = JsonDefaults.EmptyObject;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
