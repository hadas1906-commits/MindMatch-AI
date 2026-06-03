namespace MindMatchAI.Models.Runtime;
public class InterviewRuntimeState
{
    //Full temporary interview state at runtime
    public Guid InterviewId { get; set; }
    public Guid JobId { get; set; }
    public Guid CandidateId { get; set; }
    public string JobTitle { get; set; } = "";
    public string JobDescription { get; set; } = "";
    public InterviewRuntimeStatus Status { get; set; } = InterviewRuntimeStatus.Created;
    public int CurrentQuestionIndex { get; set; } = 0;
    public List<RuntimeQuestion> Questions { get; set; } = new();
    public Dictionary<Guid, RuntimeQuestion> QuestionsById { get; set; } = new();//Dictionary for quick lookup of a question by ID
    public List<RuntimeAnswer> Answers { get; set; } = new();
    public Dictionary<Guid, RuntimeAnswer> AnswersById { get; set; } = new();
    public Dictionary<Guid, List<RuntimeAnswer>> AnswersByQuestionId { get; set; } = new();//Dictionary that groups answers by question
    public RuntimeScoreAccumulator ScoreAccumulator { get; set; } = new();
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public void AddQuestion(RuntimeQuestion question)
    {
        //Adds a question to the runtime state
        Questions.Add(question);
        QuestionsById[question.RuntimeQuestionId] = question;
        LastUpdatedAtUtc = DateTime.UtcNow;
    }
    public void AddAnswer(RuntimeAnswer answer)
    {
        //Adds an answer to the runtime state
        Answers.Add(answer);
        AnswersById[answer.RuntimeAnswerId] = answer;
        if (!AnswersByQuestionId.ContainsKey(answer.RuntimeQuestionId))
        {
            AnswersByQuestionId[answer.RuntimeQuestionId] = new List<RuntimeAnswer>();
        }
        AnswersByQuestionId[answer.RuntimeQuestionId].Add(answer);
        LastUpdatedAtUtc = DateTime.UtcNow;
    }
    public RuntimeQuestion? GetQuestion(Guid questionId)
    {
        //Returns a question by ID
        return QuestionsById.TryGetValue(questionId, out var question)
            ? question
            : null;
    }
    public RuntimeAnswer? GetAnswer(Guid answerId)
    {
        //Returns an answer by ID
        return AnswersById.TryGetValue(answerId, out var answer)
            ? answer
            : null;
    }
}