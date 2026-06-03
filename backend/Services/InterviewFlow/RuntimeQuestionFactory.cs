using MindMatchAI.Constants;
using MindMatchAI.Models;
using MindMatchAI.Models.Runtime;

namespace MindMatchAI.Services.InterviewFlow;
// Converts database questions into runtime question objects used during the interview.
public class RuntimeQuestionFactory
{
    public RuntimeQuestion FromQuestionBankV2(
        QuestionBankV2Item item,
        int orderIndex,
        double? matchScore = null,
        string? overrideQuestionText = null)
    {
        return new RuntimeQuestion
        {
            SourceQuestionBankItemId = item.Id,
            SourceQuestionBankVersion = QuestionSources.QuestionBankV2,
            QuestionCode = item.Code,
            QuestionText = string.IsNullOrWhiteSpace(overrideQuestionText)
                ? item.QuestionText
                : overrideQuestionText,
            OrderIndex = orderIndex,
            ContentCoverage = RuntimeJsonConverters.ParseWeightedTags(item.ContentCoverageJson),
            RecommendedScoringModels = RuntimeJsonConverters.ParseDiagnosticModelFlags(item.RecommendedScoringModelsJson),
            AnswerSignalIds = RuntimeJsonConverters.ParseAnswerSignalIds(item.AnswerSignalsJson),
            MatchScoreFromJobQuestionMatcher = matchScore
        };
    }

    public RuntimeQuestion FromManualQuestion(string questionText, int orderIndex)
    {
        return new RuntimeQuestion
        {
            SourceQuestionBankItemId = null,
            SourceQuestionBankVersion = QuestionSources.Manual,
            QuestionCode = QuestionSources.ManualCode,
            QuestionText = questionText,
            OrderIndex = orderIndex,
            ContentCoverage = new List<WeightedTag>(),
            RecommendedScoringModels = DiagnosticModelFlags.None,
            AnswerSignalIds = new HashSet<int>(),
            MatchScoreFromJobQuestionMatcher = null
        };
    }
}
