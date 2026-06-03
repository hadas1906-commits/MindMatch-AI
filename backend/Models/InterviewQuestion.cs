using System;
using MindMatchAI.Constants;
namespace MindMatchAI.Models;
public class InterviewQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public string QuestionText { get; set; } = "";
    public int OrderIndex { get; set; }//Order of the questions in the interview
    public bool IsCompanyCustomQuestion { get; set; }//Question from the bank or from the company
    public string SourceQuestionBankVersion { get; set; } = QuestionSources.Custom;
    public Guid? SourceQuestionBankItemId { get; set; }//Question identifier from the bank
    public string ContentCoverageJson { get; set; } = JsonDefaults.EmptyObject;//Coverage topics
    public string DiagnosticCoverageJson { get; set; } = JsonDefaults.EmptyObject;//Diagnostic characteristics
    public string RecommendedScoringModelsJson { get; set; } = JsonDefaults.EmptyArray;//List of recommended models to run
    public string AnswerSignalsJson { get; set; } = JsonDefaults.EmptyArray;//Signals expected to be found in a good answer
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}