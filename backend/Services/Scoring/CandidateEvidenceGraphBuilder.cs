using Microsoft.EntityFrameworkCore;
using MindMatchAI.Data;
using MindMatchAI.Models;
using Newtonsoft.Json.Linq;

namespace MindMatchAI.Services.Scoring;

// Builds a candidate evidence graph from interview questions, answers,
// diagnostic results, relevance checks, and scoring configuration.
public class CandidateEvidenceGraphBuilder
{
    private readonly InterviewDbContext _db;
    private readonly ScoringEngineSettings _settings;

    public CandidateEvidenceGraphBuilder(InterviewDbContext db)
    {
        _db = db;
        _settings = ScoringEngineSettingsProvider.Current;
    }

    public CandidateEvidenceGraph Build(Guid interviewId)
    {
        var interview = _db.Interviews
            .Include(i => i.Job)
            .Include(i => i.Candidate)
            .FirstOrDefault(i => i.Id == interviewId);

        if (interview == null)
        {
            throw new InvalidOperationException("Interview not found.");
        }

        var questions = _db.InterviewQuestions
            .Where(q => q.JobId == interview.JobId)
            .OrderBy(q => q.OrderIndex)
            .ToList();

        var answers = _db.InterviewAnswers
            .Where(a => a.InterviewId == interviewId)
            .OrderBy(a => a.AnswerOrder)
            .ThenBy(a => a.AttemptNumber)
            .ThenBy(a => a.CreatedAtUtc)
            .ToList();

        var answerIds = answers.Select(a => a.Id).ToList();

        var diagnosticResults = _db.DiagnosticResults
            .Where(d => answerIds.Contains(d.InterviewAnswerId))
            .ToList();

        var graph = new CandidateEvidenceGraph
        {
            InterviewId = interview.Id,
            JobId = interview.JobId,
            CandidateId = interview.CandidateId
        };

        graph.JobProfile.RequiredEvidenceCoverage = _settings.RequiredEvidenceCoverage;

        BuildQuestions(graph, questions);
        BuildJobProfile(graph);
        BuildAnswers(graph, answers, diagnosticResults);
        BuildFeatureEvidence(graph);
        BuildEdges(graph);

        return graph;
    }

    private void BuildQuestions(
        CandidateEvidenceGraph graph,
        IEnumerable<InterviewQuestion> questions)
    {
        foreach (var question in questions)
        {
            var expectedFeatures = ExtractExpectedFeatures(
                question.DiagnosticCoverageJson,
                question.RecommendedScoringModelsJson,
                question.AnswerSignalsJson,
                question.ContentCoverageJson
            );

            var node = new QuestionEvidenceNode
            {
                QuestionId = question.Id,
                QuestionText = question.QuestionText,
                OrderIndex = question.OrderIndex,
                ExpectedFeatures = expectedFeatures,
                MatchToJobScore = ExtractMatchScore(question.ContentCoverageJson),
                QuestionImportance = CalculateQuestionImportance(expectedFeatures)
            };

            graph.Questions[node.QuestionId] = node;
        }
    }

    private void BuildJobProfile(CandidateEvidenceGraph graph)
    {
        var rawPriorities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var question in graph.Questions.Values)
        {
            foreach (var feature in question.ExpectedFeatures)
            {
                if (!rawPriorities.ContainsKey(feature.Key))
                {
                    rawPriorities[feature.Key] = 0;
                }

                rawPriorities[feature.Key] +=
                    feature.Value * question.MatchToJobScore * question.QuestionImportance;
            }
        }

        if (rawPriorities.Count == 0 || rawPriorities.Values.Sum() <= 0)
        {
            foreach (var item in _settings.DefaultFeaturePriorities)
            {
                rawPriorities[item.Key] = item.Value;
            }
        }

        NormalizeDictionary(rawPriorities);

