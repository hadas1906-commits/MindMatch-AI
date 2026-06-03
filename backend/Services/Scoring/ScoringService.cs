using Microsoft.EntityFrameworkCore;
using MindMatchAI.Data;
using MindMatchAI.Models;
using MindMatchAI.Services.Scoring;
using Newtonsoft.Json;
// Runs the end-to-end scoring flow and saves the final candidate score.
namespace MindMatchAI.Services.InterviewFlow;
public class ScoringService
{
    private readonly InterviewDbContext _db;
    private readonly CandidateEvidenceGraphBuilder _graphBuilder;
    private readonly CrossAnswerPatternAnalyzer _patternAnalyzer;
    private readonly FinalScoreComposer _scoreComposer;
    private readonly ScoringEngineSettings _settings;

    public ScoringService(
        InterviewDbContext db,
        CandidateEvidenceGraphBuilder graphBuilder,
        CrossAnswerPatternAnalyzer patternAnalyzer,
        FinalScoreComposer scoreComposer)
    {
        _db = db;
        _graphBuilder = graphBuilder;
        _patternAnalyzer = patternAnalyzer;
        _scoreComposer = scoreComposer;
        _settings = ScoringEngineSettingsProvider.Current;
    }

    public FinalCandidateScore? CalculateAndSaveLatestInterviewScore()
    {
        var latestInterview = _db.Interviews
            .OrderByDescending(i => i.StartedAtUtc)
            .FirstOrDefault();

        if (latestInterview == null)
        {
            return null;
        }

        return CalculateAndSaveInterviewScore(latestInterview.Id);
    }

    public FinalCandidateScore? CalculateAndSaveInterviewScore(Guid interviewId)
    {
        var interview = _db.Interviews
            .Include(i => i.Candidate)
            .Include(i => i.Job)
            .ThenInclude(j => j.Company)
            .FirstOrDefault(i => i.Id == interviewId);

        if (interview == null)
        {
            return null;
        }

        var graph = _graphBuilder.Build(interviewId);
        graph.Patterns = _patternAnalyzer.Analyze(graph);
        graph.FinalBreakdown = _scoreComposer.Compose(graph);

        var representativeAnswers = GetRepresentativeAnswers(graph).ToList();
        var rawJson = BuildRawJson(graph, representativeAnswers);

        var finalCandidateScore = new FinalCandidateScore
        {
            InterviewId = interview.Id,
            FinalScore = graph.FinalBreakdown.FinalScore,
            Summary = BuildSummary(graph, interview),
            RawJson = JsonConvert.SerializeObject(rawJson, Formatting.Indented),
            ModelVersion = _settings.ModelVersion
        };

        _db.FinalCandidateScores.Add(finalCandidateScore);

        interview.Status = _settings.CompletedInterviewStatus;
        interview.FinishedAtUtc = DateTime.UtcNow;

        _db.SaveChanges();

        Console.WriteLine("Final score was saved to FinalCandidateScores.");
        Console.WriteLine($"InterviewId: {interview.Id}");
        Console.WriteLine($"FinalScore: {finalCandidateScore.FinalScore}");
        Console.WriteLine($"ModelVersion: {finalCandidateScore.ModelVersion}");

        return finalCandidateScore;
    }

    public void PrintLatestFinalScores()
    {
        var finalScores = _db.FinalCandidateScores
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(_settings.Summary.LatestScoresToPrint)
            .ToList();

        foreach (var score in finalScores)
        {
            Console.WriteLine($"ScoreId: {score.Id}");
            Console.WriteLine($"InterviewId: {score.InterviewId}");
            Console.WriteLine($"FinalScore: {score.FinalScore}");
            Console.WriteLine($"Summary: {score.Summary}");
            Console.WriteLine($"ModelVersion: {score.ModelVersion}");
            Console.WriteLine(_settings.Summary.PrintSeparator);
        }
    }

