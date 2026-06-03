using MindMatchAI.Constants;

namespace MindMatchAI.Models.Runtime;

public class RuntimeScoreAccumulator
{
    public Dictionary<string, ScoreAccumulatorBucket> Buckets { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void AddDiagnostic(string diagnosticType, double score, double weight)
    {
        if (!Buckets.ContainsKey(diagnosticType))
        {
            Buckets[diagnosticType] = new ScoreAccumulatorBucket();
        }

        Buckets[diagnosticType].Add(score, weight);
    }

    public double? GetAverage(string diagnosticType)
    {
        if (!Buckets.TryGetValue(diagnosticType, out var bucket))
        {
            return null;
        }

        return bucket.GetAverage();
    }

    public Dictionary<string, double> GetAllAverages()
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in Buckets)
        {
            var average = pair.Value.GetAverage();

            if (average.HasValue)
            {
                result[pair.Key] = Math.Round(
                    average.Value,
                    RuntimeDefaults.ScorePrecision);
            }
        }

        return result;
    }
}

public class ScoreAccumulatorBucket
{
    public double WeightedSum { get; set; }

    public double WeightSum { get; set; }

    public int Count { get; set; }

    public void Add(double score, double weight)
    {
        WeightedSum += score * weight;
        WeightSum += weight;
        Count++;
    }

    public double? GetAverage()
    {
        if (WeightSum == 0)
        {
            return null;
        }

        return WeightedSum / WeightSum;
    }
}