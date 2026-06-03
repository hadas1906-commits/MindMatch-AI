using System;
namespace MindMatchAI.Models;
public class RoutingDecision
{//Which diagnostic models to run
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InterviewAnswerId { get; set; }
    public InterviewAnswer InterviewAnswer { get; set; } = null!;
    public bool RunTraitsModel { get; set; }
    public bool RunExperienceModel { get; set; }
    public bool RunThinkingQualityModel { get; set; }
    public bool RunPracticalAbilitiesModel { get; set; }
    public double? Confidence { get; set; }
    public string? RawJson { get; set; }
    public string ModelName { get; set; } = "";
    public string? ModelVersion { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}