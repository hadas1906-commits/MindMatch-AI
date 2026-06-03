using System.Text.Json;

namespace MindMatchAI.Models.Runtime;

public static class QuestionTagRegistry
{
    private static readonly Dictionary<string, int> NameToId = LoadTags();

    private static readonly Dictionary<int, string> IdToName =
        NameToId.ToDictionary(x => x.Value, x => x.Key);

    public static bool TryGetId(string name, out int id)
    {
        return NameToId.TryGetValue(name, out id);
    }

    public static string GetName(int id)
    {
        return IdToName.TryGetValue(id, out var name)
            ? name
            : $"unknown_{id}";
    }

    public static IReadOnlyDictionary<string, int> GetAll()
    {
        return NameToId;
    }

    private static Dictionary<string, int> LoadTags()
    {
        string configPath = Path.Combine(
            AppContext.BaseDirectory,
            "Config",
            "question-tags.json"
        );

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Question tags configuration file was not found: {configPath}"
            );
        }

        string json = File.ReadAllText(configPath);

        var tags = JsonSerializer.Deserialize<Dictionary<string, int>>(json);

        if (tags == null || tags.Count == 0)
        {
            throw new InvalidOperationException(
                "Question tags configuration file is empty or invalid."
            );
        }

        return new Dictionary<string, int>(
            tags,
            StringComparer.OrdinalIgnoreCase
        );
    }
}