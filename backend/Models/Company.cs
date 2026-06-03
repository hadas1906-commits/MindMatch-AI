using System;
using System.Collections.Generic;
namespace MindMatchAI.Models;
public class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string ContactEmailEncrypted { get; set; } = "";
    public string ContactEmailHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<Job> Jobs { get; set; } = new();//List of the company jobs
}