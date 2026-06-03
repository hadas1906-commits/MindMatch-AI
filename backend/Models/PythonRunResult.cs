namespace MindMatchAI.Models;
public class PythonRunResult
{//Helper object
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public int ExitCode { get; set; }
    public bool Success { get; set; }
}