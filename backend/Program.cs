using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using MindMatchAI.Data;
using MindMatchAI.Models;
using MindMatchAI.Models.Runtime;
using MindMatchAI.Services.InterviewFlow;
using MindMatchAI.Services.Python;
using MindMatchAI.Services.Security;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.Text.Json;
using MindMatchAI.Services.Scoring;
using MindMatchAI.Services.ModelServer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("AdminApiKey", new OpenApiSecurityScheme
    {
        Description = "Enter the admin API key. Header name: X-Admin-Key",
        Name = "X-Admin-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "AdminApiKey"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "AdminApiKey"
                },
                In = ParameterLocation.Header,
                Name = "X-Admin-Key"
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddDataProtection()
    .SetApplicationName("MindMatchAI");

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("HeavyEndpoints", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

string connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Missing connection string.");

string jwtSecret =
    builder.Configuration["Security:JwtSecret"]
    ?? throw new InvalidOperationException("Missing Security:JwtSecret");

string jwtIssuer =
    builder.Configuration["Security:JwtIssuer"]
    ?? "MindMatchAI";

string jwtAudience =
    builder.Configuration["Security:JwtAudience"]
    ?? "MindMatchAIUsers";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSecret)
            ),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactClient", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddDbContext<InterviewDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.MigrationsAssembly("MindMatchAI.Api")
    ));

string modelServerBaseUrl =
    builder.Configuration["ModelServer:BaseUrl"]
    ?? throw new InvalidOperationException("Missing ModelServer:BaseUrl");

builder.Services.AddHttpClient<PythonModelServerClient>(client =>
{
    client.BaseAddress = new Uri(modelServerBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
});

var pythonPaths = builder.Configuration
    .GetSection("PythonPaths")
    .Get<PythonPaths>()
    ?? throw new InvalidOperationException("Missing PythonPaths configuration.");

builder.Services.AddSingleton(pythonPaths);

builder.Services.AddSingleton<PythonRunner>();
builder.Services.AddScoped<RelevanceService>();
builder.Services.AddScoped<DiagnosisService>();
builder.Services.AddScoped<RoutingService>();
builder.Services.AddScoped<DiagnosticModelRunnerService>();
builder.Services.AddScoped<DerivedCategoryService>();
builder.Services.AddScoped<InterviewFlowService>();
builder.Services.AddScoped<CandidateEvidenceGraphBuilder>();
builder.Services.AddScoped<CrossAnswerPatternAnalyzer>();
builder.Services.AddScoped<FinalScoreComposer>();
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<QuestionSuggestionService>();
builder.Services.AddScoped<JobQuestionMatcherService>();
builder.Services.AddScoped<RuntimeQuestionFactory>();
builder.Services.AddScoped<PersonalDataProtector>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<InterviewRuntimeStore>();

var app = builder.Build();

app.UseRateLimiter();
app.UseCors("ReactClient");
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MindMatchAI.Api v1");
});

app.MapGet("/", () => "MindMatchAI API is running");

