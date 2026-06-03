using MindMatchAI.Constants;
namespace MindMatchAI.Models.Runtime;
public class RuntimeQuestion
{
    //Represents a question at runtime
    public Guid RuntimeQuestionId { get; set; } = Guid.NewGuid();
    public Guid? SourceQuestionBankItemId { get; set; }
    public string SourceQuestionBankVersion { get; set; } = QuestionSources.QuestionBankV2;
    public string QuestionCode { get; set; } = "";
    public string QuestionText { get; set; } = "";
    public int OrderIndex { get; set; }
    public List<WeightedTag> ContentCoverage { get; set; } = new();
    public DiagnosticModelFlags RecommendedScoringModels { get; set; } =
        DiagnosticModelFlags.None;
    public HashSet<int> AnswerSignalIds { get; set; } = new();
    public double? MatchScoreFromJobQuestionMatcher { get; set; }
    public bool HasRecommendedModel(DiagnosticModelFlags model)
    {
        return (RecommendedScoringModels & model) != 0;
    }
}