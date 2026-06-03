using MindMatchAI.Constants;
namespace MindMatchAI.Models.Runtime;
public class RuntimeDiagnosticResult
{
    //Temporary diagnostic result at runtime
    public string DiagnosticType { get; set; } = "";
    public double Score { get; set; }
    public Dictionary<string, double> SubScores { get; set; } = new();
    public string Summary { get; set; } = "";
    public double CoverageWeight { get; set; } = RuntimeDefaults.DefaultCoverageWeight;
    public string ModelName { get; set; } = "";
    public string ModelVersion { get; set; } = "";
}