app.MapGet("/api/company/scores", async (
    ClaimsPrincipal user,
    InterviewDbContext db) =>
{
    var role =
        user.FindFirst(ClaimTypes.Role)?.Value ??
        user.FindFirst("role")?.Value;

    if (!string.Equals(role, "Company", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    var companyIdValue = user.FindFirst("companyId")?.Value;

    if (!Guid.TryParse(companyIdValue, out var companyId))
    {
        return Results.BadRequest(new
        {
            message = "Company id was not found in the current user token."
        });
    }

    var scores = await (
        from finalScore in db.FinalCandidateScores
        join interview in db.Interviews on finalScore.InterviewId equals interview.Id
        join job in db.Jobs on interview.JobId equals job.Id
        where job.CompanyId == companyId
        orderby finalScore.CreatedAtUtc descending
        select new
        {
            finalScoreId = finalScore.Id,
            interviewId = finalScore.InterviewId,
            jobId = job.Id,
            jobTitle = job.Title,
            candidateId = interview.CandidateId,
            status = interview.Status,
            finalScore = finalScore.FinalScore,
            summary = finalScore.Summary,
            modelVersion = finalScore.ModelVersion,
            rawJson = finalScore.RawJson,
            createdAtUtc = finalScore.CreatedAtUtc
        }
    ).ToListAsync();

    return Results.Ok(new
    {
        scores
    });
})
.RequireAuthorization();

app.MapGet("/api/company/jobs/{jobId:guid}/ranking", async Task<IResult> (
    Guid jobId,
    ClaimsPrincipal user,
    InterviewDbContext db) =>
{
    var authError = RequireCompanyUser(user);

    if (authError != null)
    {
        return authError;
    }

    Guid companyId = GetCompanyId(user)!.Value;

    var job = await db.Jobs
        .FirstOrDefaultAsync(j => j.Id == jobId && j.CompanyId == companyId);

    if (job == null)
    {
        return Results.NotFound(new
        {
            status = "error",
            message = "Job not found for current company."
        });
    }

    var ranking = await (
        from finalScore in db.FinalCandidateScores
        join interview in db.Interviews on finalScore.InterviewId equals interview.Id
        where interview.JobId == jobId
        orderby finalScore.FinalScore descending, finalScore.CreatedAtUtc descending
        select new
        {
            finalScoreId = finalScore.Id,
            interviewId = finalScore.InterviewId,
            candidateId = interview.CandidateId,
            status = interview.Status,
            finalScore = finalScore.FinalScore,
            summary = finalScore.Summary,
            modelVersion = finalScore.ModelVersion,
            rawJson = finalScore.RawJson,
            createdAtUtc = finalScore.CreatedAtUtc
        }
    ).ToListAsync();

    return Results.Ok(new
    {
        status = "success",
        jobId = job.Id,
        jobTitle = job.Title,
        interviewDeadlineUtc = job.InterviewDeadlineUtc,
        isClosed = job.IsClosed,
        ranking
    });
})
.RequireAuthorization();

app.MapGet("/api/health", async Task<IResult> (InterviewDbContext db) =>
{
    try
    {
        bool canConnect = await db.Database.CanConnectAsync();

        return Results.Ok(new
        {
            status = "success",
            database = canConnect ? "connected" : "not connected"
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
});

app.MapPost("/api/speech/transcribe", async (
    HttpRequest request,
    IWebHostEnvironment env,
    PythonModelServerClient modelServerClient,
    PythonRunner pythonRunner,
    PythonPaths pythonPaths) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new
        {
            status = "error",
            message = "Audio file is required."
        });
    }

    var form = await request.ReadFormAsync();
    var audioFile = form.Files["audio"];

    if (audioFile == null || audioFile.Length == 0)
    {
        return Results.BadRequest(new
        {
            status = "error",
            message = "Audio file is missing."
        });
    }

    var uploadsDir = Path.Combine(env.ContentRootPath, "TempAudio");

    if (!Directory.Exists(uploadsDir))
    {
        Directory.CreateDirectory(uploadsDir);
    }

    var fileName = $"{Guid.NewGuid()}.webm";
    var audioPath = Path.Combine(uploadsDir, fileName);

    await using (var stream = File.Create(audioPath))
    {
        await audioFile.CopyToAsync(stream);
    }

    string output;
    string error = "";
    int exitCode = 0;

    try
    {
        try
        {
            output = modelServerClient.TranscribeFile(audioPath);
        }
        catch (Exception warmEx)
        {
            Console.WriteLine($"Warm transcription server failed, falling back to PythonRunner: {warmEx.Message}");

            var pythonResult = pythonRunner.Run(
                pythonPaths.AudioTranscriptionScript,
                audioPath
            );

            output = pythonResult.Output;
            error = pythonResult.Error;
            exitCode = pythonResult.ExitCode;
        }

        if (exitCode != 0)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = string.IsNullOrWhiteSpace(error)
                    ? "Transcription failed."
                    : error
            });
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;

            var status = root.GetProperty("status").GetString();

            if (status != "success")
            {
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : "Transcription failed.";

                return Results.BadRequest(new
                {
                    status = "error",
                    message
                });
            }

            var text = root.TryGetProperty("text", out var textElement)
                ? textElement.GetString()
                : "";

            return Results.Ok(new
            {
                status = "success",
                text
            });
        }
        catch
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Could not parse transcription output.",
                rawOutput = output,
                rawError = error
            });
        }
    }
    finally
    {
        try
        {
            if (File.Exists(audioPath))
            {
                File.Delete(audioPath);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }
})
.RequireAuthorization();

app.MapPost("/api/auth/company/register", async Task<IResult> (
    RegisterCompanyUserRequest request,
    InterviewDbContext db,
    PersonalDataProtector personalDataProtector,
    PasswordService passwordService,
    JwtTokenService jwtTokenService) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName) ||
            string.IsNullOrWhiteSpace(request.ContactName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Required fields are missing."
            });
        }

        if (request.Password.Length < 8)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Password must contain at least 8 characters."
            });
        }

        string normalizedEmail = request.Email.Trim().ToLowerInvariant();
        string? emailHash = personalDataProtector.CreateSearchHash(normalizedEmail);

        bool emailExists = await db.AppUsers
            .AnyAsync(u => u.EmailHash == emailHash);

        if (emailExists)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "A user with this email already exists."
            });
        }

        var company = new Company
        {
            Name = request.CompanyName.Trim(),
            ContactEmail = normalizedEmail,
            ContactEmailEncrypted = personalDataProtector.Protect(normalizedEmail) ?? "",
            ContactEmailHash = emailHash ?? "",
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var user = new AppUser
        {
            FullName = request.ContactName.Trim(),
            Role = "Company",
            EmailEncrypted = personalDataProtector.Protect(normalizedEmail) ?? "",
            EmailHash = emailHash ?? "",
            PasswordHash = passwordService.HashPassword(request.Password),
            CompanyId = company.Id
        };

        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        string token = jwtTokenService.CreateToken(user);

        return Results.Ok(new
        {
            status = "success",
            userId = user.Id,
            role = user.Role,
            companyId = company.Id,
            token
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
});

app.MapPost("/api/auth/candidate/register", async Task<IResult> (
    RegisterCandidateUserRequest request,
    InterviewDbContext db,
    PersonalDataProtector personalDataProtector,
    PasswordService passwordService,
    JwtTokenService jwtTokenService) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Required fields are missing."
            });
        }

        if (request.Password.Length < 8)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Password must contain at least 8 characters."
            });
        }

        string normalizedEmail = request.Email.Trim().ToLowerInvariant();
        string normalizedPhone = request.Phone.Trim();

        string? emailHash = personalDataProtector.CreateSearchHash(normalizedEmail);
        string? phoneHash = personalDataProtector.CreateSearchHash(normalizedPhone);

        bool emailExists = await db.AppUsers
            .AnyAsync(u => u.EmailHash == emailHash);

        if (emailExists)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "A user with this email already exists."
            });
        }

        var candidate = new Candidate
        {
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            EmailEncrypted = personalDataProtector.Protect(normalizedEmail) ?? "",
            EmailHash = emailHash ?? "",
            Phone = normalizedPhone,
            PhoneEncrypted = personalDataProtector.Protect(normalizedPhone) ?? "",
            PhoneHash = phoneHash ?? ""
        };

        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var user = new AppUser
        {
            FullName = request.FullName.Trim(),
            Role = "Candidate",
            EmailEncrypted = personalDataProtector.Protect(normalizedEmail) ?? "",
            EmailHash = emailHash ?? "",
            PasswordHash = passwordService.HashPassword(request.Password),
            CandidateId = candidate.Id
        };

        db.AppUsers.Add(user);
        await db.SaveChangesAsync();

        string token = jwtTokenService.CreateToken(user);

        return Results.Ok(new
        {
            status = "success",
            userId = user.Id,
            role = user.Role,
            candidateId = candidate.Id,
            token
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
});

