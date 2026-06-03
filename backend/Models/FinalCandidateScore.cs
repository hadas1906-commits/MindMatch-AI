using System;
namespace MindMatchAI.Models;
public class FinalCandidateScore
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterviewId { get; set; }
    public Interview Interview { get; set; } = null!;
    public double FinalScore { get; set; }
    public string? Summary { get; set; }
    public string? RawJson { get; set; }
    public string? ModelVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}