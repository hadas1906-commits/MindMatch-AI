using System;
using System.Collections.Generic;
using MindMatchAI.Constants;
namespace MindMatchAI.Models;
public class Interview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CandidateId { get; set; }
    public Candidate Candidate { get; set; } = null!;
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public string Status { get; set; } = InterviewStatuses.Started;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; }
    public List<InterviewAnswer> Answers { get; set; } = new();//List of answers from the interview
    public List<FinalCandidateScore> FinalScores { get; set; } = new();//List of final scores calculated for the interview
}