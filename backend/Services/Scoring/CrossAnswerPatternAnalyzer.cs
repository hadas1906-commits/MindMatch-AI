namespace MindMatchAI.Services.Scoring;
// Detects recurring cross-answer patterns that may affect the final candidate score.
public class CrossAnswerPatternAnalyzer
{
    private readonly ScoringEngineSettings _settings;

    public CrossAnswerPatternAnalyzer()
    {
        _settings = ScoringEngineSettingsProvider.Current;
    }

    public List<CrossAnswerPattern> Analyze(CandidateEvidenceGraph graph)
    {
        var patterns = new List<CrossAnswerPattern>();
        DetectWeakCriticalFeatures(graph, patterns);
        DetectStrongExecutionWeakExplanation(graph, patterns);
        DetectLowReliabilityDespiteScores(graph, patterns);
        DetectHighConsistency(graph, patterns);
        return patterns;
    }

    private void DetectWeakCriticalFeatures(CandidateEvidenceGraph graph, List<CrossAnswerPattern> patterns)
    {
        var settings = _settings.Patterns.WeakCriticalFeature;
        foreach (var criticalFeature in graph.JobProfile.CriticalFeatures)
        {
            if (!graph.FeatureEvidence.TryGetValue(criticalFeature, out var evidence))
            {
                continue;
            }

            if (evidence.WeightedScore < settings.WeightedScoreThreshold &&
                evidence.EvidenceCoverage >= settings.EvidenceCoverageThreshold)
            {
                patterns.Add(new CrossAnswerPattern
                {
                    PatternType = settings.PatternType,
                    Severity = settings.Severity,
                    Description = settings.DescriptionTemplate.Replace("{feature}", criticalFeature),
                    ScoreImpact = settings.Impact,
                    AffectedFeatures = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        [criticalFeature] = settings.Impact
                    },
                    RelatedQuestionIds = evidence.WeakQuestionIds
                });
            }
        }
    }

    private void DetectStrongExecutionWeakExplanation(CandidateEvidenceGraph graph, List<CrossAnswerPattern> patterns)
    {
        var settings = _settings.Patterns.StrongExecutionWeakExplanation;
        var hasPractical = graph.FeatureEvidence.TryGetValue(settings.PracticalAbilitiesFeature, out var practical);
        var hasThinking = graph.FeatureEvidence.TryGetValue(settings.ThinkingQualityFeature, out var thinking);

        if (!hasPractical || !hasThinking)
        {
            return;
        }

        if (practical!.WeightedScore >= settings.PracticalAbilitiesScoreThreshold &&
            thinking!.WeightedScore < settings.ThinkingQualityScoreThreshold)
        {
            patterns.Add(new CrossAnswerPattern
            {
                PatternType = settings.PatternType,
                Severity = settings.Severity,
                Description = settings.Description,
                ScoreImpact = settings.Impact,
                AffectedFeatures = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    [settings.ThinkingQualityFeature] = settings.Impact
                }
            });
        }
    }

    private void DetectLowReliabilityDespiteScores(CandidateEvidenceGraph graph, List<CrossAnswerPattern> patterns)
    {
        var settings = _settings.Patterns.LowReliabilityDespiteHighScores;

        var highScoreLowReliability = graph.FeatureEvidence.Values
            .Where(f => f.WeightedScore >= settings.WeightedScoreThreshold &&
                        f.Reliability < settings.ReliabilityThreshold)
            .ToList();

        if (highScoreLowReliability.Count == 0)
        {
            return;
        }

        patterns.Add(new CrossAnswerPattern
        {
            PatternType = settings.PatternType,
            Severity = settings.Severity,
            Description = settings.Description,
            ScoreImpact = settings.Impact,
            AffectedFeatures = highScoreLowReliability.ToDictionary(
                f => f.FeatureName,
                _ => settings.AffectedFeatureImpact,
                StringComparer.OrdinalIgnoreCase)
        });
    }

    private void DetectHighConsistency(CandidateEvidenceGraph graph, List<CrossAnswerPattern> patterns)
    {
        var settings = _settings.Patterns.ConsistentHighPriorityAlignment;
        var importantFeatures = graph.FeatureEvidence.Values
            .Where(f => f.PriorityWeight >= settings.PriorityWeightThreshold)
            .ToList();
        if (importantFeatures.Count < settings.MinimumImportantFeatures)
        {
            return;
        }
        var allStrong = importantFeatures.All(f =>
            f.WeightedScore >= settings.WeightedScoreThreshold &&
            f.Reliability >= settings.ReliabilityThreshold &&
            f.EvidenceCoverage >= settings.EvidenceCoverageThreshold);
        if (allStrong)
        {
            patterns.Add(new CrossAnswerPattern
            {
                PatternType = settings.PatternType,
                Severity = settings.Severity,
                Description = settings.Description,
                ScoreImpact = settings.Impact,
                AffectedFeatures = importantFeatures.ToDictionary(
                    f => f.FeatureName,
                    _ => settings.AffectedFeatureImpact,
                    StringComparer.OrdinalIgnoreCase)
            });
        }
    }
}
