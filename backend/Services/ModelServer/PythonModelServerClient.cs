using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace MindMatchAI.Services.ModelServer;

public sealed class PythonModelServerClient
{
    private readonly HttpClient _httpClient;
    private readonly ModelServerEndpointSettings _endpoints;

    public PythonModelServerClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _endpoints = LoadSettings(configuration).Endpoints;
    }

    public string CheckRelevance(string question, string answer)
    {
        return PostAndRead(_endpoints.Relevance, new { question, answer });
    }

    public string Diagnose(string question, string answer)
    {
        return PostAndRead(_endpoints.Diagnosis, new { question, answer });
    }

    public string Route(string question, string answer)
    {
        return PostAndRead(_endpoints.Routing, new { question, answer });
    }

    public string RunDiagnostic(string diagnosticType, string question, string answer)
    {
        return PostAndRead(_endpoints.Diagnostic, new
        {
            diagnosticType,
            question,
            answer
        });
    }

    public string RankQuestions(
        string jobTitle,
        string jobDescription,
        object questions,
        int topK)
    {
        return PostAndRead(_endpoints.JobQuestionMatch, new
        {
            jobTitle,
            jobDescription,
            questions,
            topK
        });
    }

    public string TranscribeFile(string audioPath)
    {
        return PostAndRead(_endpoints.TranscribeFile, new
        {
            audioPath
        });
    }

    public string WarmUp()
    {
        return PostAndRead(_endpoints.Warmup, new { });
    }

    public bool IsHealthy()
    {
        try
        {
            using var response = _httpClient
                .GetAsync(_endpoints.Health)
                .GetAwaiter()
                .GetResult();

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private string PostAndRead(string path, object payload)
    {
        using var response = _httpClient
            .PostAsJsonAsync(path, payload)
            .GetAwaiter()
            .GetResult();

        response.EnsureSuccessStatusCode();

        return response.Content
            .ReadAsStringAsync()
            .GetAwaiter()
            .GetResult();
    }

    private static ModelServerSettings LoadSettings(IConfiguration configuration)
    {
        return configuration
            .GetSection("ModelServer")
            .Get<ModelServerSettings>() ?? new ModelServerSettings();
    }

    private class ModelServerSettings
    {
        public ModelServerEndpointSettings Endpoints { get; set; } = new();
    }

    private class ModelServerEndpointSettings
    {
        public string Relevance { get; set; } = "";
        public string Diagnosis { get; set; } = "";
        public string Routing { get; set; } = "";
        public string Diagnostic { get; set; } = "";
        public string JobQuestionMatch { get; set; } = "";
        public string TranscribeFile { get; set; } = "";
        public string Warmup { get; set; } = "";
        public string Health { get; set; } = "";
    }
}
