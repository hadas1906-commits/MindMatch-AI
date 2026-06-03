using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using MindMatchAI.Models;

namespace MindMatchAI.Services.Python;

public class PythonRunner
{
    private readonly PythonPaths _paths;
    private readonly PythonRunnerSettings _settings;

    public PythonRunner(PythonPaths paths, IConfiguration configuration)
    {
        _paths = paths;
        _settings = LoadSettings(configuration);
    }

    public PythonRunResult Run(string scriptPath, params string[] args)
    {
        if (string.IsNullOrWhiteSpace(_paths.PythonExe))
        {
            return new PythonRunResult
            {
                Output = "",
                Error = "Python executable path is missing.",
                ExitCode = _settings.ErrorExitCode,
                Success = false
            };
        }

        bool pythonExeLooksLikePath =
           _paths.PythonExe.Contains(Path.DirectorySeparatorChar) ||
           _paths.PythonExe.Contains(Path.AltDirectorySeparatorChar);

        if (pythonExeLooksLikePath && !File.Exists(_paths.PythonExe))
        {
           return new PythonRunResult
           {
              Output = "",
              Error = "Python executable was not found.",
              ExitCode = _settings.ErrorExitCode,
              Success = false
            };
        }

        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return new PythonRunResult
            {
                Output = "",
                Error = "Python script path is missing.",
                ExitCode = _settings.ErrorExitCode,
                Success = false
            };
        }

        if (!File.Exists(scriptPath))
        {
            return new PythonRunResult
            {
                Output = "",
                Error = "Python script was not found.",
                ExitCode = _settings.ErrorExitCode,
                Success = false
            };
        }

        ProcessStartInfo start = new ProcessStartInfo
        {
            FileName = _paths.PythonExe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        start.EnvironmentVariables[_settings.PythonIoEncodingVariable] =
            _settings.PythonIoEncodingValue;

        start.EnvironmentVariables[_settings.PythonUtf8Variable] =
            _settings.PythonUtf8Value;

        start.ArgumentList.Add(scriptPath);

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg ?? "");
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start Python process.");

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        bool finished = process.WaitForExit(_settings.DefaultTimeoutMilliseconds);

        if (!finished)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            string partialOutput = outputTask.IsCompletedSuccessfully ? outputTask.Result : "";
            string partialError = errorTask.IsCompletedSuccessfully ? errorTask.Result : "";

            return new PythonRunResult
            {
                Output = partialOutput,
                Error = string.IsNullOrWhiteSpace(partialError)
                    ? "Python script timed out."
                    : $"Python script timed out. Partial error: {partialError}",
                ExitCode = _settings.ErrorExitCode,
                Success = false
            };
        }

        string output = outputTask.Result;
        string error = errorTask.Result;

        return new PythonRunResult
        {
            Output = output,
            Error = error,
            ExitCode = process.ExitCode,
            Success = process.ExitCode == _settings.SuccessExitCode
        };
    }

    private static PythonRunnerSettings LoadSettings(IConfiguration configuration)
    {
        var settings = configuration
            .GetSection("PythonRunner")
            .Get<PythonRunnerSettings>() ?? new PythonRunnerSettings();

        return settings;
    }

    private class PythonRunnerSettings
    {
        public int DefaultTimeoutMilliseconds { get; set; }

        public string PythonIoEncodingVariable { get; set; } = "";
        public string PythonIoEncodingValue { get; set; } = "";

        public string PythonUtf8Variable { get; set; } = "";
        public string PythonUtf8Value { get; set; } = "";

        public int SuccessExitCode { get; set; }
        public int ErrorExitCode { get; set; }
    }
}
