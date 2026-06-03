using System;
namespace MindMatchAI.Models;
public class DiagnosisFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterviewAnswerId { get; set; }
    public InterviewAnswer InterviewAnswer { get; set; } = null!;
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public string FeedbackText { get; set; } = "";
    public string? Why { get; set; }//Internal explanation of why the model determined the reason
    public string? RawJson { get; set; }
    public string ModelName { get; set; } = "";
    public string? ModelVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}