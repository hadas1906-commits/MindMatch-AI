namespace MindMatchAI.Services.Python;

// Stores Python executable and script paths used by the local Python runner fallback.
public class PythonPaths
{
    public string PythonExe { get; set; } = "";
    public string RobertaScript { get; set; } = "";
    public string ReasonsScript { get; set; } = "";
    public string RoutingScript { get; set; } = "";
    public string BigFiveScript { get; set; } = "";
    public string ExperienceScript { get; set; } = "";
    public string PracticalAbilityScript { get; set; } = "";
    public string ThinkingQualityScript { get; set; } = "";
    public string JobProfileExtractorScript { get; set; } = "";
    public string JobQuestionMatcherScript { get; set; } = "";
    public string AudioTranscriptionScript { get; set; } = "";
}