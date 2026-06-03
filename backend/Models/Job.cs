using System;
using System.Collections.Generic;
namespace MindMatchAI.Models;
public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? RequiredTraitsJson { get; set; }//Traits required for the job
    public string? RequiredSkillsJson { get; set; }//Skills required for the job
    public string? ScoringWeightsJson { get; set; }//Scoring weights
    public string? InterviewPlanJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<Interview> Interviews { get; set; } = new();//List of interviews that were opened
    public List<InterviewQuestion> Questions { get; set; } = new();
    public DateTime? InterviewDeadlineUtc { get; set; }
    public bool IsClosed { get; set; } = false;
}