using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MindMatchAI.Constants;
namespace MindMatchAI.Models;
[Table("QuestionBankV2")]//Question from the question bank
public class QuestionBankV2Item
{
    [Key]
    public Guid Id { get; set; }//Primary key
    public string Code { get; set; } = "";
    public string QuestionText { get; set; } = "";
    public string DiagnosticTarget { get; set; } = "";//Diagnostic target
    public string Difficulty { get; set; } = QuestionDifficulties.Medium;//Difficulty level
    public string ContentCoverageJson { get; set; } = JsonDefaults.EmptyObject;//Question coverage topics
    public string DiagnosticCoverageJson { get; set; } = JsonDefaults.EmptyObject;//Question diagnostic characteristics
    public string RecommendedScoringModelsJson { get; set; } = JsonDefaults.EmptyArray;//Recommended diagnostic models to run
    public string AnswerSignalsJson { get; set; } = JsonDefaults.EmptyArray;//Expected signals
    public bool IsActive { get; set; } = true;//The question is active
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}