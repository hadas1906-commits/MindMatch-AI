namespace MindMatchAI.Models.Runtime;
public enum InterviewRuntimeStatus
{
    //List of possible interview states
    Created,
    QuestionsSelected,
    InProgress,
    WaitingForRetryAnswer,
    Completed,
    Failed
}