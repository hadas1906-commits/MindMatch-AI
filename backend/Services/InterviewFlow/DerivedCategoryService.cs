using System.Text.Json;
using MindMatchAI.Data;
using MindMatchAI.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MindMatchAI.Constants;
namespace MindMatchAI.Services.InterviewFlow;
//Calculating derived categories: motivation and cultural fit
public class DerivedCategoryService
{
    private readonly InterviewDbContext _db;
    private readonly DerivedCategorySettings _settings;

    public DerivedCategoryService(InterviewDbContext db)
    {
        _db = db;
        _settings = LoadSettings();
    }

    public void CalculateAndSave(Guid interviewAnswerId)
    {
        var diagnosticResults = _db.DiagnosticResults
            .Where(d => d.InterviewAnswerId == interviewAnswerId)
            .ToList();

        var personality = ExtractScores(diagnosticResults, DiagnosticTypes.Traits);
        var thinking = ExtractScores(diagnosticResults, DiagnosticTypes.ThinkingQuality);
        var abilities = ExtractScores(diagnosticResults, DiagnosticTypes.PracticalAbilities);
        var experience = ExtractScores(diagnosticResults, DiagnosticTypes.Experience);

        var derived = DeriveCategory5And6(
            personality: personality,
            thinking: thinking,
            abilities: abilities,
            experience: experience
        );

        var oldDerivedResults = _db.DiagnosticResults
            .Where(d => d.InterviewAnswerId == interviewAnswerId
                     && (d.DiagnosticType == DiagnosticTypes.Motivation
                      || d.DiagnosticType == DiagnosticTypes.CultureFit))
            .ToList();

        if (oldDerivedResults.Count > 0)
        {
            _db.DiagnosticResults.RemoveRange(oldDerivedResults);
            _db.SaveChanges();
        }

        SaveDerivedResult(
            interviewAnswerId,
            diagnosticType: DiagnosticTypes.Motivation,
            result: derived.Motivation
        );

        SaveDerivedResult(
            interviewAnswerId,
            diagnosticType: DiagnosticTypes.CultureFit,
            result: derived.CultureFit
        );

        Console.WriteLine($"Motivation: {derived.Motivation.Score}, Coverage: {derived.Motivation.Coverage}");
        Console.WriteLine($"CultureFit: {derived.CultureFit.Score}, Coverage: {derived.CultureFit.Coverage}");
    }

    private void SaveDerivedResult(
        Guid interviewAnswerId,
        string diagnosticType,
        WeightedScoreResult result)
    {
        var raw = new
        {
            status = result.Score.HasValue ? "success" : "insufficient_data",
            diagnostic_type = diagnosticType,
            score = result.Score,
            coverage = result.Coverage,
            used_signals = result.UsedSignals,
            missing_signals = result.MissingSignals
        };

        var diagnosticResult = new DiagnosticResult
        {
            InterviewAnswerId = interviewAnswerId,
            DiagnosticType = diagnosticType,
            Score = result.Score,
            ResultLabel = result.Score.HasValue ? "Calculated" : "InsufficientData",
            Summary = BuildSummary(diagnosticType, result),
            RawJson = JsonConvert.SerializeObject(raw),
            ModelName = _settings.ModelName,
            ModelVersion = _settings.ModelVersion
        };

        _db.DiagnosticResults.Add(diagnosticResult);
        _db.SaveChanges();
    }

    private static string BuildSummary(string diagnosticType, WeightedScoreResult result)
    {
        if (!result.Score.HasValue)
        {
            return $"{diagnosticType}: not enough diagnostic signals to calculate.";
        }

        return $"{diagnosticType}: {result.Score} / 5. Coverage: {result.Coverage}. Used: {string.Join(", ", result.UsedSignals)}.";
    }

