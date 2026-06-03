using System.Text.Json;
using MindMatchAI.Data;
using MindMatchAI.Models;
using Newtonsoft.Json;
using MindMatchAI.Constants;
namespace MindMatchAI.Services.InterviewFlow;
//Receives a candidate answer, saves it, checks relevance, and if needed runs routing and diagnostics
public class InterviewFlowService
{
    private readonly InterviewDbContext _db;
    private readonly RelevanceService _relevanceService;
    private readonly DiagnosisService _diagnosisService;
    private readonly RoutingService _routingService;
    private readonly DiagnosticModelRunnerService _diagnosticModelRunnerService;
    private readonly DerivedCategoryService _derivedCategoryService;
    private readonly InterviewFlowSettings _settings;

    public InterviewFlowService(
        InterviewDbContext db,
        RelevanceService relevanceService,
        DiagnosisService diagnosisService,
        RoutingService routingService,
        DiagnosticModelRunnerService diagnosticModelRunnerService,
        DerivedCategoryService derivedCategoryService)
    {
        _db = db;
        _relevanceService = relevanceService;
        _diagnosisService = diagnosisService;
        _routingService = routingService;
        _diagnosticModelRunnerService = diagnosticModelRunnerService;
        _derivedCategoryService = derivedCategoryService;
        _settings = LoadSettings();
    }