    private object BuildRawJson(
        CandidateEvidenceGraph graph,
        List<AnswerEvidenceNode> representativeAnswers)
    {
        return new
        {
            scoringModel = _settings.ModelName,
            modelVersion = _settings.ModelVersion,
            graph.InterviewId,
            graph.JobId,
            graph.CandidateId,
            totals = new
            {
                questions = graph.Questions.Count,
                answers = graph.Answers.Count,
                representativeAnswers = representativeAnswers.Count,
                edges = graph.Edges.Count,
                features = graph.Features.Count,
                patterns = graph.Patterns.Count
            },
            finalBreakdown = graph.FinalBreakdown,
            jobRequirementProfile = new
            {
                featurePriorities = graph.JobProfile.FeaturePriorities,
                criticalFeatures = graph.JobProfile.CriticalFeatures,
                requiredEvidenceCoverage = graph.JobProfile.RequiredEvidenceCoverage
            },
            featureEvidence = graph.FeatureEvidence
                .OrderByDescending(f => f.Value.PriorityWeight)
                .ToDictionary(
                    f => f.Key,
                    f => new
                    {
                        f.Value.PriorityWeight,
                        f.Value.WeightedScore,
                        f.Value.EvidenceCoverage,
                        f.Value.Reliability,
                        f.Value.SupportingAnswersCount,
                        f.Value.FailedAnswersCount,
                        f.Value.SupportingQuestionIds,
                        f.Value.WeakQuestionIds
                    }),
            questionNodes = graph.Questions.Values
                .OrderBy(q => q.OrderIndex)
                .Select(q => new
                {
                    q.QuestionId,
                    q.OrderIndex,
                    q.QuestionText,
                    q.ExpectedFeatures,
                    q.MatchToJobScore,
                    q.QuestionImportance
                }),
            answerEvidence = representativeAnswers
                .OrderBy(a =>
                    graph.Questions.TryGetValue(a.QuestionId, out var q)
                        ? q.OrderIndex
                        : _settings.Summary.FallbackQuestionOrder)
                .Select(a => new
                {
                    a.AnswerId,
                    a.QuestionId,
                    a.RelevanceStatus,
                    a.RelevanceConfidence,
                    a.AttemptNumber,
                    a.WasCorrectedAfterGuidance,
                    a.FinalFailedQuestion,
                    a.FailureReason,
                    a.DiagnosticScores,
                    a.ReliabilityScore,
                    a.LocalWeightedScore
                }),
            crossAnswerPatterns = graph.Patterns.Select(p => new
            {
                p.PatternType,
                p.Severity,
                p.Description,
                p.ScoreImpact,
                p.AffectedFeatures,
                p.RelatedQuestionIds,
                p.RelatedAnswerIds
            }),
            graphEdges = graph.Edges.Select(e => new
            {
                e.From,
                e.To,
                e.RelationType,
                e.Weight
            })
        };
    }

    private string BuildSummary(CandidateEvidenceGraph graph, Interview interview)
    {
        var topFeatures = graph.FeatureEvidence.Values
            .OrderByDescending(f => f.PriorityWeight)
            .Take(_settings.Summary.TopFeaturesCount)
            .Select(f => $"{f.FeatureName}: {Math.Round(f.WeightedScore, _settings.Summary.SummaryScoreRoundingDigits)}")
            .ToList();

        var strongestFeature = graph.FeatureEvidence.Values
            .Where(f => f.EvidenceCoverage > 0)
            .OrderByDescending(f => f.WeightedScore)
            .FirstOrDefault();

        var weakestImportantFeature = graph.FeatureEvidence.Values
            .Where(f => f.PriorityWeight >= _settings.Summary.ImportantFeaturePriorityThreshold &&
                        f.EvidenceCoverage > 0)
            .OrderBy(f => f.WeightedScore)
            .FirstOrDefault();

        var negativePatterns = graph.Patterns
            .Where(p => p.ScoreImpact < 0)
            .OrderBy(p => p.ScoreImpact)
            .Take(_settings.Summary.NegativePatternsCount)
            .Select(p => p.Description)
            .ToList();

        var positivePatterns = graph.Patterns
            .Where(p => p.ScoreImpact > 0)
            .OrderByDescending(p => p.ScoreImpact)
            .Take(_settings.Summary.PositivePatternsCount)
            .Select(p => p.Description)
            .ToList();

        var summaryParts = new List<string>
        {
            _settings.Summary.FinalScoreTemplate
                .Replace("{jobTitle}", interview.Job.Title)
                .Replace("{finalScore}", graph.FinalBreakdown.FinalScore.ToString()),
            _settings.Summary.CalculationExplanation
        };

        if (topFeatures.Count > 0)
        {
            summaryParts.Add(_settings.Summary.TopFeaturesTemplate.Replace("{features}", string.Join(", ", topFeatures)));
        }

        if (strongestFeature != null)
        {
            summaryParts.Add(_settings.Summary.StrongestFeatureTemplate.Replace("{feature}", strongestFeature.FeatureName));
        }

        if (weakestImportantFeature != null &&
            weakestImportantFeature.WeightedScore < _settings.Summary.WeakImportantFeatureScoreThreshold)
        {
            summaryParts.Add(_settings.Summary.WeakImportantFeatureTemplate.Replace("{feature}", weakestImportantFeature.FeatureName));
        }

        summaryParts.AddRange(negativePatterns);
        summaryParts.AddRange(positivePatterns);

        return string.Join(" ", summaryParts);
    }

    private static IEnumerable<AnswerEvidenceNode> GetRepresentativeAnswers(CandidateEvidenceGraph graph)
    {
        return graph.Questions.Keys
            .Select(questionId => graph.Answers.Values
                .Where(a => a.QuestionId == questionId)
                .OrderByDescending(a => a.AttemptNumber)
                .FirstOrDefault())
            .Where(a => a != null)
            .Cast<AnswerEvidenceNode>();
    }
}
