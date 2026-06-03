namespace MindMatchAI.Services.Scoring;
// Composes the final interview score from evidence graph metrics,
// reliability, coverage, relevance behavior, consistency, and detected patterns.
public class FinalScoreComposer
{
    private readonly FinalScoreSettings _settings;
    public FinalScoreComposer()
    {
        _settings = ScoringEngineSettingsProvider.Current.FinalScore;
    }
    public FinalScoreBreakdown Compose(CandidateEvidenceGraph graph)
    {
        var priorityWeightedFeatureScore = CalculatePriorityWeightedFeatureScore(graph);
        var reliabilityScore = CalculateReliabilityScore(graph);
        var coverageScore = CalculateCoverageScore(graph);
        var relevanceBehaviorScore = CalculateRelevanceBehaviorScore(graph);
        var crossAnswerConsistencyScore = CalculateConsistencyScore(graph);
        var patternAdjustment = graph.Patterns.Sum(p => p.ScoreImpact);
        var finalScore =
            _settings.Weights.PriorityWeightedFeatureScore * priorityWeightedFeatureScore +
            _settings.Weights.ReliabilityScore * reliabilityScore +
            _settings.Weights.CoverageScore * coverageScore +
            _settings.Weights.RelevanceBehaviorScore * relevanceBehaviorScore +
            _settings.Weights.CrossAnswerConsistencyScore * crossAnswerConsistencyScore +
            patternAdjustment;
        finalScore = Clamp(finalScore, _settings.MinScore, _settings.MaxScore);
        return new FinalScoreBreakdown
        {
            PriorityWeightedFeatureScore = Math.Round(priorityWeightedFeatureScore, _settings.RoundingDigits),
            ReliabilityScore = Math.Round(reliabilityScore, _settings.RoundingDigits),
            CoverageScore = Math.Round(coverageScore, _settings.RoundingDigits),
            RelevanceBehaviorScore = Math.Round(relevanceBehaviorScore, _settings.RoundingDigits),
            CrossAnswerConsistencyScore = Math.Round(crossAnswerConsistencyScore, _settings.RoundingDigits),
            PatternAdjustment = Math.Round(patternAdjustment, _settings.RoundingDigits),
            FinalScore = Math.Round(finalScore, _settings.RoundingDigits),
            CategoryScores = graph.FeatureEvidence.ToDictionary(
                f => f.Key,
                f => Math.Round(f.Value.WeightedScore, _settings.RoundingDigits),
                StringComparer.OrdinalIgnoreCase),
            CategoryWeights = graph.Features.ToDictionary(
                f => f.Key,
                f => Math.Round(f.Value.PriorityWeight, _settings.CategoryWeightRoundingDigits),
                StringComparer.OrdinalIgnoreCase)
        };
    }
    private static double CalculatePriorityWeightedFeatureScore(CandidateEvidenceGraph graph)
    {
        var weightedSum = 0.0;
        var weightSum = 0.0;
        foreach (var feature in graph.FeatureEvidence.Values)
        {
            var weight = feature.PriorityWeight;
            if (weight <= 0 || feature.EvidenceCoverage <= 0)
            {
                continue;
            }
            weightedSum += feature.WeightedScore * weight;
            weightSum += weight;
        }
        return weightSum > 0 ? weightedSum / weightSum : 0;
    }
    private static double CalculateReliabilityScore(CandidateEvidenceGraph graph)
    {
        var representativeAnswers = GetRepresentativeAnswers(graph).ToList();
        return representativeAnswers.Count == 0
            ? 0
            : representativeAnswers.Average(a => a.ReliabilityScore) * ScoringEngineSettingsProvider.Current.FinalScore.MaxScore;
    }
    private static double CalculateCoverageScore(CandidateEvidenceGraph graph)
    {
        if (graph.FeatureEvidence.Count == 0)
        {
            return 0;
        }
        var weightedCoverage = 0.0;
        var weightSum = 0.0;
        foreach (var feature in graph.FeatureEvidence.Values)
        {
            weightedCoverage += feature.EvidenceCoverage * feature.PriorityWeight;
            weightSum += feature.PriorityWeight;
        }
        return weightSum > 0
            ? (weightedCoverage / weightSum) * ScoringEngineSettingsProvider.Current.FinalScore.MaxScore
            : 0;
    }
    private double CalculateRelevanceBehaviorScore(CandidateEvidenceGraph graph)
    {
        var representativeAnswers = GetRepresentativeAnswers(graph).ToList();

        if (representativeAnswers.Count == 0)
        {
            return 0;
        }

        var total = representativeAnswers.Count;
        var relevant = representativeAnswers.Count(a =>
            string.Equals(a.RelevanceStatus, ScoringEngineSettingsProvider.Current.RelevantStatus, StringComparison.OrdinalIgnoreCase));
        var finalFailed = representativeAnswers.Count(a => a.FinalFailedQuestion);
        var baseScore = (relevant / (double)total) * _settings.MaxScore;
        var failedPenalty = Math.Min(_settings.MaxFinalFailedPenalty, finalFailed * _settings.FinalFailedPenaltyPerAnswer);

        return Clamp(baseScore - failedPenalty, _settings.MinScore, _settings.MaxScore);
    }

    private double CalculateConsistencyScore(CandidateEvidenceGraph graph)
    {
        var scores = graph.FeatureEvidence.Values
            .Where(f => f.EvidenceCoverage > 0)
            .Select(f => f.WeightedScore)
            .ToList();

        if (scores.Count < _settings.MinimumScoresForConsistency)
        {
            return _settings.DefaultConsistencyScore;
        }

        var average = scores.Average();
        var variance = scores.Sum(s => Math.Pow(s - average, 2)) / scores.Count;
        var standardDeviation = Math.Sqrt(variance);
        var consistency = _settings.ConsistencyBaseScore - Math.Min(_settings.MaxStandardDeviationPenalty, standardDeviation);

        return Clamp(consistency, _settings.MinScore, _settings.MaxScore);
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

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
