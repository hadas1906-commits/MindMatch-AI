using Newtonsoft.Json;
namespace MindMatchAI.Models;
public class DiagnosisResult
{//Temporary object for parsing the model response
    [JsonProperty("status")]
    public string Status { get; set; } = "";

    [JsonProperty("reason")]
    public string Reason { get; set; } = "";

    [JsonProperty("feedback")]
    public string Feedback { get; set; } = "";

    [JsonProperty("why")]
    public string Why { get; set; } = "";
}