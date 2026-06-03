using MindMatchAI.Data;
using MindMatchAI.Models;
using MindMatchAI.Services.ModelServer;
using MindMatchAI.Services.Python;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MindMatchAI.Constants;
namespace MindMatchAI.Services.InterviewFlow;
public class DiagnosticModelRunnerService
{//Actually runs the diagnostic models
    private readonly InterviewDbContext _db;
    private readonly PythonRunner _pythonRunner;
    private readonly PythonPaths _paths;
    private readonly PythonModelServerClient _modelServerClient;
    public DiagnosticModelRunnerService(
        InterviewDbContext db,
        PythonRunner pythonRunner,
        PythonPaths paths,
        PythonModelServerClient modelServerClient)
    {
        _db = db;
        _pythonRunner = pythonRunner;
        _paths = paths;
        _modelServerClient = modelServerClient;
    }
    public void RunDiagnosticsByRouting(
        InterviewAnswer answerRecord,
        RoutingDecision routingDecision)
    {
        var diagnosticsToRun = new List<(string DiagnosticType, string ModelName, string ScriptPath)>();
        if (routingDecision.RunTraitsModel)
        {
            diagnosticsToRun.Add((
                DiagnosticType: DiagnosticTypes.Traits,
                ModelName: "BIG_FIVE",
                ScriptPath: _paths.BigFiveScript
            ));
        }
        if (routingDecision.RunExperienceModel)
        {
            diagnosticsToRun.Add((
                DiagnosticType: DiagnosticTypes.Experience,
                ModelName: "ExperienceModel",
                ScriptPath: _paths.ExperienceScript
            ));
        }
        if (routingDecision.RunPracticalAbilitiesModel)
        {
            diagnosticsToRun.Add((
                DiagnosticType: DiagnosticTypes.PracticalAbilities,
                ModelName: "PracticalAbilityModel",
                ScriptPath: _paths.PracticalAbilityScript
            ));
        }
        if (routingDecision.RunThinkingQualityModel)
        {
            diagnosticsToRun.Add((
                DiagnosticType: DiagnosticTypes.ThinkingQuality,
                ModelName: "ThinkingQualityModel",
                ScriptPath: _paths.ThinkingQualityScript
            ));
        }
        if (diagnosticsToRun.Count == 0)
        {
            Console.WriteLine("No diagnostic models were selected by routing.");
            return;
        }
        Console.WriteLine($"\nRunning {diagnosticsToRun.Count} diagnostic models in parallel...");
        var tasks = diagnosticsToRun
            .Select(diagnostic => Task.Run(() =>
                RunDiagnosticWithoutSaving(
                    answerRecord,
                    diagnostic.DiagnosticType,
                    diagnostic.ModelName,
                    diagnostic.ScriptPath
                )
            ))
            .ToList();
        var diagnosticResults = Task.WhenAll(tasks)
            .GetAwaiter()
            .GetResult();
        foreach (var diagnosticResult in diagnosticResults)
        {
            _db.DiagnosticResults.Add(diagnosticResult);
        }
        _db.SaveChanges();
        foreach (var diagnosticResult in diagnosticResults)
        {
            Console.WriteLine($"Diagnostic result saved: {diagnosticResult.DiagnosticType}");
            Console.WriteLine($"Score: {diagnosticResult.Score}");
        }
    }
    private DiagnosticResult RunDiagnosticWithoutSaving(
        InterviewAnswer answerRecord,
        string diagnosticType,
        string modelName,
        string scriptPath)
    {
        Console.WriteLine($"\nRunning diagnostic model: {diagnosticType}");
        try
        {
            string? warmJson = _modelServerClient.RunDiagnostic(
                diagnosticType,
                answerRecord.QuestionText,
                answerRecord.AnswerText
            );
            if (!string.IsNullOrWhiteSpace(warmJson))
            {
                double? extractedScore = TryExtractScoreFromWarmJson(warmJson, diagnosticType);
                string warmModelName = TryExtractModelNameFromJson(warmJson) ?? modelName;
                Console.WriteLine($"Warm diagnostic server returned result for {diagnosticType}.");
                return new DiagnosticResult
                {
                    InterviewAnswerId = answerRecord.Id,
                    DiagnosticType = diagnosticType,
                    Score = extractedScore,
                    ResultLabel = ModelResultLabels.Success,
                    Summary = BuildSummary(warmJson, ""),
                    RawJson = JsonConvert.SerializeObject(new
                    {
                        diagnosticType,
                        modelName = warmModelName,
                        source = ModelSourceNames.WarmModelServer,
                        success = true,
                        output = warmJson,
                        error = "",
                        extractedScore,
                        createdAtUtc = DateTime.UtcNow
                    }),
                    ModelName = warmModelName,
                    ModelVersion = "warm-model-server-v1"
                };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warm diagnostic server failed for {diagnosticType}. Falling back to PythonRunner.");
            Console.WriteLine(ex.Message);
        }
        var result = _pythonRunner.Run(
            scriptPath,
            answerRecord.QuestionText,
            answerRecord.AnswerText
        );
        double? fallbackScore = TryExtractScore(result.Output, diagnosticType);
        string rawJson = JsonConvert.SerializeObject(new
        {
            diagnosticType,
            modelName,
            scriptPath,
            source = ModelSourceNames.PythonRunnerFallback,
            success = result.Success,
            exitCode = result.ExitCode,
            output = result.Output,
            error = result.Error,
            extractedScore = fallbackScore,
            createdAtUtc = DateTime.UtcNow
        });
        return new DiagnosticResult
        {
            InterviewAnswerId = answerRecord.Id,
            DiagnosticType = diagnosticType,
            Score = fallbackScore,
            ResultLabel = result.Success ? ModelResultLabels.Success: ModelResultLabels.ModelError,
            Summary = BuildSummary(result.Output, result.Error),
            RawJson = rawJson,
            ModelName = modelName,
            ModelVersion = "python-runner-fallback"
        };
    }
    private static double? TryExtractScoreFromWarmJson(string json, string diagnosticType)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            JObject obj = JObject.Parse(json);
            if (obj.TryGetValue("score", StringComparison.OrdinalIgnoreCase, out JToken? scoreToken) &&
                double.TryParse(
                    scoreToken?.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double score))
            {
                return score;
            }
            JObject? scoresObj = obj["scores"] as JObject;
            if (scoresObj != null)
            {
                var scores = new List<double>();
                foreach (var property in scoresObj.Properties())
                {
                    if (double.TryParse(
                            property.Value?.ToString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double value))
                    {
                        scores.Add(value);
                    }
                }
                if (scores.Count > 0)
                {
                    return Math.Round(scores.Average(), RuntimeDefaults.ScorePrecision);
                }
            }
        }
        catch
        {
            return TryExtractScore(json, diagnosticType);
        }
        return TryExtractScore(json, diagnosticType);
    }
    private static string? TryExtractModelNameFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            JObject obj = JObject.Parse(json);
            string? modelName =
                obj["model"]?.ToString() ??
                obj["modelName"]?.ToString();

            return string.IsNullOrWhiteSpace(modelName) ? null : modelName;
        }
        catch
        {
            return null;
        }
    }
    private static double? TryExtractScore(string output, string diagnosticType)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }
        double? explicitScore = TryExtractExplicitScore(output);

        if (explicitScore.HasValue)
        {
            return explicitScore.Value;
        }
        if (diagnosticType.Equals("ThinkingQuality", StringComparison.OrdinalIgnoreCase))
        {
            return TryExtractThinkingQualityAverage(output);
        }
        if (diagnosticType.Equals("Traits", StringComparison.OrdinalIgnoreCase))
        {
            return TryExtractBigFiveAverage(output);
        }
        Dictionary<string, double>? jsonScores = TryExtractScoresFromDiagnosticJson(output);
        if (jsonScores != null && jsonScores.Count > 0)
        {
            return Math.Round(jsonScores.Values.Average(), RuntimeDefaults.ScorePrecision);
        }
        return null;
    }
    private static double? TryExtractExplicitScore(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            string cleanLine = line.Trim();
            if (cleanLine.Contains(ModelOutputMarkers.Score, StringComparison.OrdinalIgnoreCase))
            {
                int index = cleanLine.IndexOf(ModelOutputMarkers.Score, StringComparison.OrdinalIgnoreCase);
                string scoreText = cleanLine.Substring(index + ModelOutputMarkers.Score.Length).Trim();
                if (double.TryParse(
                        scoreText,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double score))
                {
                    return score;
                }
            }
        }
        return null;
    }
    private static double? TryExtractThinkingQualityAverage(string output)
    {
        var targetNames = new[]
        {
            "logic",
            "clarity",
            "depth",
            "structure",
            "metacognition",
            "applied"
        };
        var scores = new List<double>();
        foreach (string line in output.Split('\n'))
        {
            string cleanLine = line.Trim();
            foreach (string targetName in targetNames)
            {
                if (!cleanLine.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                double? score = ExtractScoreBeforeSlashFive(cleanLine);
                if (score.HasValue)
                {
                    scores.Add(score.Value);
                }
            }
        }
        if (scores.Count == 0)
        {
            Dictionary<string, double>? jsonScores = TryExtractScoresFromDiagnosticJson(output);
            if (jsonScores != null)
            {
                foreach (string targetName in targetNames)
                {
                    if (jsonScores.TryGetValue(targetName, out double score))
                    {
                        scores.Add(score);
                    }
                }
            }
        }

        if (scores.Count == 0)
        {
            return null;
        }

        return Math.Round(scores.Average(), RuntimeDefaults.ScorePrecision);
    }

    private static double? TryExtractBigFiveAverage(string output)
    {
        var targetNames = new[]
        {
            "Openness",
            "Conscientiousness",
            "Extraversion",
            "Agreeableness",
            "Neuroticism"
        };

        var scores = new List<double>();

        foreach (string line in output.Split('\n'))
        {
            string cleanLine = line.Trim();

            foreach (string targetName in targetNames)
            {
                if (!cleanLine.Contains(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double? score = ExtractScoreBeforeSlashFive(cleanLine);

                if (score.HasValue)
                {
                    scores.Add(score.Value);
                }
            }
        }

        if (scores.Count == 0)
        {
            Dictionary<string, double>? jsonScores = TryExtractScoresFromDiagnosticJson(output);

            if (jsonScores != null)
            {
                foreach (string targetName in targetNames)
                {
                    if (jsonScores.TryGetValue(targetName, out double score))
                    {
                        scores.Add(score);
                    }
                }
            }
        }

        if (scores.Count == 0)
        {
            return null;
        }

        return Math.Round(scores.Average(), RuntimeDefaults.ScorePrecision);
    }

    private static double? ExtractScoreBeforeSlashFive(string line)
    {
        int slashIndex = line.IndexOf("/ 5", StringComparison.OrdinalIgnoreCase);

        if (slashIndex < 0)
        {
            slashIndex = line.IndexOf("/5", StringComparison.OrdinalIgnoreCase);
        }

        if (slashIndex < 0)
        {
            return null;
        }

        string beforeSlash = line.Substring(0, slashIndex);

        var matches = System.Text.RegularExpressions.Regex.Matches(
            beforeSlash,
            @"[-+]?\d+(\.\d+)?"
        );

        if (matches.Count == 0)
        {
            return null;
        }

        string lastNumber = matches[matches.Count - 1].Value;

        if (double.TryParse(
                lastNumber,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double score))
        {
            return Math.Max(1.0, Math.Min(5.0, score));
        }

        return null;
    }

    private static Dictionary<string, double>? TryExtractScoresFromDiagnosticJson(string output)
    {
        foreach (string line in output.Split('\n'))
        {
            string cleanLine = line.Trim();

            string jsonText;

            if (cleanLine.StartsWith(ModelOutputMarkers.DiagnosticJson, StringComparison.OrdinalIgnoreCase))
            {
                jsonText = cleanLine.Substring(ModelOutputMarkers.DiagnosticJson.Length).Trim();
            }
            else if (cleanLine.StartsWith("{") && cleanLine.EndsWith("}"))
            {
                jsonText = cleanLine;
            }
            else
            {
                continue;
            }

            try
            {
                JObject obj = JObject.Parse(jsonText);

                JObject? scoresObj = obj["scores"] as JObject;

                if (scoresObj == null)
                {
                    return null;
                }

                var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (var property in scoresObj.Properties())
                {
                    if (double.TryParse(
                            property.Value?.ToString(),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double score))
                    {
                        scores[property.Name] = score;
                    }
                }

                return scores;
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static string BuildSummary(string output, string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return error.Length > 500
                ? error.Substring(0, 500)
                : error;
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            string clean = output.Trim();

            return clean.Length > 500
                ? clean.Substring(0, 500)
                : clean;
        }

        return "No output returned from diagnostic model.";
    }
}