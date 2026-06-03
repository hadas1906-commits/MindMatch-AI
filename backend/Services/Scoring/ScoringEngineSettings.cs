using System.Text.Json;
namespace MindMatchAI.Services.Scoring;
public static class ScoringEngineSettingsProvider
{
// Loads and defines configuration classes for the scoring engine.
    private static readonly Lazy<ScoringEngineSettings> Settings = new(LoadSettings);
    public static ScoringEngineSettings Current => Settings.Value;
    private static ScoringEngineSettings LoadSettings()
    {
        string configPath = Path.Combine(AppContext.BaseDirectory, "Config", "scoring-engine-settings.json");
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Scoring engine settings file was not found: {configPath}");
        }
        string json = File.ReadAllText(configPath);
        var root = JsonSerializer.Deserialize<ScoringEngineSettingsRoot>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (root?.ScoringEngine == null)
        {
            throw new InvalidOperationException("scoring-engine-settings.json is missing ScoringEngine section.");
        }
        return root.ScoringEngine;
    }
}
public class ScoringEngineSettingsRoot
{
    public ScoringEngineSettings? ScoringEngine { get; set; }
}
public class ScoringEngineSettings
{
    public string ModelName { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string CompletedInterviewStatus { get; set; } = "";
    public string RelevantStatus { get; set; } = "";
    public double RequiredEvidenceCoverage { get; set; }
    public List<string> KnownFeatures { get; set; } = new();
    public Dictionary<string, double> DefaultFeaturePriorities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FeatureAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public GraphBuilderSettings GraphBuilder { get; set; } = new();
    public ReliabilitySettings Reliability { get; set; } = new();
    public FinalScoreSettings FinalScore { get; set; } = new();
    public PatternSettings Patterns { get; set; } = new();
    public SummarySettings Summary { get; set; } = new();
}
public class GraphBuilderSettings
{
    public double CriticalFeaturePriorityThreshold { get; set; }
    public double FallbackDiagnosticScore { get; set; }
    public double FinalFailedLocalScore { get; set; }
    public double DefaultLocalAnswerScore { get; set; }
    public double DefaultJobWeight { get; set; }
    public double WeakLocalWeightedScoreThreshold { get; set; }
    public double WeakReliabilityThreshold { get; set; }
    public double DefaultMatchScore { get; set; }
    public double MinimumMatchScore { get; set; }
    public double MaximumMatchScore { get; set; }
    public List<string> MatchScorePropertyNames { get; set; } = new();
    public double QuestionImportanceBase { get; set; }
    public double QuestionImportancePerFeature { get; set; }
    public double QuestionImportanceMin { get; set; }
    public double QuestionImportanceMax { get; set; }
    public double ScoreMinFiveScale { get; set; }
    public double ScoreMaxFiveScale { get; set; }
    public double ScoreMinHundredScale { get; set; }
    public double ScoreMaxHundredScale { get; set; }
    public Dictionary<string, double> DefaultExpectedFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public GraphRelationTypesSettings RelationTypes { get; set; } = new();
    public GraphNodePrefixesSettings NodePrefixes { get; set; } = new();
    public GraphRoundingSettings Rounding { get; set; } = new();
}
public class GraphRelationTypesSettings
{
    public string JobRequiresFeature { get; set; } = "";
    public string QuestionTestsFeature { get; set; } = "";
    public string AnswerAnswersQuestion { get; set; } = "";
}
public class GraphNodePrefixesSettings
{
    public string Job { get; set; } = "";
    public string Feature { get; set; } = "";
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
}
public class GraphRoundingSettings
{
    public int FeatureWeight { get; set; }
    public int Score { get; set; }
    public int Coverage { get; set; }
    public int Reliability { get; set; }
    public int EdgeWeight { get; set; }
    public int AnswerReliability { get; set; }
}
public class ReliabilitySettings
{
    public double FinalFailedReliability { get; set; }
    public double InitialReliability { get; set; }
    public int SecondAttemptNumber { get; set; }
    public double SecondAttemptPenalty { get; set; }
    public int ThirdAttemptMinimum { get; set; }
    public double ThirdOrMoreAttemptPenalty { get; set; }
    public double NotRelevantPenalty { get; set; }
    public double LowConfidenceMinimumPositiveValue { get; set; }
    public double LowConfidenceThreshold { get; set; }
    public double LowConfidencePenalty { get; set; }
    public int ShortAnswerWordThreshold { get; set; }
    public double ShortAnswerPenalty { get; set; }
    public double MinimumReliability { get; set; }
    public double MaximumReliability { get; set; }
}
public class FinalScoreSettings
{
    public FinalScoreWeights Weights { get; set; } = new();
    public double MinScore { get; set; }
    public double MaxScore { get; set; }
    public int RoundingDigits { get; set; }
    public int CategoryWeightRoundingDigits { get; set; }
    public double FinalFailedPenaltyPerAnswer { get; set; }
    public double MaxFinalFailedPenalty { get; set; }
    public int MinimumScoresForConsistency { get; set; }
    public double DefaultConsistencyScore { get; set; }
    public double ConsistencyBaseScore { get; set; }
    public double MaxStandardDeviationPenalty { get; set; }
}
public class FinalScoreWeights
{
    public double PriorityWeightedFeatureScore { get; set; }
    public double ReliabilityScore { get; set; }
    public double CoverageScore { get; set; }
    public double RelevanceBehaviorScore { get; set; }
    public double CrossAnswerConsistencyScore { get; set; }
}
public class PatternSettings
{
    public WeakCriticalFeaturePatternSettings WeakCriticalFeature { get; set; } = new();
    public StrongExecutionWeakExplanationPatternSettings StrongExecutionWeakExplanation { get; set; } = new();
    public LowReliabilityDespiteHighScoresPatternSettings LowReliabilityDespiteHighScores { get; set; } = new();
    public ConsistentHighPriorityAlignmentPatternSettings ConsistentHighPriorityAlignment { get; set; } = new();
}
public class WeakCriticalFeaturePatternSettings
{
    public string PatternType { get; set; } = "";
    public double WeightedScoreThreshold { get; set; }
    public double EvidenceCoverageThreshold { get; set; }
    public double Impact { get; set; }
    public string Severity { get; set; } = "";
    public string DescriptionTemplate { get; set; } = "";
}
public class StrongExecutionWeakExplanationPatternSettings
{
    public string PatternType { get; set; } = "";
    public string PracticalAbilitiesFeature { get; set; } = "";
    public string ThinkingQualityFeature { get; set; } = "";
    public double PracticalAbilitiesScoreThreshold { get; set; }
    public double ThinkingQualityScoreThreshold { get; set; }
    public double Impact { get; set; }
    public string Severity { get; set; } = "";
    public string Description { get; set; } = "";
}
public class LowReliabilityDespiteHighScoresPatternSettings
{
    public string PatternType { get; set; } = "";
    public double WeightedScoreThreshold { get; set; }
    public double ReliabilityThreshold { get; set; }
    public double Impact { get; set; }
    public double AffectedFeatureImpact { get; set; }
    public string Severity { get; set; } = "";
    public string Description { get; set; } = "";
}
public class ConsistentHighPriorityAlignmentPatternSettings
{
    public string PatternType { get; set; } = "";
    public double PriorityWeightThreshold { get; set; }
    public int MinimumImportantFeatures { get; set; }
    public double WeightedScoreThreshold { get; set; }
    public double ReliabilityThreshold { get; set; }
    public double EvidenceCoverageThreshold { get; set; }
    public double Impact { get; set; }
    public double AffectedFeatureImpact { get; set; }
    public string Severity { get; set; } = "";
    public string Description { get; set; } = "";
}
public class SummarySettings
{
    public int LatestScoresToPrint { get; set; }
    public int TopFeaturesCount { get; set; }
    public int NegativePatternsCount { get; set; }
    public int PositivePatternsCount { get; set; }
    public double ImportantFeaturePriorityThreshold { get; set; }
    public double WeakImportantFeatureScoreThreshold { get; set; }
    public int SummaryScoreRoundingDigits { get; set; }
    public int FallbackQuestionOrder { get; set; }
    public string PrintSeparator { get; set; } = "";
    public string FinalScoreTemplate { get; set; } = "";
    public string CalculationExplanation { get; set; } = "";
    public string TopFeaturesTemplate { get; set; } = "";
    public string StrongestFeatureTemplate { get; set; } = "";
    public string WeakImportantFeatureTemplate { get; set; } = "";
}