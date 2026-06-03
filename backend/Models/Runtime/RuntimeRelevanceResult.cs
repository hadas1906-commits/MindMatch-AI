using MindMatchAI.Constants;
namespace MindMatchAI.Models.Runtime;
public class RuntimeRelevanceResult
{
    //Relevance result at runtime
    public bool IsRelevant { get; set; }
    public double Score { get; set; }
    public string Status =>
        IsRelevant
            ? InterviewStatuses.Relevant
            : InterviewStatuses.Irrelevant;
    public string? Reason { get; set; }
}