    private DerivedCategoryResult DeriveCategory5And6(
        Dictionary<string, double> personality,
        Dictionary<string, double> thinking,
        Dictionary<string, double> abilities,
        Dictionary<string, double> experience)
    {
        double? O = SafeGet(personality, "O");
        double? C = SafeGet(personality, "C");
        double? E = SafeGet(personality, "E");
        double? A = SafeGet(personality, "A");
        double? N = SafeGet(personality, "N");

        double? emotionalStability = null;

        if (N.HasValue)
        {
            emotionalStability = ClipScore(_settings.ScoreScale.ReverseTraitBase - N.Value);
        }

        double? logic = SafeGet(thinking, "logic");
        double? clarity = SafeGet(thinking, "clarity");
        double? depth = SafeGet(thinking, "depth");

        double? specificity = SafeGet(abilities, "specificity");
        double? ownership = SafeGet(abilities, "ownership");
        double? impact = SafeGet(abilities, "impact");

        double? exposure = SafeGet(experience, "exposure");
        double? complexity = SafeGet(experience, "complexity");
        double? responsibility = SafeGet(experience, "responsibility");

        var motivationSignals = BuildSignals(
            _settings.Categories.Motivation,
            new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            {
                ["C"] = C,
                ["O"] = O,
                ["responsibility"] = responsibility,
                ["ownership"] = ownership,
                ["impact"] = impact,
                ["depth"] = depth,
                ["exposure"] = exposure
            }
        );

        var cultureFitSignals = BuildSignals(
            _settings.Categories.CultureFit,
            new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = A,
                ["E"] = E,
                ["clarity"] = clarity,
                ["emotional_stability"] = emotionalStability,
                ["ownership"] = ownership,
                ["responsibility"] = responsibility,
                ["logic"] = logic
            }
        );

