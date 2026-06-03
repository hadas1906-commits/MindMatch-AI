namespace MindMatchAI.Models.Runtime;
public class FinalInterviewSummary
{
    //Temporary interview summary at the end of the process
    public Guid InterviewId { get; set; }
    public Guid JobId { get; set; }
    public Guid CandidateId { get; set; }
    public string JobTitle { get; set; } = "";
    public int QuestionsAsked { get; set; }
    public int AnswersReceived { get; set; }
    public int RelevantAnswers { get; set; }
    public int IrrelevantAnswers { get; set; }
    public Dictionary<string, double> DiagnosticScores { get; set; } = new();
    public double FinalWeightedScore { get; set; }
    public List<string> Strengths { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public string Recommendation { get; set; } = "";
    public List<FinalQuestionSummary> QuestionSummaries { get; set; } = new();
}
public class FinalQuestionSummary
{
    //Summary of a single question
    public string QuestionText { get; set; } = "";
    public string AnswerSummary { get; set; } = "";
    public List<string> DiagnosticsUsed { get; set; } = new();
    public Dictionary<string, double> Scores { get; set; } = new();
}