app.MapPost("/api/auth/login", async Task<IResult> (
    LoginRequest request,
    InterviewDbContext db,
    PersonalDataProtector personalDataProtector,
    PasswordService passwordService,
    JwtTokenService jwtTokenService) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Email and password are required."
            });
        }

        string? emailHash = personalDataProtector.CreateSearchHash(request.Email);

        var user = await db.AppUsers
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash && u.IsActive);

        if (user == null)
        {
            return Results.Unauthorized();
        }

        bool passwordOk = passwordService.VerifyPassword(
            request.Password,
            user.PasswordHash
        );

        if (!passwordOk)
        {
            return Results.Unauthorized();
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        string token = jwtTokenService.CreateToken(user);

        return Results.Ok(new
        {
            status = "success",
            userId = user.Id,
            role = user.Role,
            companyId = user.CompanyId,
            candidateId = user.CandidateId,
            token
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
});

app.MapGet("/api/auth/me", IResult (ClaimsPrincipal user) =>
{
    Guid? userId = GetUserId(user);
    string? role = GetUserRole(user);
    Guid? companyId = GetCompanyId(user);
    Guid? candidateId = GetCandidateId(user);

    return Results.Ok(new
    {
        status = "success",
        userId,
        role,
        companyId,
        candidateId
    });
})
.RequireAuthorization();

app.MapPost("/api/jobs/open", async Task<IResult> (
    CreateOpenJobRequest request,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (string.IsNullOrWhiteSpace(request.JobTitle) ||
            string.IsNullOrWhiteSpace(request.JobDescription))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Job title and job description are required."
            });
        }

        if (request.JobTitle.Length > 200 ||
            request.JobDescription.Length > 5000)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "One or more fields are too long."
            });
        }

        var companyExists = await db.Companies
            .AnyAsync(c => c.Id == companyId);

        if (!companyExists)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Company not found for current user."
            });
        }

        if (!request.InterviewDeadline.HasValue)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Interview deadline is required."
            });
        }

        var selectedDate = request.InterviewDeadline.Value.Date;
        var today = DateTime.Today;
        var maxDeadline = today.AddDays(30);

        if (selectedDate < today)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Interview deadline cannot be in the past."
            });
        }

        if (selectedDate > maxDeadline)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Interview deadline cannot be more than 30 days from today."
            });
        }

        var endOfSelectedDay = selectedDate
            .AddDays(1)
            .AddTicks(-1);

        DateTime? deadlineUtc = DateTime.SpecifyKind(endOfSelectedDay, DateTimeKind.Local)
            .ToUniversalTime();

        var job = new Job
        {
            CompanyId = companyId,
            Title = request.JobTitle.Trim(),
            Description = request.JobDescription.Trim(),
            RequiredTraitsJson = "{}",
            InterviewDeadlineUtc = deadlineUtc,
            IsClosed = false
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            status = "success",
            jobId = job.Id,
            companyId = job.CompanyId,
            jobTitle = job.Title,
            jobDescription = job.Description,
            interviewDeadlineUtc = job.InterviewDeadlineUtc,
            isClosed = job.IsClosed,
            message = "Open job was created successfully."
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapGet("/api/jobs/open", async Task<IResult> (
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCandidateUser(user);

        if (authError != null)
        {
            return authError;
        }

        var now = DateTime.UtcNow;

        var jobs = await db.Jobs
            .Include(j => j.Company)
            .Where(j => !j.IsClosed)
            .Where(j => j.InterviewDeadlineUtc == null || j.InterviewDeadlineUtc >= now)
            .OrderBy(j => j.Title)
            .Select(j => new
            {
                jobId = j.Id,
                companyId = j.CompanyId,
                companyName = j.Company != null ? j.Company.Name : "",
                jobTitle = j.Title,
                jobDescription = j.Description,
                interviewDeadlineUtc = j.InterviewDeadlineUtc,
                questionsCount = db.InterviewQuestions.Count(q => q.JobId == j.Id)
            })
            .ToListAsync();

        return Results.Ok(new
        {
            status = "success",
            count = jobs.Count,
            jobs
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization();

app.MapPost("/api/jobs/{jobId:guid}/join", async Task<IResult> (
    Guid jobId,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCandidateUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid candidateId = GetCandidateId(user)!.Value;

        if (jobId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "JobId is required."
            });
        }

        var job = await db.Jobs
            .FirstOrDefaultAsync(j => j.Id == jobId);

        if (job == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Job not found."
            });
        }

        if (job.IsClosed ||
            (job.InterviewDeadlineUtc.HasValue && job.InterviewDeadlineUtc.Value < DateTime.UtcNow))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "This job is no longer active for interviews."
            });
        }

        var existingInterview = await db.Interviews
            .FirstOrDefaultAsync(i =>
                i.JobId == jobId &&
                i.CandidateId == candidateId);

        if (existingInterview != null)
        {
            return Results.Ok(new
            {
                status = "success",
                alreadyJoined = true,
                message = "You have already started this interview, so you cannot start it again.",
                interviewId = existingInterview.Id,
                jobId,
                candidateId
            });
        }

        var interview = new Interview
        {
            CandidateId = candidateId,
            JobId = job.Id,
            Status = "Started"
        };

        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            status = "success",
            message = "Candidate joined the job successfully.",
            interviewId = interview.Id,
            jobId = job.Id,
            candidateId
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapPost("/api/jobs/{jobId:guid}/questions", async Task<IResult> (
    Guid jobId,
    CreateQuestionRequest request,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (jobId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "JobId is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.QuestionText))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionText is required."
            });
        }

        if (request.QuestionText.Length > 3000)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionText is too long."
            });
        }

        if (request.OrderIndex < 1 || request.OrderIndex > 100)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "OrderIndex must be between 1 and 100."
            });
        }

        var job = await db.Jobs
            .FirstOrDefaultAsync(j =>
                j.Id == jobId &&
                j.CompanyId == companyId);

        if (job == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Job not found for current company."
            });
        }

        var question = new InterviewQuestion
        {
            JobId = jobId,
            QuestionText = request.QuestionText.Trim(),
            OrderIndex = request.OrderIndex,
            IsCompanyCustomQuestion = true,
            SourceQuestionBankVersion = "Custom",
            SourceQuestionBankItemId = null,
            ContentCoverageJson = "{}",
            DiagnosticCoverageJson = "{}",
            RecommendedScoringModelsJson = "[]",
            AnswerSignalsJson = "[]"
        };

        db.InterviewQuestions.Add(question);
        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            status = "success",
            jobId,
            questionId = question.Id,
            questionText = question.QuestionText,
            orderIndex = question.OrderIndex,
            sourceQuestionBankVersion = question.SourceQuestionBankVersion
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapPost("/api/jobs/{jobId:guid}/questions/from-bank", async Task<IResult> (
    Guid jobId,
    AddQuestionFromBankRequest request,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (jobId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "JobId is required."
            });
        }

        if (request.QuestionBankItemId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionBankItemId is required."
            });
        }

        var job = await db.Jobs
            .FirstOrDefaultAsync(j =>
                j.Id == jobId &&
                j.CompanyId == companyId);

        if (job == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Job not found for current company."
            });
        }

        var v2BankQuestion = await db.QuestionBankV2
            .FirstOrDefaultAsync(q =>
                q.Id == request.QuestionBankItemId &&
                q.IsActive);

        if (v2BankQuestion == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "QuestionBankV2 item not found."
            });
        }

        int orderIndex = request.OrderIndex;

        if (orderIndex <= 0)
        {
            int currentMaxOrder = await db.InterviewQuestions
                .Where(q => q.JobId == jobId)
                .Select(q => (int?)q.OrderIndex)
                .MaxAsync() ?? 0;

            orderIndex = currentMaxOrder + 1;
        }

        if (orderIndex > 100)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "OrderIndex must be between 0 and 100."
            });
        }

        if (!string.IsNullOrWhiteSpace(request.OverrideQuestionText) &&
            request.OverrideQuestionText.Length > 3000)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "OverrideQuestionText is too long."
            });
        }

        string questionText = string.IsNullOrWhiteSpace(request.OverrideQuestionText)
            ? v2BankQuestion.QuestionText
            : request.OverrideQuestionText.Trim();

        var question = new InterviewQuestion
        {
            JobId = jobId,
            QuestionText = questionText,
            OrderIndex = orderIndex,
            IsCompanyCustomQuestion = false,
            SourceQuestionBankVersion = "QuestionBankV2",
            SourceQuestionBankItemId = request.QuestionBankItemId,
            ContentCoverageJson = v2BankQuestion.ContentCoverageJson ?? "{}",
            DiagnosticCoverageJson = v2BankQuestion.DiagnosticCoverageJson ?? "{}",
            RecommendedScoringModelsJson = v2BankQuestion.RecommendedScoringModelsJson ?? "[]",
            AnswerSignalsJson = v2BankQuestion.AnswerSignalsJson ?? "[]"
        };

        db.InterviewQuestions.Add(question);
        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            status = "success",
            jobId,
            questionId = question.Id,
            questionText = question.QuestionText,
            orderIndex = question.OrderIndex,
            source = question.SourceQuestionBankVersion,
            questionBankItemId = request.QuestionBankItemId,
            diagnosticTarget = v2BankQuestion.DiagnosticTarget,
            level = "General",
            roleFamily = "General",
            tagsJson = v2BankQuestion.ContentCoverageJson,
            contentCoverageJson = question.ContentCoverageJson,
            diagnosticCoverageJson = question.DiagnosticCoverageJson,
            recommendedScoringModelsJson = question.RecommendedScoringModelsJson,
            answerSignalsJson = question.AnswerSignalsJson
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapGet("/api/interviews/{interviewId:guid}/questions", async Task<IResult> (
    Guid interviewId,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCandidateUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid candidateId = GetCandidateId(user)!.Value;

        if (interviewId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "InterviewId is required."
            });
        }

        var interview = await db.Interviews
            .Include(i => i.Job)
            .FirstOrDefaultAsync(i =>
                i.Id == interviewId &&
                i.CandidateId == candidateId);

        if (interview == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Interview not found for current candidate."
            });
        }

        var questions = await db.InterviewQuestions
            .Where(q => q.JobId == interview.JobId)
            .OrderBy(q => q.OrderIndex)
            .Select(q => new
            {
                questionId = q.Id,
                questionText = q.QuestionText,
                orderIndex = q.OrderIndex,
                source = q.SourceQuestionBankVersion,
                questionBankItemId = q.SourceQuestionBankItemId,
                contentCoverageJson = q.ContentCoverageJson,
                diagnosticCoverageJson = q.DiagnosticCoverageJson,
                recommendedScoringModelsJson = q.RecommendedScoringModelsJson,
                answerSignalsJson = q.AnswerSignalsJson
            })
            .ToListAsync();

        return Results.Ok(new
        {
            status = "success",
            interviewId,
            jobId = interview.JobId,
            jobTitle = interview.Job?.Title,
            count = questions.Count,
            questions
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization();

app.MapPost("/api/interviews/start", async Task<IResult> (
    StartInterviewRequest request,
    InterviewDbContext db,
    PersonalDataProtector personalDataProtector,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyIdFromToken = GetCompanyId(user)!.Value;

        if (string.IsNullOrWhiteSpace(request.CompanyName) ||
            string.IsNullOrWhiteSpace(request.CompanyEmail) ||
            string.IsNullOrWhiteSpace(request.JobTitle) ||
            string.IsNullOrWhiteSpace(request.JobDescription) ||
            string.IsNullOrWhiteSpace(request.CandidateName) ||
            string.IsNullOrWhiteSpace(request.CandidateEmail) ||
            string.IsNullOrWhiteSpace(request.CandidatePhone))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Required fields are missing."
            });
        }

        if (request.CompanyName.Length > 200 ||
            request.CompanyEmail.Length > 300 ||
            request.JobTitle.Length > 200 ||
            request.JobDescription.Length > 5000 ||
            request.CandidateName.Length > 200 ||
            request.CandidateEmail.Length > 300 ||
            request.CandidatePhone.Length > 50)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "One or more fields are too long."
            });
        }

        var company = await db.Companies
            .FirstOrDefaultAsync(c => c.Id == companyIdFromToken);

        if (company == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Company not found for current user."
            });
        }

        var job = new Job
        {
            CompanyId = company.Id,
            Title = request.JobTitle,
            Description = request.JobDescription,
            RequiredTraitsJson = "{}"
        };

        db.Jobs.Add(job);
        await db.SaveChangesAsync();

        string? candidateEmailHash =
            personalDataProtector.CreateSearchHash(request.CandidateEmail);

        var existingCandidateUser = await db.AppUsers
            .Where(u =>
                u.Role == "Candidate" &&
                u.EmailHash == candidateEmailHash &&
                u.CandidateId.HasValue &&
                u.IsActive)
            .FirstOrDefaultAsync();

        Candidate candidate;

        if (existingCandidateUser != null)
        {
            var existingCandidate = await db.Candidates
                .FirstOrDefaultAsync(c => c.Id == existingCandidateUser.CandidateId!.Value);

            if (existingCandidate == null)
            {
                return Results.NotFound(new
                {
                    status = "error",
                    message = "Registered candidate was found, but candidate record is missing."
                });
            }

            candidate = existingCandidate;
        }
        else
        {
            string normalizedCandidateEmail = request.CandidateEmail.Trim().ToLowerInvariant();
            string normalizedCandidatePhone = request.CandidatePhone.Trim();

            candidate = new Candidate
            {
                FullName = request.CandidateName.Trim(),
                Email = normalizedCandidateEmail,
                EmailEncrypted = personalDataProtector.Protect(normalizedCandidateEmail) ?? "",
                EmailHash = candidateEmailHash ?? "",
                Phone = normalizedCandidatePhone,
                PhoneEncrypted = personalDataProtector.Protect(normalizedCandidatePhone) ?? "",
                PhoneHash = personalDataProtector.CreateSearchHash(normalizedCandidatePhone)
            };

            db.Candidates.Add(candidate);
            await db.SaveChangesAsync();
        }

        var interview = new Interview
        {
            CandidateId = candidate.Id,
            JobId = job.Id,
            Status = "Started"
        };

        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        return Results.Ok(new
        {
            status = "success",
            companyId = company.Id,
            jobId = job.Id,
            candidateId = candidate.Id,
            interviewId = interview.Id
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapGet("/api/jobs/{jobId:guid}/suggest-questions", async Task<IResult> (
    Guid jobId,
    int? count,
    QuestionSuggestionService questionSuggestionService,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (jobId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "JobId is required."
            });
        }

        var jobExistsForCompany = await db.Jobs
            .AnyAsync(j => j.Id == jobId && j.CompanyId == companyId);

        if (!jobExistsForCompany)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Job not found for current company."
            });
        }

        int requestedCount = count ?? 5;

        if (requestedCount < 1 || requestedCount > 20)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "Count must be between 1 and 20."
            });
        }

        var questions = questionSuggestionService.SuggestQuestionsForJob(
            jobId,
            requestedCount
        );

        return Results.Ok(new
        {
            status = "success",
            jobId,
            count = questions.Count,
            questions = questions.Select(q => new
            {
                questionBankItemId = q.Id,
                questionText = q.QuestionText,
                diagnosticTarget = q.DiagnosticTarget,
                level = q.Level,
                roleFamily = q.RoleFamily,
                tagsJson = q.TagsJson
            })
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapPost("/api/interviews/{interviewId:guid}/questions", async Task<IResult> (
    Guid interviewId,
    CreateQuestionRequest request,
    InterviewDbContext db,
    InterviewRuntimeStore runtimeStore,
    RuntimeQuestionFactory runtimeQuestionFactory,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (interviewId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "InterviewId is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.QuestionText))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionText is required."
            });
        }

        if (request.QuestionText.Length > 3000)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionText is too long."
            });
        }

        if (request.OrderIndex < 1 || request.OrderIndex > 100)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "OrderIndex must be between 1 and 100."
            });
        }

        var interview = await db.Interviews
            .Include(i => i.Job)
            .FirstOrDefaultAsync(i =>
                i.Id == interviewId &&
                i.Job != null &&
                i.Job.CompanyId == companyId);

        if (interview == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Interview not found for current company."
            });
        }

        var question = new InterviewQuestion
        {
            JobId = interview.JobId,
            QuestionText = request.QuestionText,
            OrderIndex = request.OrderIndex,
            IsCompanyCustomQuestion = request.IsCompanyCustomQuestion,
            SourceQuestionBankVersion = "Custom",
            SourceQuestionBankItemId = null,
            ContentCoverageJson = "{}",
            DiagnosticCoverageJson = "{}",
            RecommendedScoringModelsJson = "[]",
            AnswerSignalsJson = "[]"
        };

        db.InterviewQuestions.Add(question);
        await db.SaveChangesAsync();

        if (!runtimeStore.TryGet(interviewId, out var runtimeState) || runtimeState == null)
        {
            runtimeState = new InterviewRuntimeState
            {
                InterviewId = interview.Id,
                JobId = interview.JobId,
                CandidateId = interview.CandidateId,
                JobTitle = interview.Job?.Title ?? "",
                JobDescription = interview.Job?.Description ?? "",
                Status = InterviewRuntimeStatus.Created
            };

            runtimeStore.CreateOrReplace(runtimeState);
        }

        var runtimeQuestion = runtimeQuestionFactory.FromManualQuestion(
            questionText: question.QuestionText,
            orderIndex: question.OrderIndex
        );

        runtimeQuestion.RuntimeQuestionId = question.Id;
        runtimeQuestion.SourceQuestionBankVersion = "Custom";

        runtimeState.AddQuestion(runtimeQuestion);
        runtimeState.Status = InterviewRuntimeStatus.QuestionsSelected;
        runtimeStore.Update(runtimeState);

        return Results.Ok(new
        {
            status = "success",
            interviewId,
            questionId = question.Id,
            questionText = question.QuestionText,
            orderIndex = question.OrderIndex,
            sourceQuestionBankVersion = question.SourceQuestionBankVersion
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapPost("/api/interviews/{interviewId:guid}/questions/from-bank", async Task<IResult> (
    Guid interviewId,
    AddQuestionFromBankRequest request,
    InterviewDbContext db,
    InterviewRuntimeStore runtimeStore,
    RuntimeQuestionFactory runtimeQuestionFactory,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (interviewId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "InterviewId is required."
            });
        }

        if (request.QuestionBankItemId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionBankItemId is required."
            });
        }

        if (request.OrderIndex < 0 || request.OrderIndex > 100)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "OrderIndex must be between 0 and 100."
            });
        }

        if (!string.IsNullOrWhiteSpace(request.OverrideQuestionText) &&
            request.OverrideQuestionText.Length > 3000)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "OverrideQuestionText is too long."
            });
        }

        var interview = await db.Interviews
            .Include(i => i.Job)
            .FirstOrDefaultAsync(i =>
                i.Id == interviewId &&
                i.Job != null &&
                i.Job.CompanyId == companyId);

        if (interview == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Interview not found for current company."
            });
        }

        var v2BankQuestion = await db.QuestionBankV2
            .FirstOrDefaultAsync(q => q.Id == request.QuestionBankItemId && q.IsActive);

        if (v2BankQuestion == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "QuestionBankV2 item not found"
            });
        }

        int orderIndex = request.OrderIndex;

        if (orderIndex <= 0)
        {
            int currentMaxOrder = await db.InterviewQuestions
                .Where(q => q.JobId == interview.JobId)
                .Select(q => (int?)q.OrderIndex)
                .MaxAsync() ?? 0;

            orderIndex = currentMaxOrder + 1;
        }

        string questionText = string.IsNullOrWhiteSpace(request.OverrideQuestionText)
            ? v2BankQuestion.QuestionText
            : request.OverrideQuestionText.Trim();

        string diagnosticTarget = v2BankQuestion.DiagnosticTarget;
        string level = "General";
        string roleFamily = "General";
        string tagsJson = v2BankQuestion.ContentCoverageJson;

        var question = new InterviewQuestion
        {
            JobId = interview.JobId,
            QuestionText = questionText,
            OrderIndex = orderIndex,
            IsCompanyCustomQuestion = false,
            SourceQuestionBankVersion = "QuestionBankV2",
            SourceQuestionBankItemId = request.QuestionBankItemId,
            ContentCoverageJson = v2BankQuestion.ContentCoverageJson ?? "{}",
            DiagnosticCoverageJson = v2BankQuestion.DiagnosticCoverageJson ?? "{}",
            RecommendedScoringModelsJson = v2BankQuestion.RecommendedScoringModelsJson ?? "[]",
            AnswerSignalsJson = v2BankQuestion.AnswerSignalsJson ?? "[]"
        };

        db.InterviewQuestions.Add(question);
        await db.SaveChangesAsync();

        if (!runtimeStore.TryGet(interviewId, out var runtimeState) || runtimeState == null)
        {
            runtimeState = new InterviewRuntimeState
            {
                InterviewId = interview.Id,
                JobId = interview.JobId,
                CandidateId = interview.CandidateId,
                JobTitle = interview.Job?.Title ?? "",
                JobDescription = interview.Job?.Description ?? "",
                Status = InterviewRuntimeStatus.Created
            };

            runtimeStore.CreateOrReplace(runtimeState);
        }

        RuntimeQuestion runtimeQuestion = runtimeQuestionFactory.FromQuestionBankV2(
            item: v2BankQuestion,
            orderIndex: orderIndex,
            matchScore: null,
            overrideQuestionText: request.OverrideQuestionText
        );

        runtimeQuestion.RuntimeQuestionId = question.Id;

        runtimeState.AddQuestion(runtimeQuestion);
        runtimeState.Status = InterviewRuntimeStatus.QuestionsSelected;

        runtimeStore.Update(runtimeState);

        return Results.Ok(new
        {
            status = "success",
            interviewId,
            jobId = interview.JobId,
            questionId = question.Id,
            questionText = question.QuestionText,
            orderIndex = question.OrderIndex,
            source = question.SourceQuestionBankVersion,
            questionBankItemId = request.QuestionBankItemId,
            diagnosticTarget,
            level,
            roleFamily,
            tagsJson,
            contentCoverageJson = question.ContentCoverageJson,
            diagnosticCoverageJson = question.DiagnosticCoverageJson,
            recommendedScoringModelsJson = question.RecommendedScoringModelsJson,
            answerSignalsJson = question.AnswerSignalsJson
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapPost("/api/interviews/{interviewId:guid}/answers", async Task<IResult> (
    Guid interviewId,
    SubmitAnswerRequest request,
    InterviewFlowService interviewFlowService,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCandidateUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid candidateId = GetCandidateId(user)!.Value;

        if (interviewId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "InterviewId is required."
            });
        }

        bool interviewBelongsToCandidate = await db.Interviews
            .AnyAsync(i => i.Id == interviewId && i.CandidateId == candidateId);

        if (!interviewBelongsToCandidate)
        {
            return Results.Forbid();
        }

        if (request.QuestionId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionId is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.QuestionText))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionText is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.AnswerText))
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "AnswerText is required."
            });
        }

        if (request.QuestionText.Length > 3000)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "QuestionText is too long."
            });
        }

        if (request.AnswerText.Length > 8000)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "AnswerText is too long."
            });
        }

        if (request.AnswerOrder < 1 || request.AnswerOrder > 100)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "AnswerOrder must be between 1 and 100."
            });
        }

        if (request.AttemptNumber < 1 || request.AttemptNumber > 4)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "AttemptNumber must be between 1 and 4."
            });
        }

        if (request.MaxAttemptsPerQuestion < 1 || request.MaxAttemptsPerQuestion > 5)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "MaxAttemptsPerQuestion must be between 1 and 5."
            });
        }

        InterviewAnswer answer = interviewFlowService.ProcessAnswer(
            interviewId: interviewId,
            questionId: request.QuestionId,
            questionText: request.QuestionText,
            answerText: request.AnswerText,
            answerOrder: request.AnswerOrder,
            attemptNumber: request.AttemptNumber,
            parentAnswerId: request.ParentAnswerId,
            maxAttemptsPerQuestion: request.MaxAttemptsPerQuestion
        );

        return Results.Ok(new
        {
            status = "success",
            answerId = answer.Id,
            interviewId = answer.InterviewId,
            questionId = answer.QuestionId,
            relevanceStatus = answer.RelevanceStatus,
            relevanceConfidence = answer.RelevanceConfidence,
            guidanceMessage = answer.GuidanceMessage,
            attemptNumber = answer.AttemptNumber,
            parentAnswerId = answer.ParentAnswerId,
            isFinalAttemptForQuestion = answer.IsFinalAttemptForQuestion
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapPost("/api/interviews/{interviewId:guid}/finish", async Task<IResult> (
    Guid interviewId,
    ScoringService scoringService,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (interviewId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "InterviewId is required."
            });
        }

        bool interviewBelongsToCompany = await db.Interviews
            .Include(i => i.Job)
            .AnyAsync(i =>
                i.Id == interviewId &&
                i.Job != null &&
                i.Job.CompanyId == companyId);

        if (!interviewBelongsToCompany)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Interview not found for current company."
            });
        }

        var score = scoringService.CalculateAndSaveInterviewScore(interviewId);

        if (score == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Interview not found"
            });
        }

        return Results.Ok(new
        {
            status = "success",
            interviewId = score.InterviewId,
            scoreId = score.Id,
            finalScore = score.FinalScore,
            summary = score.Summary,
            rawJson = score.RawJson,
            modelVersion = score.ModelVersion,
            createdAtUtc = score.CreatedAtUtc
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization()
.RequireRateLimiting("HeavyEndpoints");

app.MapGet("/api/interviews/{interviewId:guid}/score", async Task<IResult> (
    Guid interviewId,
    InterviewDbContext db,
    ClaimsPrincipal user) =>
{
    try
    {
        var authError = RequireCompanyUser(user);

        if (authError != null)
        {
            return authError;
        }

        Guid companyId = GetCompanyId(user)!.Value;

        if (interviewId == Guid.Empty)
        {
            return Results.BadRequest(new
            {
                status = "error",
                message = "InterviewId is required."
            });
        }

        bool interviewBelongsToCompany = await db.Interviews
            .Include(i => i.Job)
            .AnyAsync(i =>
                i.Id == interviewId &&
                i.Job != null &&
                i.Job.CompanyId == companyId);

        if (!interviewBelongsToCompany)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Interview not found for current company."
            });
        }

        var score = await db.FinalCandidateScores
            .Where(s => s.InterviewId == interviewId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (score == null)
        {
            return Results.NotFound(new
            {
                status = "error",
                message = "Score not found for this interview"
            });
        }

        return Results.Ok(new
        {
            status = "success",
            scoreId = score.Id,
            interviewId = score.InterviewId,
            finalScore = score.FinalScore,
            summary = score.Summary,
            rawJson = score.RawJson,
            modelVersion = score.ModelVersion,
            createdAtUtc = score.CreatedAtUtc
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex);
        return Results.Problem("An internal server error occurred.");
    }
})
.RequireAuthorization();

static Guid? GetUserId(ClaimsPrincipal user)
{
    string? value = user.FindFirst("userId")?.Value;

    if (Guid.TryParse(value, out Guid userId))
    {
        return userId;
    }

    return null;
}

static string? GetUserRole(ClaimsPrincipal user)
{
    return user.FindFirst("role")?.Value
        ?? user.FindFirst(ClaimTypes.Role)?.Value;
}

static Guid? GetCompanyId(ClaimsPrincipal user)
{
    string? value = user.FindFirst("companyId")?.Value;

    if (Guid.TryParse(value, out Guid companyId))
    {
        return companyId;
    }

    return null;
}

static Guid? GetCandidateId(ClaimsPrincipal user)
{
    string? value = user.FindFirst("candidateId")?.Value;

    if (Guid.TryParse(value, out Guid candidateId))
    {
        return candidateId;
    }

    return null;
}

static IResult? RequireCompanyUser(ClaimsPrincipal user)
{
    string? role = GetUserRole(user);
    Guid? companyId = GetCompanyId(user);

    if (!string.Equals(role, "Company", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    if (!companyId.HasValue)
    {
        return Results.Forbid();
    }

    return null;
}

static IResult? RequireCandidateUser(ClaimsPrincipal user)
{
    string? role = GetUserRole(user);
    Guid? candidateId = GetCandidateId(user);

    if (!string.Equals(role, "Candidate", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Forbid();
    }

    if (!candidateId.HasValue)
    {
        return Results.Forbid();
    }

    return null;
}

app.Run();

public class CreateOpenJobRequest
{
    public string JobTitle { get; set; } = "";
    public string JobDescription { get; set; } = "";
    public DateTime? InterviewDeadline { get; set; }
}

public class StartInterviewRequest
{
    public string CompanyName { get; set; } = "";
    public string CompanyEmail { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string JobDescription { get; set; } = "";
    public string CandidateName { get; set; } = "";
    public string CandidateEmail { get; set; } = "";
    public string CandidatePhone { get; set; } = "";
}

public class CreateQuestionRequest
{
    public string QuestionText { get; set; } = "";
    public int OrderIndex { get; set; } = 1;
    public bool IsCompanyCustomQuestion { get; set; } = false;
}

public class AddQuestionFromBankRequest
{
    public Guid QuestionBankItemId { get; set; }
    public string? OverrideQuestionText { get; set; }
    public int OrderIndex { get; set; } = 0;
}

public class SubmitAnswerRequest
{
    public Guid QuestionId { get; set; }
    public string QuestionText { get; set; } = "";
    public string AnswerText { get; set; } = "";
    public int AnswerOrder { get; set; } = 1;
    public int AttemptNumber { get; set; } = 1;
    public Guid? ParentAnswerId { get; set; }
    public int MaxAttemptsPerQuestion { get; set; } = 3;
}

public class RegisterCompanyUserRequest
{
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public class RegisterCandidateUserRequest
{
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Password { get; set; } = "";
}

public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