    public InterviewAnswer ProcessAnswer(
        Guid interviewId,
        Guid questionId,
        string questionText,
        string answerText,
        int answerOrder,
        int attemptNumber,
        Guid? parentAnswerId,
        int maxAttemptsPerQuestion = 0)
    {
        if (maxAttemptsPerQuestion <= 0)
        {
            maxAttemptsPerQuestion = _settings.MaxAttemptsPerQuestion;
        }

        bool isFinalAttempt = attemptNumber >= maxAttemptsPerQuestion;

        InterviewQuestion? interviewQuestion = _db.InterviewQuestions
            .FirstOrDefault(q => q.Id == questionId);

        QuestionMetadata questionMetadata = BuildQuestionMetadata(interviewQuestion);

        string effectiveQuestionText = !string.IsNullOrWhiteSpace(interviewQuestion?.QuestionText)
            ? interviewQuestion.QuestionText
            : questionText;

        if (string.IsNullOrWhiteSpace(effectiveQuestionText))
        {
            throw new Exception("QuestionText is required. Could not find question text in request or InterviewQuestions table.");
        }

        Console.WriteLine("\n Loaded question metadata:");
        Console.WriteLine($"QuestionId: {questionId}");
        Console.WriteLine($"SourceQuestionBankVersion: {questionMetadata.SourceQuestionBankVersion}");
        Console.WriteLine($"SourceQuestionBankItemId: {questionMetadata.SourceQuestionBankItemId}");
        Console.WriteLine($"RecommendedScoringModelsJson: {questionMetadata.RecommendedScoringModelsJson}");
        Console.WriteLine($"ContentCoverageJson length: {questionMetadata.ContentCoverageJson.Length}");
        Console.WriteLine($"DiagnosticCoverageJson length: {questionMetadata.DiagnosticCoverageJson.Length}");
        Console.WriteLine($"AnswerSignalsJson length: {questionMetadata.AnswerSignalsJson.Length}");

        var answerRecord = new InterviewAnswer
        {
            InterviewId = interviewId,
            QuestionId = questionId,
            QuestionText = effectiveQuestionText,
            AnswerText = answerText,
            AnswerOrder = answerOrder,
            AttemptNumber = attemptNumber,
            ParentAnswerId = parentAnswerId,
            IsFinalAttemptForQuestion = isFinalAttempt,
            RelevanceStatus = InterviewStatuses.Pending
        };

        _db.InterviewAnswers.Add(answerRecord);
        _db.SaveChanges();

        
        Console.WriteLine($"AnswerId: {answerRecord.Id}");
        Console.WriteLine($"InterviewId: {answerRecord.InterviewId}");
        Console.WriteLine($"QuestionId: {answerRecord.QuestionId}");
        Console.WriteLine($"AttemptNumber: {answerRecord.AttemptNumber}");
        Console.WriteLine($"ParentAnswerId: {answerRecord.ParentAnswerId}");
        Console.WriteLine($"IsFinalAttemptForQuestion: {answerRecord.IsFinalAttemptForQuestion}");
       

        RelevanceResult relevanceResult;

        try
        {
            relevanceResult = _relevanceService.CheckRelevance(effectiveQuestionText, answerText);
        }
        catch
        {
            answerRecord.RelevanceStatus = InterviewStatuses.ModelError;
            _db.SaveChanges();
            throw;
        }

        string relevanceStatus = relevanceResult.IsRelevant
            ? InterviewStatuses.Relevant : InterviewStatuses.Irrelevant;

        answerRecord.RelevanceStatus = relevanceStatus;
        answerRecord.RelevanceConfidence = relevanceResult.Score;

        var relevanceCheck = new RelevanceCheck
        {
            InterviewAnswerId = answerRecord.Id,
            Status = relevanceStatus,
            Score = relevanceResult.Score,
            ModelName = _settings.RelevanceModelName,
            ModelVersion = _settings.RelevanceModelVersion
        };

        _db.RelevanceChecks.Add(relevanceCheck);
        _db.SaveChanges();

       
        if (relevanceResult.IsRelevant)
        {
            HandleRelevantAnswer(
                effectiveQuestionText,
                answerText,
                answerRecord,
                questionMetadata
            );

            return answerRecord;
        }

        if (!isFinalAttempt)
        {
            HandleIrrelevantAnswerWithRetry(effectiveQuestionText, answerText, answerRecord);
            return answerRecord;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.ResetColor();

        answerRecord.IsFinalAttemptForQuestion = true;
        _db.SaveChanges();

        return answerRecord;
    }

    private void HandleRelevantAnswer(
        string question,
        string answer,
        InterviewAnswer answerRecord,
        QuestionMetadata questionMetadata)
    {
       
        RoutingResult? routingResult = _routingService.RunRouting(
            question,
            answer,
            questionMetadata.RecommendedScoringModelsJson,
            questionMetadata.DiagnosticCoverageJson,
            questionMetadata.ContentCoverageJson,
            questionMetadata.AnswerSignalsJson
        );

        

        RoutingDecision routingDecision = _routingService.BuildDecision(answerRecord, routingResult);

        _db.RoutingDecisions.Add(routingDecision);
        _db.SaveChanges();

        _diagnosticModelRunnerService.RunDiagnosticsByRouting(answerRecord, routingDecision);

        _derivedCategoryService.CalculateAndSave(answerRecord.Id);

    }

    private void HandleIrrelevantAnswerWithRetry(
        string question,
        string answer,
        InterviewAnswer answerRecord)
    {

        DiagnosisResult? diagnosis = _diagnosisService.RunDiagnosis(question, answer);

        if (diagnosis == null)
        {

            answerRecord.DiagnosisJson = JsonConvert.SerializeObject(new
            {
                error = "Diagnosis returned null",
                createdAtUtc = DateTime.UtcNow
            });

            _db.SaveChanges();
            return;
        }

        DiagnosisFeedback diagnosisFeedback =
            _diagnosisService.SaveDiagnosis(answerRecord, diagnosis);

        _db.DiagnosisFeedbacks.Add(diagnosisFeedback);
        _db.SaveChanges();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.ResetColor();
    }

    private static QuestionMetadata BuildQuestionMetadata(InterviewQuestion? question)
    {
        if (question == null)
        {
            return new QuestionMetadata
            {
                SourceQuestionBankVersion = QuestionSources.Unknown,
                SourceQuestionBankItemId = null,
                ContentCoverageJson = JsonDefaults.EmptyObject,
                DiagnosticCoverageJson = JsonDefaults.EmptyObject,
                RecommendedScoringModelsJson = JsonDefaults.EmptyArray,
                AnswerSignalsJson = JsonDefaults.EmptyArray
            };
        }

        return new QuestionMetadata
        {
            SourceQuestionBankVersion = question.SourceQuestionBankVersion,
            SourceQuestionBankItemId = question.SourceQuestionBankItemId,
            ContentCoverageJson = string.IsNullOrWhiteSpace(question.ContentCoverageJson)
                ? "{}"
                : question.ContentCoverageJson,
            DiagnosticCoverageJson = string.IsNullOrWhiteSpace(question.DiagnosticCoverageJson)
                ? "{}"
                : question.DiagnosticCoverageJson,
            RecommendedScoringModelsJson = string.IsNullOrWhiteSpace(question.RecommendedScoringModelsJson)
                ? "[]"
                : question.RecommendedScoringModelsJson,
            AnswerSignalsJson = string.IsNullOrWhiteSpace(question.AnswerSignalsJson)
                ? "[]"
                : question.AnswerSignalsJson
        };
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= maxLength
            ? value
            : value.Substring(0, maxLength) + "...";
    }

    private static InterviewFlowSettings LoadSettings()
    {
        string configPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "interview-flow-settings.json"
        );

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Interview flow settings file was not found: {configPath}"
            );
        }

        string json = File.ReadAllText(configPath);

        var root = System.Text.Json.JsonSerializer.Deserialize<InterviewFlowSettingsRoot>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        );

        if (root?.InterviewFlow == null)
        {
            throw new InvalidOperationException(
                "interview-flow-settings.json is missing InterviewFlow section."
            );
        }

        return root.InterviewFlow;
    }

    private class QuestionMetadata
    {
        public string SourceQuestionBankVersion { get; set; } = "Unknown";
        public Guid? SourceQuestionBankItemId { get; set; }
        public string ContentCoverageJson { get; set; } = "{}";
        public string DiagnosticCoverageJson { get; set; } = "{}";
        public string RecommendedScoringModelsJson { get; set; } = "[]";
        public string AnswerSignalsJson { get; set; } = "[]";
    }

    private class InterviewFlowSettingsRoot
    {
        public InterviewFlowSettings? InterviewFlow { get; set; }
    }

    private class InterviewFlowSettings
    {
        public int MaxAttemptsPerQuestion { get; set; } = 3;
        public int MetadataLogMaxLength { get; set; } = 400;
        public string RelevanceModelName { get; set; } = "SBERT+RoBERTa";
        public string RelevanceModelVersion { get; set; } = "current";
        public string RoutingSuccessStatus { get; set; } = "success";
    }
}
