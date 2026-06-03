using System;
namespace MindMatchAI.Models;
public class DiagnosticResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterviewAnswerId { get; set; }
    public InterviewAnswer InterviewAnswer { get; set; } = null!;
    public string DiagnosticType { get; set; } = "";//Diagnostic type
    public double? Score { get; set; }
    public string? ResultLabel { get; set; }
    public string? Summary { get; set; }//Summary
    public string? RawJson { get; set; }//Full output
    public string ModelName { get; set; } = "";
    public string? ModelVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}