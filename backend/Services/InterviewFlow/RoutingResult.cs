using Newtonsoft.Json;
namespace MindMatchAI.Services.InterviewFlow;
public class RoutingResult
{
// Represents the output returned from the routing model.
    [JsonProperty("status")]
    public string Status { get; set; } = "";
    [JsonProperty("threshold")]
    public double Threshold { get; set; }
    [JsonProperty("model_path")]
    public string? ModelPath { get; set; }
    [JsonProperty("question")]
    public string Question { get; set; } = "";
    [JsonProperty("answer")]
    public string Answer { get; set; } = "";
    [JsonProperty("probabilities")]
    public Dictionary<string, double> Probabilities { get; set; } = new();
    [JsonProperty("predicted")]
    public Dictionary<string, int> Predicted { get; set; } = new();
    public string? RawMetadataJson { get; set; }
}