        foreach (var item in rawPriorities.OrderByDescending(x => x.Value))
        {
            graph.JobProfile.FeaturePriorities[item.Key] = item.Value;

            graph.Features[item.Key] = new FeatureNode
            {
                Name = item.Key,
                PriorityWeight = item.Value,
                IsCritical = item.Value >= _settings.GraphBuilder.CriticalFeaturePriorityThreshold
            };
        }

        graph.JobProfile.CriticalFeatures = graph.Features.Values
            .Where(f => f.IsCritical)
            .Select(f => f.Name)
            .ToList();
    }

    private void BuildAnswers(
        CandidateEvidenceGraph graph,
        IEnumerable<InterviewAnswer> answers,
        IEnumerable<DiagnosticResult> diagnosticResults)
    {
        var diagnosticsByAnswer = diagnosticResults
            .GroupBy(d => d.InterviewAnswerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var answer in answers)
        {
            var questionId = answer.QuestionId;

            if (!graph.Questions.ContainsKey(questionId))
            {
                continue;
            }

            var answerDiagnostics = diagnosticsByAnswer.TryGetValue(answer.Id, out var diagnostics)
                ? diagnostics
                : new List<DiagnosticResult>();

            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var diagnostic in answerDiagnostics)
            {
                if (!diagnostic.Score.HasValue)
                {
                    continue;
                }

                var featureName = NormalizeFeatureName(diagnostic.DiagnosticType);

                scores[featureName] = Math.Round(
                    NormalizeScoreTo100(diagnostic.Score.Value),
                    _settings.GraphBuilder.Rounding.Score);
            }

            var finalFailed =
                answer.IsFinalAttemptForQuestion &&
                !string.Equals(
                    answer.RelevanceStatus,
                    _settings.RelevantStatus,
                    StringComparison.OrdinalIgnoreCase);

            var node = new AnswerEvidenceNode
            {
                AnswerId = answer.Id,
                QuestionId = questionId,
                AnswerText = answer.AnswerText,
                RelevanceStatus = answer.RelevanceStatus ?? "",
                RelevanceConfidence = answer.RelevanceConfidence ?? 0,
                AttemptNumber = answer.AttemptNumber,
                WasCorrectedAfterGuidance = answer.AttemptNumber >= _settings.Reliability.SecondAttemptNumber,
                FinalFailedQuestion = finalFailed,
                FailureReason = ExtractFailureReason(answer),
                DiagnosticScores = scores,
                ReliabilityScore = CalculateReliability(
                    answer.AttemptNumber,
                    answer.RelevanceStatus ?? "",
                    answer.RelevanceConfidence ?? 0,
                    finalFailed,
                    answer.AnswerText ?? "")
            };

            node.LocalWeightedScore = Math.Round(
                CalculateLocalAnswerScore(graph, node),
                _settings.GraphBuilder.Rounding.Score);

            graph.Answers[node.AnswerId] = node;
        }
    }

    private void BuildFeatureEvidence(CandidateEvidenceGraph graph)
    {
        foreach (var feature in graph.Features.Values)
        {
            var relatedQuestions = graph.Questions.Values
                .Where(q => q.ExpectedFeatures.ContainsKey(feature.Name))
                .ToList();

            var representativeAnswers = relatedQuestions
                .Select(q => GetRepresentativeAnswerForQuestion(graph, q.QuestionId))
                .Where(a => a != null)
                .Cast<AnswerEvidenceNode>()
                .ToList();

            var weightedSum = 0.0;
            var weightSum = 0.0;
            var reliabilitySum = 0.0;
            var supportingCount = 0;
            var failedCount = 0;

            foreach (var answer in representativeAnswers)
            {
                var question = graph.Questions[answer.QuestionId];

                var questionFeatureWeight = question.ExpectedFeatures.TryGetValue(feature.Name, out var qWeight)
                    ? qWeight
                    : 0;

                var diagnosticScore = answer.DiagnosticScores.TryGetValue(feature.Name, out var score)
                    ? score
                    : answer.LocalWeightedScore;

                if (diagnosticScore <= 0)
                {
                    diagnosticScore = _settings.GraphBuilder.FallbackDiagnosticScore;
                }

                var evidenceWeight =
                    feature.PriorityWeight *
                    questionFeatureWeight *
                    question.QuestionImportance *
                    answer.ReliabilityScore;

                if (evidenceWeight <= 0)
                {
                    continue;
                }

                weightedSum += diagnosticScore * evidenceWeight;
                weightSum += evidenceWeight;
                reliabilitySum += answer.ReliabilityScore;

                if (answer.FinalFailedQuestion)
                {
                    failedCount++;
                }
                else
                {
                    supportingCount++;
                }
            }

            graph.FeatureEvidence[feature.Name] = new FeatureEvidenceSummary
            {
                FeatureName = feature.Name,
                PriorityWeight = Math.Round(
                    feature.PriorityWeight,
                    _settings.GraphBuilder.Rounding.FeatureWeight),
                WeightedScore = Math.Round(
                    weightSum > 0 ? weightedSum / weightSum : 0,
                    _settings.GraphBuilder.Rounding.Score),
                EvidenceCoverage = relatedQuestions.Count == 0
                    ? 0
                    : Math.Round(
                        Math.Min(
                            _settings.GraphBuilder.MaximumMatchScore,
                            representativeAnswers.Count / (double)relatedQuestions.Count),
                        _settings.GraphBuilder.Rounding.Coverage),
                Reliability = representativeAnswers.Count == 0
                    ? 0
                    : Math.Round(
                        reliabilitySum / representativeAnswers.Count,
                        _settings.GraphBuilder.Rounding.Reliability),
                SupportingAnswersCount = supportingCount,
                FailedAnswersCount = failedCount,
                SupportingQuestionIds = relatedQuestions
                    .Select(q => q.QuestionId)
                    .ToList(),
                WeakQuestionIds = representativeAnswers
                    .Where(a =>
                        a.FinalFailedQuestion ||
                        a.LocalWeightedScore < _settings.GraphBuilder.WeakLocalWeightedScoreThreshold ||
                        a.ReliabilityScore < _settings.GraphBuilder.WeakReliabilityThreshold)
                    .Select(a => a.QuestionId)
                    .Distinct()
                    .ToList()
            };
        }
    }

    private static AnswerEvidenceNode? GetRepresentativeAnswerForQuestion(
        CandidateEvidenceGraph graph,
        Guid questionId)
    {
        return graph.Answers.Values
            .Where(a => a.QuestionId == questionId)
            .OrderByDescending(a => a.AttemptNumber)
            .ThenByDescending(a => a.AnswerId)
            .FirstOrDefault();
    }

    private void BuildEdges(CandidateEvidenceGraph graph)
    {
        foreach (var feature in graph.Features.Values)
        {
            graph.Edges.Add(new EvidenceEdge
            {
                From = $"{_settings.GraphBuilder.NodePrefixes.Job}:{graph.JobId}",
                To = $"{_settings.GraphBuilder.NodePrefixes.Feature}:{feature.Name}",
                RelationType = _settings.GraphBuilder.RelationTypes.JobRequiresFeature,
                Weight = Math.Round(
                    feature.PriorityWeight,
                    _settings.GraphBuilder.Rounding.EdgeWeight)
            });
        }

        foreach (var question in graph.Questions.Values)
        {
            foreach (var feature in question.ExpectedFeatures)
            {
                graph.Edges.Add(new EvidenceEdge
                {
                    From = $"{_settings.GraphBuilder.NodePrefixes.Question}:{question.QuestionId}",
                    To = $"{_settings.GraphBuilder.NodePrefixes.Feature}:{feature.Key}",
                    RelationType = _settings.GraphBuilder.RelationTypes.QuestionTestsFeature,
                    Weight = Math.Round(
                        feature.Value,
                        _settings.GraphBuilder.Rounding.EdgeWeight)
                });
            }
        }

        foreach (var answer in graph.Answers.Values)
        {
            graph.Edges.Add(new EvidenceEdge
            {
                From = $"{_settings.GraphBuilder.NodePrefixes.Answer}:{answer.AnswerId}",
                To = $"{_settings.GraphBuilder.NodePrefixes.Question}:{answer.QuestionId}",
                RelationType = _settings.GraphBuilder.RelationTypes.AnswerAnswersQuestion,
                Weight = Math.Round(
                    answer.ReliabilityScore,
                    _settings.GraphBuilder.Rounding.AnswerReliability)
            });
        }
    }

    private Dictionary<string, double> ExtractExpectedFeatures(params string?[] sources)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in _settings.KnownFeatures)
        {
            foreach (var source in sources)
            {
                if (!string.IsNullOrWhiteSpace(source) &&
                    JsonContainsFeature(source, feature))
                {
                    if (!result.ContainsKey(feature))
                    {
                        result[feature] = 0;
                    }

                    result[feature] += _settings.GraphBuilder.DefaultMatchScore;
                }
            }
        }

        if (result.Count == 0)
        {
            foreach (var item in _settings.GraphBuilder.DefaultExpectedFeatures)
            {
                result[item.Key] = item.Value;
            }
        }

        NormalizeDictionary(result);

        return result;
    }

    private static bool JsonContainsFeature(string source, string feature)
    {
        if (source.Contains(feature, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedFeature = feature.Replace(
            " ",
            "",
            StringComparison.OrdinalIgnoreCase);

        return source.Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Contains(normalizedFeature, StringComparison.OrdinalIgnoreCase);
    }

    private double ExtractMatchScore(string? contentCoverageJson)
    {
        if (string.IsNullOrWhiteSpace(contentCoverageJson))
        {
            return _settings.GraphBuilder.DefaultMatchScore;
        }

        try
        {
            var obj = JObject.Parse(contentCoverageJson);

            foreach (var propertyName in _settings.GraphBuilder.MatchScorePropertyNames)
            {
                if (obj[propertyName] != null &&
                    double.TryParse(obj[propertyName]!.ToString(), out var score))
                {
                    var normalizedScore = score <= _settings.GraphBuilder.MaximumMatchScore
                        ? score
                        : score / _settings.GraphBuilder.ScoreMaxHundredScale;

                    return Clamp(
                        normalizedScore,
                        _settings.GraphBuilder.MinimumMatchScore,
                        _settings.GraphBuilder.MaximumMatchScore);
                }
            }
        }
        catch
        {
            // Ignore invalid metadata and use the default match score.
        }

        return _settings.GraphBuilder.DefaultMatchScore;
    }

    private double CalculateLocalAnswerScore(
        CandidateEvidenceGraph graph,
        AnswerEvidenceNode answer)
    {
        if (!graph.Questions.TryGetValue(answer.QuestionId, out var question))
        {
            return _settings.GraphBuilder.DefaultLocalAnswerScore;
        }

        var weightedSum = 0.0;
        var weightSum = 0.0;

        foreach (var expectedFeature in question.ExpectedFeatures)
        {
            var score = answer.DiagnosticScores.TryGetValue(expectedFeature.Key, out var diagnosticScore)
                ? diagnosticScore
                : _settings.GraphBuilder.DefaultLocalAnswerScore;

            if (score <= 0)
            {
                continue;
            }

            var jobWeight = graph.JobProfile.FeaturePriorities.TryGetValue(expectedFeature.Key, out var jw)
                ? jw
                : _settings.GraphBuilder.DefaultJobWeight;

            var weight = jobWeight * expectedFeature.Value * answer.ReliabilityScore;

            weightedSum += score * weight;
            weightSum += weight;
        }

        if (weightSum > 0)
        {
            return weightedSum / weightSum;
        }

        if (answer.FinalFailedQuestion)
        {
            return _settings.GraphBuilder.FinalFailedLocalScore;
        }

        if (answer.DiagnosticScores.Count > 0)
        {
            return answer.DiagnosticScores.Values.Average();
        }

        return _settings.GraphBuilder.DefaultLocalAnswerScore;
    }

    private double CalculateReliability(
        int attemptNumber,
        string relevanceStatus,
        double relevanceConfidence,
        bool finalFailed,
        string answerText)
    {
        if (finalFailed)
        {
            return _settings.Reliability.FinalFailedReliability;
        }

        var reliability = _settings.Reliability.InitialReliability;

        if (attemptNumber == _settings.Reliability.SecondAttemptNumber)
        {
            reliability -= _settings.Reliability.SecondAttemptPenalty;
        }
        else if (attemptNumber >= _settings.Reliability.ThirdAttemptMinimum)
        {
            reliability -= _settings.Reliability.ThirdOrMoreAttemptPenalty;
        }

        if (!string.Equals(
                relevanceStatus,
                _settings.RelevantStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            reliability -= _settings.Reliability.NotRelevantPenalty;
        }

        if (relevanceConfidence > _settings.Reliability.LowConfidenceMinimumPositiveValue &&
            relevanceConfidence < _settings.Reliability.LowConfidenceThreshold)
        {
            reliability -= _settings.Reliability.LowConfidencePenalty;
        }

        var wordCount = answerText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Length;

        if (wordCount < _settings.Reliability.ShortAnswerWordThreshold)
        {
            reliability -= _settings.Reliability.ShortAnswerPenalty;
        }

        return Math.Round(
            Clamp(
                reliability,
                _settings.Reliability.MinimumReliability,
                _settings.Reliability.MaximumReliability),
            _settings.GraphBuilder.Rounding.AnswerReliability);
    }

    private double CalculateQuestionImportance(Dictionary<string, double> expectedFeatures)
    {
        return expectedFeatures.Count == 0
            ? _settings.GraphBuilder.QuestionImportanceMin
            : Clamp(
                _settings.GraphBuilder.QuestionImportanceBase +
                expectedFeatures.Count * _settings.GraphBuilder.QuestionImportancePerFeature,
                _settings.GraphBuilder.QuestionImportanceMin,
                _settings.GraphBuilder.QuestionImportanceMax);
    }

    private static string? ExtractFailureReason(InterviewAnswer answer)
    {
        if (!string.IsNullOrWhiteSpace(answer.GuidanceMessage))
        {
            return answer.GuidanceMessage;
        }

        if (!string.IsNullOrWhiteSpace(answer.DiagnosisJson))
        {
            return answer.DiagnosisJson;
        }

        return null;
    }

    private string NormalizeFeatureName(string value)
    {
        var cleaned = value.Trim();

        if (_settings.FeatureAliases.TryGetValue(cleaned, out string? normalized))
        {
            return normalized;
        }

        return cleaned.Replace(" ", "");
    }

    private double NormalizeScoreTo100(double score)
    {
        if (score <= _settings.GraphBuilder.ScoreMaxFiveScale)
        {
            return ((Clamp(
                         score,
                         _settings.GraphBuilder.ScoreMinFiveScale,
                         _settings.GraphBuilder.ScoreMaxFiveScale)
                     - _settings.GraphBuilder.ScoreMinFiveScale)
                    / (_settings.GraphBuilder.ScoreMaxFiveScale - _settings.GraphBuilder.ScoreMinFiveScale))
                   * _settings.GraphBuilder.ScoreMaxHundredScale;
        }

        return Clamp(
            score,
            _settings.GraphBuilder.ScoreMinHundredScale,
            _settings.GraphBuilder.ScoreMaxHundredScale);
    }

    private static void NormalizeDictionary(Dictionary<string, double> dictionary)
    {
        var total = dictionary.Values.Sum();

        if (total <= 0)
        {
            return;
        }

        foreach (var key in dictionary.Keys.ToList())
        {
            dictionary[key] = dictionary[key] / total;
        }
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}