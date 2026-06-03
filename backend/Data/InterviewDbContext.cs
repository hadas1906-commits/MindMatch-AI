using Microsoft.EntityFrameworkCore;
using MindMatchAI.Models;
namespace MindMatchAI.Data;
public class InterviewDbContext : DbContext
{
    //Class constructor
    public InterviewDbContext(DbContextOptions<InterviewDbContext> options)
        : base(options)
    {
    }
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<InterviewQuestion> InterviewQuestions => Set<InterviewQuestion>();
    public DbSet<InterviewAnswer> InterviewAnswers => Set<InterviewAnswer>();
    public DbSet<RelevanceCheck> RelevanceChecks => Set<RelevanceCheck>();
    public DbSet<DiagnosisFeedback> DiagnosisFeedbacks => Set<DiagnosisFeedback>();
    public DbSet<RoutingDecision> RoutingDecisions => Set<RoutingDecision>();
    public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
    public DbSet<FinalCandidateScore> FinalCandidateScores => Set<FinalCandidateScore>();
    public DbSet<QuestionBankV2Item> QuestionBankV2 { get; set; }
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    //Structure of the tables and the relationships between them
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>()
            .HasKey(c => c.Id);//Primary key
        modelBuilder.Entity<Company>()
            .Property(c => c.Name)//Required field
            .IsRequired();
        modelBuilder.Entity<Company>()
            .HasIndex(c => c.Name);//Creates an index on the company name to search companies faster
        modelBuilder.Entity<Company>()
            .Property(c => c.ContactEmail)
            .IsRequired();

        modelBuilder.Entity<Company>()
            .Property(c => c.ContactEmailEncrypted)
            .IsRequired();

modelBuilder.Entity<Company>()
    .Property(c => c.ContactEmailHash)
    .IsRequired();

        modelBuilder.Entity<Job>()
            .HasKey(j => j.Id);
        modelBuilder.Entity<Job>()
            .Property(j => j.Title)
            .IsRequired();
        modelBuilder.Entity<Job>()
            .HasOne(j => j.Company)
            .WithMany(c => c.Jobs)
            .HasForeignKey(j => j.CompanyId);//Defines a relationship between a job and a company
        modelBuilder.Entity<Job>()
            .HasIndex(j => j.CompanyId);
            
        modelBuilder.Entity<Candidate>()
            .HasKey(c => c.Id);
        modelBuilder.Entity<Candidate>()
            .Property(c => c.FullName)
            .IsRequired();
        modelBuilder.Entity<Candidate>()
            .HasIndex(c => c.Email);
modelBuilder.Entity<Candidate>()
    .Property(c => c.Email)
    .IsRequired();

modelBuilder.Entity<Candidate>()
    .Property(c => c.EmailEncrypted)
    .IsRequired();

modelBuilder.Entity<Candidate>()
    .Property(c => c.EmailHash)
    .IsRequired();
    
        modelBuilder.Entity<Interview>()
            .HasKey(i => i.Id);
        modelBuilder.Entity<Interview>()
            .HasOne(i => i.Candidate)
            .WithMany(c => c.Interviews)
            .HasForeignKey(i => i.CandidateId);
        modelBuilder.Entity<Interview>()
            .HasOne(i => i.Job)
            .WithMany(j => j.Interviews)
            .HasForeignKey(i => i.JobId);
        modelBuilder.Entity<Interview>()
            .HasIndex(i => i.CandidateId);
        modelBuilder.Entity<Interview>()
            .HasIndex(i => i.JobId);
        modelBuilder.Entity<Interview>()
            .HasIndex(i => i.Status);

        modelBuilder.Entity<InterviewQuestion>()
            .HasKey(q => q.Id);
        modelBuilder.Entity<InterviewQuestion>()
            .Property(q => q.QuestionText)
            .IsRequired();
        modelBuilder.Entity<InterviewQuestion>()
            .HasOne(q => q.Job)
            .WithMany(j => j.Questions)
            .HasForeignKey(q => q.JobId);
        modelBuilder.Entity<InterviewQuestion>()
            .HasIndex(q => q.JobId);

        modelBuilder.Entity<InterviewAnswer>()
            .HasKey(a => a.Id);
        modelBuilder.Entity<InterviewAnswer>()
            .Property(a => a.QuestionText)//The question is saved with the answer so that even if the question changes...
            .IsRequired();
        modelBuilder.Entity<InterviewAnswer>()
            .Property(a => a.AnswerText)
            .IsRequired();
        modelBuilder.Entity<InterviewAnswer>()
            .HasOne(a => a.Interview)
            .WithMany(i => i.Answers)
            .HasForeignKey(a => a.InterviewId)
            .IsRequired();
        modelBuilder.Entity<InterviewAnswer>()
            .HasOne(a => a.Question)
            .WithMany()
            .HasForeignKey(a => a.QuestionId)
            .IsRequired();
        modelBuilder.Entity<InterviewAnswer>()
            .HasOne(a => a.ParentAnswer)//Special relationship for retry answers
            .WithMany(a => a.RetryAnswers)//The new answer points to the previous answer
            .HasForeignKey(a => a.ParentAnswerId)
            .IsRequired(false);
        modelBuilder.Entity<InterviewAnswer>()
            .HasIndex(a => a.InterviewId);
        modelBuilder.Entity<InterviewAnswer>()
            .HasIndex(a => a.QuestionId);
        modelBuilder.Entity<InterviewAnswer>()
            .HasIndex(a => a.ParentAnswerId);
        modelBuilder.Entity<InterviewAnswer>()
            .HasIndex(a => a.CreatedAtUtc);
        modelBuilder.Entity<InterviewAnswer>()
            .HasIndex(a => a.RelevanceStatus);
        modelBuilder.Entity<InterviewAnswer>()//To quickly find a specific attempt of a candidate for a specific question in a specific interview in case of irrelevance
            .HasIndex(a => new { a.InterviewId, a.QuestionId, a.AttemptNumber });

