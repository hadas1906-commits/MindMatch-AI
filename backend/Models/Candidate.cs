using System;
using System.Collections.Generic;
namespace MindMatchAI.Models;
public class Candidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string EmailEncrypted { get; set; } = "";
    public string EmailHash { get; set; } = "";
    public string? PhoneEncrypted { get; set; }
    public string? PhoneHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<Interview> Interviews { get; set; } = new();//List of the candidate interviews
}