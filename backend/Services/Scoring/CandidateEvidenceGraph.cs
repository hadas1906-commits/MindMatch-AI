namespace MindMatchAI.Services.Scoring;
public class CandidateEvidenceGraph
{
// Defines the data structures used by the scoring engine evidence graph.
    public Guid InterviewId { get; set; }
    public Guid JobId { get; set; }
    public Guid CandidateId { get; set; }
    public JobRequirementProfile JobProfile { get; set; } = new();
    public Dictionary<string, FeatureNode> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<Guid, QuestionEvidenceNode> Questions { get; set; } = new();
    public Dictionary<Guid, AnswerEvidenceNode> Answers { get; set; } = new();
    public List<EvidenceEdge> Edges { get; set; } = new();
    public Dictionary<string, FeatureEvidenceSummary> FeatureEvidence { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CrossAnswerPattern> Patterns { get; set; } = new();
    public FinalScoreBreakdown FinalBreakdown { get; set; } = new();
}
public class JobRequirementProfile
{
    public Dictionary<string, double> FeaturePriorities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> CriticalFeatures { get; set; } = new();
    public double RequiredEvidenceCoverage { get; set; }
}
public class FeatureNode// Represents a feature node in the evidence graph.
{
    public string Name { get; set; } = "";
    public double PriorityWeight { get; set; }
    public bool IsCritical { get; set; }
}
public class QuestionEvidenceNode
{
    public Guid QuestionId { get; set; }
    public string QuestionText { get; set; } = "";
    public int OrderIndex { get; set; }
    public Dictionary<string, double> ExpectedFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double MatchToJobScore { get; set; }
    public double QuestionImportance { get; set; }
}
public class AnswerEvidenceNode
{
    public Guid AnswerId { get; set; }
    public Guid QuestionId { get; set; }
    public string AnswerText { get; set; } = "";
    public string RelevanceStatus { get; set; } = "";
    public double RelevanceConfidence { get; set; }
    public int AttemptNumber { get; set; }
    public bool WasCorrectedAfterGuidance { get; set; }
    public bool FinalFailedQuestion { get; set; }
    public string? FailureReason { get; set; }
    public Dictionary<string, double> DiagnosticScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public double ReliabilityScore { get; set; }
    public double LocalWeightedScore { get; set; }
}
public class EvidenceEdge
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string RelationType { get; set; } = "";
    public double Weight { get; set; }
}
public class FeatureEvidenceSummary
{
    public string FeatureName { get; set; } = "";
    public double PriorityWeight { get; set; }
    public double WeightedScore { get; set; }
    public double EvidenceCoverage { get; set; }
    public double Reliability { get; set; }
    public int SupportingAnswersCount { get; set; }
    public int FailedAnswersCount { get; set; }
    public List<Guid> SupportingQuestionIds { get; set; } = new();
    public List<Guid> WeakQuestionIds { get; set; } = new();
}
public class CrossAnswerPattern
{
    public string PatternType { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Description { get; set; } = "";
    public double ScoreImpact { get; set; }
    public Dictionary<string, double> AffectedFeatures { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Guid> RelatedQuestionIds { get; set; } = new();
    public List<Guid> RelatedAnswerIds { get; set; } = new();
}
public class FinalScoreBreakdown
{
    public double PriorityWeightedFeatureScore { get; set; }
    public double ReliabilityScore { get; set; }
    public double CoverageScore { get; set; }
    public double RelevanceBehaviorScore { get; set; }
    public double CrossAnswerConsistencyScore { get; set; }
    public double PatternAdjustment { get; set; }
    public double FinalScore { get; set; }
    public Dictionary<string, double> CategoryScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> CategoryWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}