using MindMatchAI.Constants;
namespace MindMatchAI.Models.Runtime;
public class RuntimeRoutingDecision
{
    //Represents a temporary routing decision at runtime
    public bool RunTraitsModel { get; set; }
    public bool RunThinkingQualityModel { get; set; }
    public bool RunPracticalAbilitiesModel { get; set; }
    public bool RunExperienceModel { get; set; }
    public Dictionary<string, double> LinearModelProbabilities { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ModelsRecommendedByMetadata { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> FinalModelsToRun { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string RoutingMode { get; set; } = RuntimeDefaults.DefaultRoutingMode;
}