        modelBuilder.Entity<RelevanceCheck>()
            .HasKey(r => r.Id);
        modelBuilder.Entity<RelevanceCheck>()
            .HasOne(r => r.InterviewAnswer)
            .WithMany(a => a.RelevanceChecks)
            .HasForeignKey(r => r.InterviewAnswerId);
        modelBuilder.Entity<RelevanceCheck>()
            .HasIndex(r => r.InterviewAnswerId);
        modelBuilder.Entity<RelevanceCheck>()
            .HasIndex(r => r.Status);
        modelBuilder.Entity<RelevanceCheck>()
            .HasIndex(r => r.CreatedAtUtc);

        modelBuilder.Entity<DiagnosisFeedback>()
            .HasKey(d => d.Id);
        modelBuilder.Entity<DiagnosisFeedback>()
            .HasOne(d => d.InterviewAnswer)
            .WithMany(a => a.DiagnosisFeedbacks)
            .HasForeignKey(d => d.InterviewAnswerId);
        modelBuilder.Entity<DiagnosisFeedback>()
            .HasIndex(d => d.InterviewAnswerId);
        modelBuilder.Entity<DiagnosisFeedback>()
            .HasIndex(d => d.Reason);
        modelBuilder.Entity<DiagnosisFeedback>()
            .HasIndex(d => d.CreatedAtUtc);

        modelBuilder.Entity<RoutingDecision>()
            .HasKey(r => r.Id);
        modelBuilder.Entity<RoutingDecision>()
            .HasOne(r => r.InterviewAnswer)
            .WithMany(a => a.RoutingDecisions)
            .HasForeignKey(r => r.InterviewAnswerId);
        modelBuilder.Entity<RoutingDecision>()
            .HasIndex(r => r.InterviewAnswerId);
        modelBuilder.Entity<RoutingDecision>()
            .HasIndex(r => r.CreatedAtUtc);

        modelBuilder.Entity<DiagnosticResult>()
            .HasKey(d => d.Id);
        modelBuilder.Entity<DiagnosticResult>()
            .HasOne(d => d.InterviewAnswer)
            .WithMany(a => a.DiagnosticResults)
            .HasForeignKey(d => d.InterviewAnswerId);
        modelBuilder.Entity<DiagnosticResult>()
            .HasIndex(d => d.InterviewAnswerId);
        modelBuilder.Entity<DiagnosticResult>()
            .HasIndex(d => d.DiagnosticType);
        modelBuilder.Entity<DiagnosticResult>()
            .HasIndex(d => d.CreatedAtUtc);

        modelBuilder.Entity<FinalCandidateScore>()
            .HasKey(f => f.Id);
        modelBuilder.Entity<FinalCandidateScore>()
            .HasOne(f => f.Interview)
            .WithMany(i => i.FinalScores)
            .HasForeignKey(f => f.InterviewId);
        modelBuilder.Entity<FinalCandidateScore>()
            .HasIndex(f => f.InterviewId);
        modelBuilder.Entity<FinalCandidateScore>()
            .HasIndex(f => f.FinalScore);
        modelBuilder.Entity<FinalCandidateScore>()
            .HasIndex(f => f.CreatedAtUtc);

        modelBuilder.Entity<AppUser>()
            .HasKey(u => u.Id);
        modelBuilder.Entity<AppUser>()
            .Property(u => u.FullName)
            .IsRequired();
        modelBuilder.Entity<AppUser>()
            .Property(u => u.Role)
            .IsRequired();
        modelBuilder.Entity<AppUser>()
           .Property(u => u.PasswordHash)
           .IsRequired();
        modelBuilder.Entity<AppUser>()
           .HasIndex(u => u.EmailHash)
           .IsUnique();
        modelBuilder.Entity<AppUser>()
           .HasIndex(u => u.Role);
        modelBuilder.Entity<AppUser>()
          .HasOne(u => u.Company)
          .WithMany()
          .HasForeignKey(u => u.CompanyId)
          .IsRequired(false);
        modelBuilder.Entity<AppUser>()
          .HasOne(u => u.Candidate)
          .WithMany()
          .HasForeignKey(u => u.CandidateId)
          .IsRequired(false);
    }
}