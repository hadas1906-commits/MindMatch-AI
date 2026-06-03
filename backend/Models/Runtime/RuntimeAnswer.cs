namespace MindMatchAI.Models.Runtime;
public class RuntimeAnswer
{
    //Represents an answer at runtime
    public Guid RuntimeAnswerId { get; set; } = Guid.NewGuid();
    public Guid RuntimeQuestionId { get; set; }
    public string AnswerText { get; set; } = "";
    public int AttemptNumber { get; set; }
    public bool IsFinalAttempt { get; set; }
    public RuntimeRelevanceResult? Relevance { get; set; }
    public RuntimeRoutingDecision? RoutingDecision { get; set; }
    public Dictionary<string, RuntimeDiagnosticResult> Diagnostics { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public DateTime AnsweredAtUtc { get; set; } = DateTime.UtcNow;
    public void AddDiagnostic(RuntimeDiagnosticResult diagnostic)
    {
        Diagnostics[diagnostic.DiagnosticType] = diagnostic;
    }
}