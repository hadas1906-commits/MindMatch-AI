using Newtonsoft.Json.Linq;
namespace MindMatchAI.Models.Runtime;
public static class RuntimeJsonConverters
{
    //Helper file that converts JSON into runtime structures
    public static List<WeightedTag> ParseWeightedTags(string? json)
    {
        //Receives JSON and returns a list of weighted tags
        var result = new List<WeightedTag>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }
        try
        {
            var obj = JObject.Parse(json);
            foreach (var prop in obj.Properties())
            {
                if (!QuestionTagRegistry.TryGetId(prop.Name, out int tagId))
                {
                    continue;
                }
                if (double.TryParse(
                        prop.Value?.ToString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double weight))
                {
                    result.Add(new WeightedTag(tagId, weight));
                }
            }
        }
        catch
        {
            return result;
        }
        return result;
    }
    public static DiagnosticModelFlags ParseDiagnosticModelFlags(string? json)
    {
        //Receives JSON of model names and returns flags.
        if (string.IsNullOrWhiteSpace(json))
        {
            return DiagnosticModelFlags.None;
        }
        try
        {
            var arr = JArray.Parse(json);
            var flags = DiagnosticModelFlags.None;
            foreach (var item in arr)
            {
                string name = item?.ToString() ?? "";
                if (DiagnosticConstants.TryParseName(name, out var type))
                {
                    flags |= DiagnosticConstants.ToFlag(type);
                }
            }
            return flags;
        }
        catch
        {
            return DiagnosticModelFlags.None;
        }
    }
    public static HashSet<int> ParseAnswerSignalIds(string? json)
    {
        //Receives JSON of answer signals and returns numeric tag IDs.
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }
        try
        {
            var obj = JToken.Parse(json);
            if (obj is JArray arr)
            {
                foreach (var item in arr)
                {
                    string name = item?.ToString() ?? "";
                    if (QuestionTagRegistry.TryGetId(name, out int tagId))
                    {
                        result.Add(tagId);
                    }
                }
            }
            if (obj is JObject objectValue)
            {
                foreach (var prop in objectValue.Properties())
                {
                    if (QuestionTagRegistry.TryGetId(prop.Name, out int tagId))
                    {
                        result.Add(tagId);
                    }
                }
            }
        }
        catch
        {
            return result;
        }
        return result;
    }
}