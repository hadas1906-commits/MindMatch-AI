namespace MindMatchAI.Models;
public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();//Automatically creates a new identifier
    public string FullName { get; set; } = "";
    public string Role { get; set; } = "";
    public string? EmailEncrypted { get; set; }
    public string? EmailHash { get; set; }
    public string PasswordHash { get; set; } = "";
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid? CandidateId { get; set; }
    public Candidate? Candidate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
}