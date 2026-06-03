using System;
using System.Collections.Generic;
namespace MindMatchAI.Models;
public class InterviewAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterviewId { get; set; }
    public Interview Interview { get; set; }
    public Guid QuestionId { get; set; }
    public InterviewQuestion Question { get; set; }
    public string QuestionText { get; set; } = "";//The question text at the time the candidate answered
    public string AnswerText { get; set; } = "";
    public int AnswerOrder { get; set; }//The answer order in the interview
    public int AttemptNumber { get; set; } = 1;//Attempt number
    public Guid? ParentAnswerId { get; set; }//Link to the previous answer in case of a retry attempt
    public InterviewAnswer? ParentAnswer { get; set; }//The previous answer object
    public List<InterviewAnswer> RetryAnswers { get; set; } = new();//List of retry answers created from this answer
    public bool IsFinalAttemptForQuestion { get; set; }//Whether this is the final attempt
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? RelevanceStatus { get; set; }
    public double? RelevanceConfidence { get; set; }//Confidence score of the relevance model
    public string? DiagnosisJson { get; set; }//Full diagnosis output in case the answer is not relevant
    public string? GuidanceMessage { get; set; }//Retry feedback
    public string? PersonalityAnalysisJson { get; set; }//Personality analysis if available
    public List<RelevanceCheck> RelevanceChecks { get; set; } = new();//Relevance checks for the answer
    public List<DiagnosisFeedback> DiagnosisFeedbacks { get; set; } = new();//Diagnosis feedbacks
    public List<RoutingDecision> RoutingDecisions { get; set; } = new();//Routing decisions
    public List<DiagnosticResult> DiagnosticResults { get; set; } = new();//Diagnostic results
}