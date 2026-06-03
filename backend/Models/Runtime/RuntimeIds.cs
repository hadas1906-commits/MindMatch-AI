using MindMatchAI.Constants;

namespace MindMatchAI.Models.Runtime;

[Flags]
public enum DiagnosticModelFlags
{
    None = 0,

    Traits = 1 << 0,
    ThinkingQuality = 1 << 1,
    PracticalAbilities = 1 << 2,
    Experience = 1 << 3,
    Motivation = 1 << 4,
    CultureFit = 1 << 5
}

public enum DiagnosticTypeId
{
    Traits = 0,
    ThinkingQuality = 1,
    PracticalAbilities = 2,
    Experience = 3,
    Motivation = 4,
    CultureFit = 5
}

public static class DiagnosticConstants
{
    public const int DiagnosticCount = 6;

    public static DiagnosticModelFlags ToFlag(DiagnosticTypeId type)
    {
        return type switch
        {
            DiagnosticTypeId.Traits => DiagnosticModelFlags.Traits,
            DiagnosticTypeId.ThinkingQuality => DiagnosticModelFlags.ThinkingQuality,
            DiagnosticTypeId.PracticalAbilities => DiagnosticModelFlags.PracticalAbilities,
            DiagnosticTypeId.Experience => DiagnosticModelFlags.Experience,
            DiagnosticTypeId.Motivation => DiagnosticModelFlags.Motivation,
            DiagnosticTypeId.CultureFit => DiagnosticModelFlags.CultureFit,
            _ => DiagnosticModelFlags.None
        };
    }

    public static string ToName(DiagnosticTypeId type)
    {
        return type switch
        {
            DiagnosticTypeId.Traits => DiagnosticTypes.Traits,
            DiagnosticTypeId.ThinkingQuality => DiagnosticTypes.ThinkingQuality,
            DiagnosticTypeId.PracticalAbilities => DiagnosticTypes.PracticalAbilities,
            DiagnosticTypeId.Experience => DiagnosticTypes.Experience,
            DiagnosticTypeId.Motivation => DiagnosticTypes.Motivation,
            DiagnosticTypeId.CultureFit => DiagnosticTypes.CultureFit,
            _ => DiagnosticTypes.Unknown
        };
    }

    public static bool TryParseName(string? name, out DiagnosticTypeId type)
    {
        type = DiagnosticTypeId.Traits;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Equals(DiagnosticTypes.Traits, StringComparison.OrdinalIgnoreCase) ||
            name.Equals("BIG_FIVE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("personality", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagnosticTypeId.Traits;
            return true;
        }

        if (name.Equals(DiagnosticTypes.ThinkingQuality, StringComparison.OrdinalIgnoreCase) ||
            name.Equals("thinking", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagnosticTypeId.ThinkingQuality;
            return true;
        }

        if (name.Equals(DiagnosticTypes.PracticalAbilities, StringComparison.OrdinalIgnoreCase) ||
            name.Equals("abilities", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagnosticTypeId.PracticalAbilities;
            return true;
        }

        if (name.Equals(DiagnosticTypes.Experience, StringComparison.OrdinalIgnoreCase) ||
            name.Equals("experience", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagnosticTypeId.Experience;
            return true;
        }

        if (name.Equals(DiagnosticTypes.Motivation, StringComparison.OrdinalIgnoreCase))
        {
            type = DiagnosticTypeId.Motivation;
            return true;
        }

        if (name.Equals(DiagnosticTypes.CultureFit, StringComparison.OrdinalIgnoreCase))
        {
            type = DiagnosticTypeId.CultureFit;
            return true;
        }

        return false;
    }
}

public readonly struct WeightedTag
{
    public WeightedTag(int tagId, double weight)
    {
        TagId = tagId;
        Weight = weight;
    }

    public int TagId { get; }

    public double Weight { get; }
}