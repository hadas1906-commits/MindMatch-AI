namespace MindMatchAI.Models.Runtime;
public class RuntimeQuestionCandidate
{
    //Candidate question for selection from the question bank
    public Guid QuestionBankItemId { get; set; }
    public string QuestionCode { get; set; } = "";
    public string QuestionText { get; set; } = "";
    public string DiagnosticTarget { get; set; } = "";
    public double MatchScore { get; set; }
    public Dictionary<string, double> ContentCoverage { get; set; } = new();
    public HashSet<string> RecommendedScoringModels { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}