        return new DerivedCategoryResult
        {
            Motivation = WeightedScore(motivationSignals),
            CultureFit = WeightedScore(cultureFitSignals)
        };
    }

    private static List<Signal> BuildSignals(
        Dictionary<string, double> weights,
        Dictionary<string, double?> values)
    {
        var signals = new List<Signal>();

        foreach (var pair in weights)
        {
            values.TryGetValue(pair.Key, out double? value);
            signals.Add(new Signal(pair.Key, pair.Value, value));
        }

        return signals;
    }

    private WeightedScoreResult WeightedScore(List<Signal> signals)
    {
        double totalPossibleWeight = signals.Sum(s => s.Weight);
        double usedWeight = 0.0;
        double weightedSum = 0.0;

        var usedSignals = new List<string>();
        var missingSignals = new List<string>();

        foreach (var signal in signals)
        {
            if (!signal.Value.HasValue)
            {
                missingSignals.Add(signal.Name);
                continue;
            }
            usedWeight += signal.Weight;
            weightedSum += signal.Weight * signal.Value.Value;
            usedSignals.Add(signal.Name);
        }

        if (usedWeight == 0)
        {
            return new WeightedScoreResult
            {
                Score = null,
                Coverage = 0.0,
                UsedSignals = usedSignals,
                MissingSignals = missingSignals
            };
        }

        double score = weightedSum / usedWeight;
        score = ClipScore(score);

        double coverage = totalPossibleWeight > 0
            ? usedWeight / totalPossibleWeight
            : 0.0;

        return new WeightedScoreResult
        {
            Score = Math.Round(score, _settings.ScoreScale.RoundingDigits),
            Coverage = Math.Round(coverage, _settings.ScoreScale.RoundingDigits),
            UsedSignals = usedSignals,
            MissingSignals = missingSignals
        };
    }

    private double ClipScore(double value)
    {
        if (value < _settings.ScoreScale.Min)
        {
            return _settings.ScoreScale.Min;
        }

        if (value > _settings.ScoreScale.Max)
        {
            return _settings.ScoreScale.Max;
        }

        return value;
    }

    private static double? SafeGet(Dictionary<string, double> data, string key)
    {
        foreach (var pair in data)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static Dictionary<string, double> ExtractScores(
        List<DiagnosticResult> diagnosticResults,
        string diagnosticType)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var diagnostic = diagnosticResults
            .Where(d => d.DiagnosticType == diagnosticType)
            .OrderByDescending(d => d.CreatedAtUtc)
            .FirstOrDefault();

        if (diagnostic == null || string.IsNullOrWhiteSpace(diagnostic.RawJson))
        {
            return result;
        }

        try
        {
            JObject raw = JObject.Parse(diagnostic.RawJson);
            string? output = raw["output"]?.ToString();

            if (!string.IsNullOrWhiteSpace(output))
            {
                string? jsonLine = ExtractDiagnosticJsonLine(output);

                if (!string.IsNullOrWhiteSpace(jsonLine))
                {
                    JObject inner = JObject.Parse(jsonLine);
                    ExtractScoresFromJObject(inner, result);
                    return NormalizeKeys(diagnosticType, result);
                }
            }

            ExtractScoresFromJObject(raw, result);
            return NormalizeKeys(diagnosticType, result);
        }
        catch
        {
            return result;
        }
    }

    private static string? ExtractDiagnosticJsonLine(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            string cleanLine = line.Trim();

            if (cleanLine.StartsWith(ModelOutputMarkers.DiagnosticJson))
            {
                return cleanLine.Replace("DIAGNOSTIC_JSON:", "").Trim();
            }
        }

        return null;
    }

    private static void ExtractScoresFromJObject(
        JObject obj,
        Dictionary<string, double> result)
    {
        var scoresToken = obj["scores"];

        if (scoresToken is JObject scoresObj)
        {
            foreach (var property in scoresObj.Properties())
            {
                if (double.TryParse(
                        property.Value?.ToString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double value))
                {
                    result[property.Name] = value;
                }
            }
        }
    }

    private static Dictionary<string, double> NormalizeKeys(
        string diagnosticType,
        Dictionary<string, double> source)
    {
        var normalized = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in source)
        {
            string key = pair.Key.Trim();

            if (diagnosticType == "PracticalAbilities")
            {
                if (key.Equals("Specificity", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["specificity"] = pair.Value;
                    continue;
                }

                if (key.Equals("Ownership", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["ownership"] = pair.Value;
                    continue;
                }

                if (key.Equals("Impact", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["impact"] = pair.Value;
                    continue;
                }
            }

            if (diagnosticType == "Experience")
            {
                if (key.Equals("exposure", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["exposure"] = pair.Value;
                    continue;
                }

                if (key.Equals("complexity", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["complexity"] = pair.Value;
                    continue;
                }

                if (key.Equals("responsibility", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["responsibility"] = pair.Value;
                    continue;
                }
            }

            if (diagnosticType == "ThinkingQuality")
            {
                if (key.Equals("logic", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["logic"] = pair.Value;
                    continue;
                }

                if (key.Equals("clarity", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["clarity"] = pair.Value;
                    continue;
                }

                if (key.Equals("depth", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["depth"] = pair.Value;
                    continue;
                }
            }

            if (diagnosticType == "Traits")
            {
                if (key.Equals("O", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Openness", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["O"] = pair.Value;
                    continue;
                }

                if (key.Equals("C", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Conscientiousness", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["C"] = pair.Value;
                    continue;
                }

                if (key.Equals("E", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Extraversion", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["E"] = pair.Value;
                    continue;
                }

                if (key.Equals("A", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Agreeableness", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["A"] = pair.Value;
                    continue;
                }

                if (key.Equals("N", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Neuroticism", StringComparison.OrdinalIgnoreCase))
                {
                    normalized["N"] = pair.Value;
                    continue;
                }
            }

            normalized[key] = pair.Value;
        }

        return normalized;
    }

    private static DerivedCategorySettings LoadSettings()
    {
        string configPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "scoring-settings.json"
        );

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Scoring settings file was not found: {configPath}"
            );
        }

        string json = File.ReadAllText(configPath);

        var root = System.Text.Json.JsonSerializer.Deserialize<ScoringSettingsRoot>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );

        if (root?.DerivedCategories == null)
        {
            throw new InvalidOperationException(
                "scoring-settings.json is missing DerivedCategories section."
            );
        }

        return root.DerivedCategories;
    }

    private record Signal(string Name, double Weight, double? Value);

    private class DerivedCategoryResult
    {
        public WeightedScoreResult Motivation { get; set; } = new();
        public WeightedScoreResult CultureFit { get; set; } = new();
    }

    private class WeightedScoreResult
    {
        public double? Score { get; set; }
        public double Coverage { get; set; }
        public List<string> UsedSignals { get; set; } = new();
        public List<string> MissingSignals { get; set; } = new();
    }

    private class ScoringSettingsRoot
    {
        public DerivedCategorySettings? DerivedCategories { get; set; }
    }

    private class DerivedCategorySettings
    {
        public string ModelName { get; set; } = "DerivedCategoryFormula";
        public string ModelVersion { get; set; } = "weighted-coverage-v1";
        public ScoreScaleSettings ScoreScale { get; set; } = new();
        public DerivedCategoryWeights Categories { get; set; } = new();
    }

    private class ScoreScaleSettings
    {
        public double Min { get; set; } = 1.0;
        public double Max { get; set; } = 5.0;
        public double ReverseTraitBase { get; set; } = 6.0;
        public int RoundingDigits { get; set; } = 3;
    }

    private class DerivedCategoryWeights
    {
        public Dictionary<string, double> Motivation { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> CultureFit { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}