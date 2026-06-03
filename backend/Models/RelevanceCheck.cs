using System;
namespace MindMatchAI.Models;
public class RelevanceCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterviewAnswerId { get; set; }
    public InterviewAnswer InterviewAnswer { get; set; } = null!;
    public string Status { get; set; } = "";
    public double? Score { get; set; }
    public string ModelName { get; set; } = "";
    public string? ModelVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}