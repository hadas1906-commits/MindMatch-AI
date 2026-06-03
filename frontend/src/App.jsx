import { useEffect, useMemo, useState } from "react";

const API_BASE_URL = "http://localhost:5275";

const JOB_TITLE_OPTIONS = [
  "Backend Developer",
  "Frontend Developer",
  "Full Stack Developer",
  "Data Analyst",
  "QA Engineer",
  "DevOps Engineer",
  "Product Manager",
  "UI/UX Designer",
  "Customer Success Manager",
  "Sales Representative",
  "Other",
];

function saveSession(session) {
  localStorage.setItem("mindmatch_session", JSON.stringify(session));
}

function loadSession() {
  try {
    const raw = localStorage.getItem("mindmatch_session");
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function clearSession() {
  localStorage.removeItem("mindmatch_session");
}

function authHeaders(token) {
  return {
    "Content-Type": "application/json",
    Authorization: `Bearer ${token}`,
  };
}

async function apiRequest(path, options = {}) {
  const response = await fetch(`${API_BASE_URL}${path}`, options);
  const text = await response.text();
  let data = null;

  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = { raw: text };
    }
  }

  if (!response.ok) {
    const message =
      data?.message ||
      data?.title ||
      data?.raw ||
      `Request failed with status ${response.status}`;
    throw new Error(message);
  }

  return data;
}


async function transcribeAudioBlob(audioBlob, token) {
  const formData = new FormData();
  const extension = audioBlob.type.includes("mp4") ? "m4a" : "webm";
  const fileName = `recording.${extension}`;

  formData.append("audio", audioBlob, fileName);
  formData.append("file", audioBlob, fileName);
  formData.append("audioFile", audioBlob, fileName);
  formData.append("source", "browser-microphone");

  const response = await fetch(`${API_BASE_URL}/api/speech/transcribe`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
    },
    body: formData,
  });

  let data = null;
  const text = await response.text();

  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = { raw: text };
    }
  }

  if (!response.ok) {
    throw new Error(data?.message || data?.title || data?.raw || "Could not transcribe the recording.");
  }

  const transcript =
    data?.text ||
    data?.transcript ||
    data?.Transcript ||
    data?.result?.text ||
    data?.result?.transcript ||
    "";

  if (!String(transcript).trim()) {
    throw new Error("The recording was uploaded, but no transcript was returned. Check that the API forwards the saved audio file to the Python transcription model.");
  }

  return transcript;
}

function isDuplicateUserMessage(message) {
  const text = String(message || "").toLowerCase();
  return (
    text.includes("already exists") ||
    text.includes("email already") ||
    text.includes("user with this email") ||
    text.includes("duplicate")
  );
}

function countSentences(text) {
  const trimmed = String(text || "").trim();
  if (!trimmed) return 0;

  const sentenceMatches = trimmed
    .split(/[.!?؟。]+/)
    .map((part) => part.trim())
    .filter((part) => part.length >= 12);

  if (sentenceMatches.length > 0) return sentenceMatches.length;

  const words = trimmed.split(/\s+/).filter(Boolean);
  return words.length >= 18 ? 2 : 1;
}


function getDateInputValue(date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function getTodayDateInputValue() {
  return getDateInputValue(new Date());
}

function getDateInputConstraints() {
  const today = new Date();
  const maxDate = new Date();
  maxDate.setDate(today.getDate() + 30);

  return {
    min: getDateInputValue(today), // תאריך של היום
    max: getDateInputValue(maxDate) // תאריך של עוד 30 יום
  };
}

function clampDeadlineInputValue(value) {
  const today = getTodayDateInputValue();
  const max = getMaxDeadlineDateInputValue();

  if (!value) return max;
  if (value < today) return today;
  if (value > max) return max;
  return value;
}

function formatDisplayDate(dateValue) {
  if (!dateValue) return "";
  const date = new Date(dateValue);
  if (Number.isNaN(date.getTime())) return "";
  return date.toLocaleDateString();
}


function getMaxDeadlineDateInputValue() {
  const maxDate = new Date();
  maxDate.setDate(maxDate.getDate() + 30);
  return getDateInputValue(maxDate);
}

function getJobDeadlineValue(job) {
  return job?.interviewDeadlineUtc || job?.interviewDeadline || job?.deadline || job?.deadlineUtc || "";
}

function isDeadlineExpired(deadlineValue) {
  if (!deadlineValue) return false;

  const deadline = new Date(deadlineValue);
  if (Number.isNaN(deadline.getTime())) return false;

  const endOfDeadlineDay = new Date(deadline);
  endOfDeadlineDay.setHours(23, 59, 59, 999);

  return endOfDeadlineDay < new Date();
}

function filterActiveJobs(jobs) {
  return (jobs || []).filter((job) => !isDeadlineExpired(getJobDeadlineValue(job)));
}

function getScoreDeadlineValue(scoreItem) {
  return (
    scoreItem?.interviewDeadlineUtc ||
    scoreItem?.interviewDeadline ||
    scoreItem?.jobInterviewDeadlineUtc ||
    scoreItem?.jobInterviewDeadline ||
    scoreItem?.jobDeadlineUtc ||
    scoreItem?.jobDeadline ||
    scoreItem?.deadlineUtc ||
    scoreItem?.deadline ||
    scoreItem?.job?.interviewDeadlineUtc ||
    scoreItem?.job?.interviewDeadline ||
    scoreItem?.job?.deadlineUtc ||
    scoreItem?.job?.deadline ||
    ""
  );
}

function isClosedScoreItem(scoreItem) {
  if (scoreItem?.jobIsClosed === true || scoreItem?.isClosed === true) return true;

  const status = String(scoreItem?.jobStatus || scoreItem?.status || "").toLowerCase();
  if (["closed", "completed", "finished", "expired"].includes(status)) return true;

  const deadline = getScoreDeadlineValue(scoreItem);
  return Boolean(deadline) && isDeadlineExpired(deadline);
}

function Field({ label, value, onChange, type = "text", placeholder = "", min, max }) {
  return (
    <label className="field">
      <span>{label}</span>
      <input
        type={type}
        value={value}
        placeholder={placeholder}
        min={min}
        max={max}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  );
}

function PasswordField({ label, value, onChange, placeholder = "" }) {
  const [visible, setVisible] = useState(false);

  return (
    <label className="field">
      <span>{label}</span>
      <div className="password-input-wrap">
        <input
          type={visible ? "text" : "password"}
          value={value}
          placeholder={placeholder}
          onChange={(event) => onChange(event.target.value)}
        />
        <button
          type="button"
          className="password-eye-btn"
          onClick={() => setVisible((current) => !current)}
          aria-label={visible ? "Hide password" : "Show password"}
        >
          {visible ? "🙈" : "👁"}
        </button>
      </div>
    </label>
  );
}

function TextArea({ label, value, onChange, placeholder = "", rows = 5 }) {
  return (
    <label className="field">
      <span>{label}</span>
      <textarea
        value={value}
        placeholder={placeholder}
        rows={rows}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  );
}


function VoiceTextArea({
  label,
  value,
  onChange,
  token,
  placeholder = "",
  rows = 5,
}) {
  const [isRecording, setIsRecording] = useState(false);
  const [mediaRecorder, setMediaRecorder] = useState(null);
  const [voiceError, setVoiceError] = useState("");

  async function startRecording() {
    setVoiceError("");

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const preferredMimeTypes = [
        "audio/webm;codecs=opus",
        "audio/webm",
        "audio/mp4",
      ];
      const mimeType = preferredMimeTypes.find((type) => MediaRecorder.isTypeSupported(type));
      const recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
      const chunks = [];

      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) {
          chunks.push(event.data);
        }
      };

      recorder.onstop = async () => {
        try {
          const audioBlob = new Blob(chunks, { type: recorder.mimeType || "audio/webm" });
          const transcript = await transcribeAudioBlob(audioBlob, token);

          if (transcript.trim()) {
            const nextValue = value?.trim()
              ? `${value.trim()} ${transcript.trim()}`
              : transcript.trim();

            onChange(nextValue);
          }
        } catch (err) {
          setVoiceError(err.message);
        } finally {
          stream.getTracks().forEach((track) => track.stop());
        }
      };

      recorder.start();
      setMediaRecorder(recorder);
      setIsRecording(true);
    } catch {
      setVoiceError("Could not access the microphone. Make sure the browser has recording permission.");
    }
  }

  function stopRecording() {
    if (mediaRecorder && mediaRecorder.state !== "inactive") {
      mediaRecorder.stop();
    }

    setIsRecording(false);
    setMediaRecorder(null);
  }

  function toggleRecording() {
    if (isRecording) {
      stopRecording();
    } else {
      startRecording();
    }
  }

  return (
    <label className="field voice-field">
      <span>{label}</span>

      <div className="voice-textarea-wrap">
        <textarea
          value={value}
          placeholder={placeholder}
          rows={rows}
          onChange={(event) => onChange(event.target.value)}
        />

        <button
          type="button"
          className={isRecording ? "mic-btn recording" : "mic-btn"}
          onClick={toggleRecording}
          title={isRecording ? "Stop recording" : "Voice recording"}
        >
          {isRecording ? "■" : "🎙"}
        </button>
      </div>

      {voiceError && <small className="voice-error">{voiceError}</small>}
    </label>
  );
}

function SelectField({ label, value, onChange, children }) {
  return (
    <label className="field">
      <span>{label}</span>
      <select value={value} onChange={(event) => onChange(event.target.value)}>
        {children}
      </select>
    </label>
  );
}

function Message({ error, success }) {
  if (!error && !success) return null;
  return <div className={error ? "message error" : "message success"}>{error || success}</div>;
}

function EmptyState({ title, text }) {
  return (
    <section className="empty-state">
      <h3>{title}</h3>
      <p>{text}</p>
    </section>
  );
}

function Header({ session, setSession, setPage, currentPageKey, shared = {} }) {
  function logout() {
    clearSession();
    setSession(null);
    setPage("login");
  }

  const homePage =
    session?.role === "Company"
      ? "companyDashboard"
      : session?.role === "Candidate"
        ? "candidateDashboard"
        : "login";

  return (
    <header className="app-header">
      <div className="brand" onClick={() => setPage(homePage)}>
        <div className="logo-shell">
          <img src="/logo.png" alt="MindMatch AI" className="logo-image" />
        </div>
        <div className="brand-copy">
          <strong>MindMatch AI</strong>
          <span>Precision Matching for Real Potential</span>
        </div>
      </div>

      <nav>
        {!session && (
          <div className="tab-buttons-container">
            <button className={currentPageKey === "login" ? "active" : ""} onClick={() => setPage("login")}>Login</button>
            <button className={currentPageKey === "registerCompany" ? "active" : ""} onClick={() => setPage("registerCompany")}>Company Registration</button>
            <button className={currentPageKey === "registerCandidate" ? "active" : ""} onClick={() => setPage("registerCandidate")}>Candidate Registration</button>
          </div>
        )}

        {session?.role === "Company" && (
          <div className="tab-buttons-container">
            <button
              className={currentPageKey === "companyDashboard" ? "active" : ""}
              onClick={() => setPage("companyDashboard")}
            >
              Company Area
            </button>
          </div>
        )}

        {session?.role === "Candidate" && (
          <div className="tab-buttons-container">
            <button
              className={currentPageKey === "candidateDashboard" ? "active" : ""}
              onClick={() => setPage("candidateDashboard")}
            >
              Candidate Area
            </button>
          </div>
        )}

        {session && <button className="logout-btn" onClick={logout}>Logout</button>}
      </nav>
    </header>
  );
}


function LoginPage({ setSession, setPage, prefilledEmail = "" }) {
  const [email, setEmail] = useState(prefilledEmail || "companyauth@test.com");
  const [password, setPassword] = useState("Password123!");
  const [error, setError] = useState("");

  async function submit(event) {
    event.preventDefault();
    setError("");

    try {
      const data = await apiRequest("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });

      const session = {
        token: data.token,
        userId: data.userId,
        role: data.role,
        companyId: data.companyId || null,
        candidateId: data.candidateId || null,
      };

      saveSession(session);
      setSession(session);
      setPage(data.role === "Company" ? "companyDashboard" : "candidateDashboard");
    } catch (err) {
      setError(err.message);
    }
  }

  return (
    <main className="page-card login-card">
  <h2>Login</h2>
      <p className="muted">A smart system for matching questions to jobs, analyzing candidate answers, and generating a clear fit score.</p>

      <form onSubmit={submit} className="form-grid">
        <Field label="Email Address" value={email} onChange={setEmail} />
        <PasswordField label="Password" value={password} onChange={setPassword} />
        <button type="submit" className="action-btn">Login to System</button>
      </form>

      <Message error={error} />
    </main>
  );
}

function RegisterCompanyPage({ setSession, setPage, setPrefilledEmail }) {
  const [companyName, setCompanyName] = useState("MindMatch Test Company");
  const [contactName, setContactName] = useState("Company Manager");
  const [email, setEmail] = useState(`company${Date.now()}@test.com`);
  const [password, setPassword] = useState("Password123!");
  const [cardHolderName, setCardHolderName] = useState("Company Manager");
  const [cardNumber, setCardNumber] = useState("");
  const [cardExpiry, setCardExpiry] = useState("");
  const [cardCvv, setCardCvv] = useState("");
  const [error, setError] = useState("");

  async function submit(event) {
    event.preventDefault();
    setError("");

    const cleanCardNumber = cardNumber.replace(/\s+/g, "");

    if (!cardHolderName.trim() || cleanCardNumber.length < 12 || !cardExpiry.trim() || cardCvv.trim().length < 3) {
      setError("Please enter valid billing details before creating a company account.");
      return;
    }

    try {
      const data = await apiRequest("/api/auth/company/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          companyName,
          contactName,
          email,
          password,
          billingDetails: {
            cardHolderName,
            cardLast4: cleanCardNumber.slice(-4),
            cardExpiry,
            paymentMethod: "credit-card-demo",
          },
        }),
      });

      const session = {
        token: data.token,
        userId: data.userId,
        role: data.role,
        companyId: data.companyId || null,
        candidateId: null,
      };

      saveSession(session);
      setSession(session);
      setPage("companyDashboard");
    } catch (err) {
      if (isDuplicateUserMessage(err.message)) {
        setPrefilledEmail(email);
        setPage("login");
        return;
      }

      setError(err.message);
    }
  }

  return (
    <main className="page-card register-company-card">
      <h2>Company Registration</h2>
      <p className="muted">Create a company account to manage open jobs, matched questions, and candidate scores.</p>

      <section className="billing-info-card">
        <span className="eyebrow">Billing</span>
        <h3>Payment details are required before publishing jobs</h3>
        <p>
          This screen collects billing details for the company account. In production, payment should be processed through a secure payment provider and tokenized on the server.
        </p>
      </section>

      <form onSubmit={submit} className="form-grid">
        <Field label="Company Name" value={companyName} onChange={setCompanyName} />
        <Field label="Contact Name" value={contactName} onChange={setContactName} />
        <Field label="Company Email" value={email} onChange={setEmail} />
        <PasswordField label="Password" value={password} onChange={setPassword} />

        <div className="form-section-title">Credit Card Details</div>
        <Field label="Cardholder Name" value={cardHolderName} onChange={setCardHolderName} />
        <Field label="Card Number" value={cardNumber} onChange={setCardNumber} placeholder="1234 5678 9012 3456" />
        <div className="billing-form-row">
          <Field label="Expiry" value={cardExpiry} onChange={setCardExpiry} placeholder="MM/YY" />
          <PasswordField label="CVV" value={cardCvv} onChange={setCardCvv} placeholder="123" />
        </div>

        <button type="submit" className="action-btn">Register and Login</button>
      </form>
      <Message error={error} />
    </main>
  );
}


function RegisterCandidatePage({ setSession, setPage, setPrefilledEmail }) {
  const [fullName, setFullName] = useState("Test Candidate Auth");
  const [email, setEmail] = useState(`candidate${Date.now()}@test.com`);
  const [phone, setPhone] = useState("0501234567");
  const [password, setPassword] = useState("Password123!");
  const [error, setError] = useState("");

  async function submit(event) {
    event.preventDefault();
    setError("");

    try {
      const data = await apiRequest("/api/auth/candidate/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ fullName, email, phone, password }),
      });

      const session = {
        token: data.token,
        userId: data.userId,
        role: data.role,
        companyId: null,
        candidateId: data.candidateId || null,
      };

      saveSession(session);
      setSession(session);
      setPage("candidateDashboard");
    } catch (err) {
      if (isDuplicateUserMessage(err.message)) {
        setPrefilledEmail(email);
        setPage("login");
        return;
      }

      setError(err.message);
    }
  }

  return (
    <main className="page-card">
      <h2>Candidate Registration</h2>
      <p className="muted">Create a personal account to view open jobs, join interviews, and submit answers.</p>
      <form onSubmit={submit} className="form-grid">
        <Field label="Full Name" value={fullName} onChange={setFullName} />
        <Field label="Email Address" value={email} onChange={setEmail} />
        <Field label="Phone Number" value={phone} onChange={setPhone} />
        <PasswordField label="Password" value={password} onChange={setPassword} />
        <button type="submit" className="action-btn">Register and Login</button>
      </form>
      <Message error={error} />
    </main>
  );
}

function CompanyDashboard({ shared, setPage }) {
  return (
    <main className="page-card wide company-dashboard-page">
      <section className="dashboard-hero">
        <div>
          <span className="eyebrow">Company Workspace</span>
          <h2>Company Area</h2>
          <p>
            Create a new job or review candidate interviews and scores from one clean workspace.
          </p>
        </div>
      </section>

      <section className="dashboard-workflow-grid company-dashboard-actions-only">
        <button className="dashboard-flow-card primary-flow-card" onClick={() => setPage("startInterview")}>
          <span className="flow-number">01</span>
          <div>
            <strong>Create New Job</strong>
            <p>Define the role, add requirements, set the interview deadline, and approve AI-matched questions.</p>
          </div>
        </button>

        <button className="dashboard-flow-card primary-flow-card" onClick={() => setPage("score")}>
          <span className="flow-number">02</span>
          <div>
            <strong>Candidate Review</strong>
            <p>View open interviews and completed interviews ranked by candidate fit score.</p>
          </div>
        </button>
      </section>
    </main>
  );
}

function CandidateDashboard({ shared, setPage }) {
  return (
    <main className="page-card wide candidate-dashboard-page">
      <section className="candidate-hero">
        <div>
          <span className="eyebrow">Candidate Portal</span>
          <h2>Candidate Area</h2>
          <p>
            Browse open jobs and start a focused AI interview for the role that matches you.
          </p>
        </div>

        <button type="button" className="action-btn dashboard-main-action" onClick={() => setPage("openJobs")}>
          Browse Open Jobs
        </button>
      </section>
    </main>
  );
}


function StartInterviewPage({ session, shared, setShared, setPage }) {
  // הגדרת משתני עזר לחישוב טווחי התאריכים
  const todayDate = getTodayDateInputValue();
  const maxDate = getDateInputValue(new Date(new Date().setDate(new Date().getDate() + 30)));

  const [jobTitleChoice, setJobTitleChoice] = useState(JOB_TITLE_OPTIONS.includes(shared.jobTitle) ? shared.jobTitle : "Backend Developer");
  const [customJobTitle, setCustomJobTitle] = useState(JOB_TITLE_OPTIONS.includes(shared.jobTitle) ? "" : shared.jobTitle || "");
  const [jobDescription, setJobDescription] = useState(shared.jobDescription || "Build APIs and integrations. Work with backend services, databases, and external systems.");
  const [interviewDeadline, setInterviewDeadline] = useState(shared.interviewDeadline || todayDate);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const jobTitle = jobTitleChoice === "Other" ? customJobTitle.trim() : jobTitleChoice;

  async function submit(event) {
    event.preventDefault();
    setError("");
    setSuccess("");

    if (!jobTitle) {
      setError("Choose a job title or enter a custom job title.");
      return;
    }

    if (countSentences(jobDescription) < 3) {
      setError("Please provide more details about this job.");
      return;
    }

    if (interviewDeadline < todayDate) {
      setError("The interview deadline cannot be in the past.");
      return;
    }

    if (interviewDeadline > maxDate) {
      setError("The interview deadline cannot be more than 30 days from today.");
      return;
    }

    try {
      const data = await apiRequest("/api/company/open-jobs", {
        method: "POST",
        headers: authHeaders(session.token),
        body: JSON.stringify({
          jobTitle,
          jobDescription,
          interviewDeadline,
        }),
      });

      setShared({
        ...shared,
        jobId: data.jobId,
        jobTitle: data.jobTitle || jobTitle,
        jobDescription: data.jobDescription || jobDescription,
        interviewDeadline,
        selectedQuestions: [],
        customQuestions: [],
      });

      setSuccess("The job was created successfully. We are now matching interview questions for you.");
      setPage("suggestQuestions");
    } catch (err) {
      setError(err.message);
    }
  }

  return (
    <main className="page-card wide create-job-page">
      <h2>Create Open Job</h2>
      <p className="muted">The company defines the role, requirements, and interview deadline. Candidates will later be able to join the open job.</p>

      <section className="pricing-notice-card">
        <div>
          <span className="eyebrow">Pricing Notice</span>
          <h3>Publishing a job may create a billing charge</h3>
          <p>
            Your company billing details are connected to this account. Before creating a job, confirm the role details and interview deadline. The final charge should be handled by the backend/payment provider according to your pricing policy.
          </p>
        </div>
        <strong>Job creation billing</strong>
      </section>

      <form onSubmit={submit} className="form-grid">
        <SelectField label="Job Title / Role" value={jobTitleChoice} onChange={setJobTitleChoice}>
          {JOB_TITLE_OPTIONS.map((title) => (
            <option key={title} value={title}>{title === "Other" ? "Other" : title}</option>
          ))}
        </SelectField>

        {jobTitleChoice === "Other" && (
          <Field label="Custom Job Title" value={customJobTitle} onChange={setCustomJobTitle} />
        )}

        <section className="deadline-card">
          <div>
            <span className="deadline-label">Interview Deadline</span>
            <strong>Active until</strong>
            <p>Choose today or any date up to 30 days from today.</p>
          </div>

          <Field
            label="Deadline Date"
            type="date"
            value={interviewDeadline}
            onChange={(val) => setInterviewDeadline(val)}
            min={todayDate}
            max={maxDate}
          />
        </section>

        <VoiceTextArea
          label="Job Description and Requirements"
          value={jobDescription}
          onChange={setJobDescription}
          token={session.token}
          placeholder="Type or record: what the role includes, which skills are required, and what experience is important for the job."
        />
        <button type="submit" className="action-btn create-job-submit-btn">Create Job and Approve Questions</button>
      </form>
      <Message error={error} success={success} />
    </main>
  );
}

function SuggestQuestionsPage({ session, shared, setShared }) {
  const [count, setCount] = useState(String(shared.questionCount || 5));
  const [questions, setQuestions] = useState(shared.suggestedQuestions || []);
  const [customQuestion, setCustomQuestion] = useState("");
  const [customQuestions, setCustomQuestions] = useState(shared.customQuestions || []);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [questionsLoaded, setQuestionsLoaded] = useState((shared.suggestedQuestions || []).length > 0);
  const [isLoadingQuestions, setIsLoadingQuestions] = useState(false);
  const [isSavingQuestions, setIsSavingQuestions] = useState(false);
  const [questionsSaved, setQuestionsSaved] = useState(false);

  async function loadQuestions(event) {
    event.preventDefault();

    if (isLoadingQuestions) return;

    setError("");
    setSuccess("");
    setQuestionsSaved(false);

    if (!shared.jobId) {
      setError("First create an open job on the Create Job page.");
      return;
    }

    const requestedCount = Number(count);

    if (Number.isNaN(requestedCount) || requestedCount < 5 || requestedCount > 20) {
      setError("The number of questions must be between 5 and 20.");
      return;
    }

    setIsLoadingQuestions(true);

    try {
      const fetchCount = Math.min(20, requestedCount * 2);

      const data = await apiRequest(`/api/jobs/${shared.jobId}/suggest-questions?count=${fetchCount}`, {
        method: "GET",
        headers: { Authorization: `Bearer ${session.token}` },
      });

      const list = uniqueQuestionsByText(data.questions || []).slice(0, requestedCount);

      setQuestions(list);
      setQuestionsLoaded(true);
      setShared({
        ...shared,
        suggestedQuestions: list,
        selectedQuestions: list,
        questionCount: requestedCount,
      });
      setSuccess("The questions were displayed. You can add company questions before saving.");
    } catch (err) {
      setError(err.message);
    } finally {
      setIsLoadingQuestions(false);
    }
  }

  async function addSelectedQuestions() {
    if (isSavingQuestions) return;

    setError("");
    setSuccess("");

    if (questionsSaved) {
      setSuccess("The questions have already been saved for this job.");
      return;
    }

    if (!shared.jobId) {
      setError("First create an open job.");
      return;
    }

    if (!questionsLoaded) {
      setError("First click Show Matching Questions.");
      return;
    }

    const uniqueBankQuestions = uniqueQuestionsByText(questions);
    const bankQuestionTexts = new Set(uniqueBankQuestions.map((question) => normalizeQuestionText(getQuestionText(question))));
    const uniqueCustomQuestions = uniqueQuestionsByText(customQuestions).filter(
      (question) => !bankQuestionTexts.has(normalizeQuestionText(question))
    );

    if (uniqueBankQuestions.length === 0 && uniqueCustomQuestions.length === 0) {
      setError("You must display questions or add a company question.");
      return;
    }

    setIsSavingQuestions(true);

    try {
      for (let index = 0; index < uniqueBankQuestions.length; index += 1) {
        await apiRequest(`/api/jobs/${shared.jobId}/questions/from-bank`, {
          method: "POST",
          headers: authHeaders(session.token),
          body: JSON.stringify({
            questionBankItemId: uniqueBankQuestions[index].questionBankItemId,
            orderIndex: index + 1,
          }),
        });
      }

      for (let index = 0; index < uniqueCustomQuestions.length; index += 1) {
        await apiRequest(`/api/jobs/${shared.jobId}/questions`, {
          method: "POST",
          headers: authHeaders(session.token),
          body: JSON.stringify({
            questionText: uniqueCustomQuestions[index],
            orderIndex: uniqueBankQuestions.length + index + 1,
            isCompanyCustomQuestion: true,
          }),
        });
      }

      setQuestions(uniqueBankQuestions);
      setCustomQuestions(uniqueCustomQuestions);
      setShared({
        ...shared,
        selectedQuestions: uniqueBankQuestions,
        customQuestions: uniqueCustomQuestions,
      });

      setQuestionsSaved(true);
      setSuccess(`Saved ${uniqueBankQuestions.length + uniqueCustomQuestions.length} questions for the job without duplicates.`);
    } catch (err) {
      setError(err.message);
    } finally {
      setIsSavingQuestions(false);
    }
  }

  function addCustomQuestionLocally() {
    setError("");
    const trimmed = customQuestion.trim();

    if (!questionsLoaded) {
      setError("You can add a question only after displaying the matching questions.");
      return;
    }

    if (!trimmed || countSentences(trimmed) < 1 || trimmed.split(/\s+/).filter(Boolean).length < 5) {
      setError("The question is too short.");
      return;
    }

    const newQuestionKey = normalizeQuestionText(trimmed);
    const existingQuestionKeys = [
      ...questions.map((question) => normalizeQuestionText(getQuestionText(question))),
      ...customQuestions.map((question) => normalizeQuestionText(question)),
    ];

    if (existingQuestionKeys.includes(newQuestionKey)) {
      setError("This question already exists in the list.");
      return;
    }

    setCustomQuestions((current) => [...current, trimmed]);
    setCustomQuestion("");
  }

  function removeCustomQuestion(indexToRemove) {
    setCustomQuestions((current) => current.filter((_, index) => index !== indexToRemove));
  }

  return (
    <main className="page-card wide">
      <h2>Interview Questions Approval</h2>
      <p className="muted">The system suggests questions according to the job requirements. After displaying the questions, the company can also add its own questions.</p>

      <section className="summary friendly-summary">
        <h3>{shared.jobTitle || "No job selected"}</h3>
        <p>{shared.jobDescription || "Create an open job before selecting questions."}</p>
      </section>

      <form onSubmit={loadQuestions} className="form-grid compact-form">
        <Field label="Desired Number of Questions" value={count} onChange={setCount} type="number" min="5" max="20" />
        <button type="submit" className="action-btn" disabled={isLoadingQuestions}>
          {isLoadingQuestions ? "Matching questions..." : "Show Matching Questions"}
        </button>
      </form>

      {isLoadingQuestions && (
        <div className="model-loading-card">
          <div className="loader-ring" aria-hidden="true"></div>
          <div>
            <strong>Matching questions to the job</strong>
            <p>The system is reading the job description, comparing it with the question bank, and selecting the best questions...</p>
          </div>
        </div>
      )}

      {questionsLoaded && questions.length > 0 && (
        <section className="questions-list">
          <h3>Recommended Interview Questions</h3>
          {questions.map((question, index) => (
            <article key={question.questionBankItemId} className="question-card clean-question-card">
              <div>
                <strong>Question {index + 1}</strong>
                <p>{question.questionText}</p>
              </div>
            </article>
          ))}
        </section>
      )}

      {questionsLoaded && (
        <section className="custom-question-box">
          <h3>Add a Question</h3>
          <div className="inline-action">
            <VoiceTextArea
              label="Question Text"
              value={customQuestion}
              onChange={setCustomQuestion}
              token={session.token}
              rows={3}
              placeholder="Type or record an additional interview question."
            />
            <button type="button" className="action-btn" onClick={addCustomQuestionLocally}>Add Question to List</button>
          </div>

          {customQuestions.length > 0 && (
            <div className="custom-question-list">
              {customQuestions.map((question, index) => (
                <div className="custom-question-item" key={`${question}-${index}`}>
                  <span>{question}</span>
                  <button type="button" className="small-link-btn" onClick={() => removeCustomQuestion(index)}>Remove</button>
                </div>
              ))}
            </div>
          )}
        </section>
      )}

      {questionsLoaded && (questions.length > 0 || customQuestions.length > 0) && (
        <>
          <button
            type="button"
            className="action-btn save-questions-btn"
            onClick={addSelectedQuestions}
            disabled={isSavingQuestions || questionsSaved}
          >
            {isSavingQuestions ? "Saving questions..." : questionsSaved ? "Questions Saved" : "Save Questions to Job"}
          </button>

          {isSavingQuestions && (
            <div className="model-loading-card">
              <div className="loader-ring" aria-hidden="true"></div>
              <div>
                <strong>Saving questions to the job</strong>
                <p>The system is saving the selected questions and preparing the interview for candidates...</p>
              </div>
            </div>
          )}
        </>
      )}

      <Message error={error} success={success} />
    </main>
  );
}

function OpenJobsPage({ session, shared, setShared, setPage }) {
  const [jobs, setJobs] = useState([]);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [jobMessages, setJobMessages] = useState({});
  const [isLoadingJobs, setIsLoadingJobs] = useState(false);

  async function loadJobs() {
    if (isLoadingJobs) return;

    setError("");
    setSuccess("");
    setJobMessages({});
    setIsLoadingJobs(true);

    try {
      const data = await apiRequest("/api/jobs/open", {
        method: "GET",
        headers: { Authorization: `Bearer ${session.token}` },
      });

      setJobs(filterActiveJobs(data.jobs || []));
    } catch (err) {
      setError(err.message);
    } finally {
      setIsLoadingJobs(false);
    }
  }

  useEffect(() => {
    loadJobs();
  }, []);

  async function joinJob(job) {
    setError("");
    setSuccess("");

    setJobMessages((current) => ({
      ...current,
      [job.jobId]: "",
    }));

    if (isDeadlineExpired(getJobDeadlineValue(job))) {
      setJobMessages((current) => ({
        ...current,
        [job.jobId]: "This interview deadline has passed and the job is no longer open.",
      }));
      setJobs((currentJobs) => filterActiveJobs(currentJobs));
      return;
    }

    try {
      const data = await apiRequest(`/api/jobs/${job.jobId}/join`, {
        method: "POST",
        headers: authHeaders(session.token),
        body: JSON.stringify({ preferredQuestionCount: 5 }),
      });

      if (data.alreadyCompleted || data.alreadyJoined) {
        setJobMessages((current) => ({
          ...current,
          [job.jobId]: "You have already started this interview, so you cannot start it again.",
        }));
        return;
      }

      const questionsData = await apiRequest(`/api/interviews/${data.interviewId}/questions`, {
        method: "GET",
        headers: { Authorization: `Bearer ${session.token}` },
      });

      const interviewQuestions = questionsData.questions || [];

      setShared({
        ...shared,
        jobId: data.jobId,
        jobTitle: job.jobTitle,
        jobDescription: job.jobDescription,
        interviewId: data.interviewId,
        candidateId: data.candidateId,
        interviewQuestions,
        currentQuestionIndex: 0,
        attemptsByQuestion: {},
        parentAnswerByQuestion: {},
      });

      setSuccess("The interview started successfully.");
      setPage("answer");
    } catch (err) {
      setJobMessages((current) => ({
        ...current,
        [job.jobId]: err.message || "Could not start this interview.",
      }));
    }
  }

  return (
    <main className="page-card wide open-jobs-page">
      <section className="open-jobs-header">
        <div>
          <span className="eyebrow">Open Interviews</span>
          <h2>Open Jobs</h2>
          <p className="muted">Choose a suitable role and start a structured AI interview.</p>
        </div>
      </section>

      {isLoadingJobs && (
        <div className="answer-loading-card">
          <div className="loader-ring" aria-hidden="true"></div>
          <div>
            <strong>Loading open jobs</strong>
            <p>Expired jobs are filtered out automatically.</p>
          </div>
        </div>
      )}

      {jobs.length > 0 ? (
        <section className="jobs-grid refined-jobs-grid">
          {jobs.map((job) => (
            <article key={job.jobId} className="job-card refined-job-card">
              <div>
                <span className="job-company-name">{job.companyName || "MindMatch Company"}</span>
                <h3>{job.jobTitle}</h3>
                <p>{job.jobDescription}</p>
              </div>

              {getJobDeadlineValue(job) && (
                <span className="job-deadline-badge">
                  Active until {formatDisplayDate(getJobDeadlineValue(job))}
                </span>
              )}

              {jobMessages[job.jobId] && (
                <div className="job-inline-warning">
                  <strong>Interview unavailable</strong>
                  <p>{jobMessages[job.jobId]}</p>
                </div>
              )}

              <button type="button" className="action-btn job-start-btn" onClick={() => joinJob(job)}>
                Start Interview
              </button>
            </article>
          ))}
        </section>
      ) : !isLoadingJobs ? (
        <EmptyState title="No open jobs available" text="There are currently no active jobs. Jobs whose interview deadline has passed are removed from this list." />
      ) : null}

      <Message error={error} success={success} />
    </main>
  );
}


function CandidateAnswerPage({ session, shared, setShared }) {
  const [answerText, setAnswerText] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [guidanceMessage, setGuidanceMessage] = useState("");
  const [isSubmittingAnswer, setIsSubmittingAnswer] = useState(false);

  const questions = shared.interviewQuestions || [];
  const currentIndex = shared.currentQuestionIndex || 0;
  const currentQuestion = questions[currentIndex];
  const questionId = currentQuestion?.questionId || currentQuestion?.id || currentQuestion?.runtimeQuestionId || "";
  const questionText = currentQuestion?.questionText || currentQuestion?.text || "";

  const attemptsByQuestion = shared.attemptsByQuestion || {};
  const parentAnswerByQuestion = shared.parentAnswerByQuestion || {};
  const currentAttempt = attemptsByQuestion[questionId] || 1;
  const currentParentAnswerId = parentAnswerByQuestion[questionId] || null;

  async function submit(event) {
    event.preventDefault();
  
    if (isSubmittingAnswer) return;
  
    setError("");
    setSuccess("");
    setGuidanceMessage("");
  
    if (!shared.interviewId || !questionId || !questionText) {
      setError("No active question was found. Go to Open Jobs and start an interview first.");
      return;
    }
  
    if (!answerText.trim()) {
      setError("Please enter an answer before submitting.");
      return;
    }
  
    setIsSubmittingAnswer(true);
  
    try {
      const data = await apiRequest(`/api/interviews/${shared.interviewId}/answers`, {
        method: "POST",
        headers: authHeaders(session.token),
        body: JSON.stringify({
          questionId,
          questionText,
          answerText,
          answerOrder: currentIndex + 1,
          attemptNumber: currentAttempt,
          parentAnswerId: currentParentAnswerId,
          maxAttemptsPerQuestion: 3,
        }),
      });
  
      const isRelevant = String(data.relevanceStatus || "").toLowerCase() === "relevant";
      const shouldRetrySameQuestion = !isRelevant && currentAttempt < 3;
      const nextIndex = currentIndex + 1;
  
      setAnswerText("");
  
      if (shouldRetrySameQuestion) {
        const message =
          data.guidanceMessage ||
          "The answer did not address the question. Try again with a more focused answer.";
  
        setGuidanceMessage(message);
  
        setShared({
          ...shared,
          questionId,
          questionText,
          attemptsByQuestion: {
            ...attemptsByQuestion,
            [questionId]: currentAttempt + 1,
          },
          parentAnswerByQuestion: {
            ...parentAnswerByQuestion,
            [questionId]: data.answerId,
          },
        });
  
        return;
      }
  
      setShared({
        ...shared,
        questionId,
        questionText,
        currentQuestionIndex: nextIndex,
        attemptsByQuestion: {
          ...attemptsByQuestion,
          [questionId]: 1,
        },
        parentAnswerByQuestion: {
          ...parentAnswerByQuestion,
          [questionId]: null,
        },
      });
  
      setSuccess(
        nextIndex >= questions.length
          ? "The answer was saved. You completed all interview questions."
          : "The answer was saved. You can continue to the next question."
      );
    } catch (err) {
      setError(err.message);
    } finally {
      setIsSubmittingAnswer(false);
    }
  }
  return (
    <main className={guidanceMessage ? "page-card wide candidate-answer-page retry-mode" : "page-card wide candidate-answer-page"}>
      <h2>Answer Interview Questions</h2>
      <p className="muted">Answer clearly and focus on the question.</p>

      {!currentQuestion ? (
        <EmptyState title="No active question" text="Go to the Open Jobs page, choose a job, and start an interview." />
      ) : (
        <form onSubmit={submit} className="form-grid answer-form-grid">
          {!guidanceMessage && (
            <section className="interview-question-panel">
              <span>Question {currentIndex + 1} of {questions.length}</span>
              <h3>{questionText}</h3>
            </section>
          )}

          {guidanceMessage && (
            <div className="guidance-box retry-guidance-card">
              <span>Try again</span>
              <strong>Your answer needs to match the question more clearly</strong>
              <p>{guidanceMessage}</p>
            </div>
          )}

          <VoiceTextArea
            label={guidanceMessage ? "Rewrite Your Answer" : "Your Answer"}
            value={answerText}
            onChange={setAnswerText}
            token={session.token}
            rows={5}
            placeholder={guidanceMessage ? "Write a more focused answer based on the feedback above." : "You can type or record an answer."}
          />

          {isSubmittingAnswer && (
            <div className="answer-loading-card">
              <div className="loader-ring" aria-hidden="true"></div>
              <div>
                <strong>The system is checking your answer</strong>
                <p>Please wait while the AI verifies relevance and diagnostic evidence.</p>
              </div>
            </div>
          )}

          <div className="answer-actions">
            <button type="submit" className="action-btn" disabled={isSubmittingAnswer}>
              {isSubmittingAnswer ? "Checking..." : guidanceMessage ? "Submit Improved Answer" : "Submit Answer"}
            </button>
          </div>
        </form>
      )}

      <Message error={error} success={success} />
    </main>
  );
}
function ScorePage({ session, shared }) {
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [score, setScore] = useState(null);

  const [savedScores, setSavedScores] = useState([]);
  const [openJobs, setOpenJobs] = useState([]);
  const [activeScoreTab, setActiveScoreTab] = useState("openJobs");
  const [isLoadingSavedScores, setIsLoadingSavedScores] = useState(false);
  const [isLoadingOpenJobs, setIsLoadingOpenJobs] = useState(false);
  const [isCalculatingScore, setIsCalculatingScore] = useState(false);

  const [selectedJobKey, setSelectedJobKey] = useState("");
  const [selectedCandidateKey, setSelectedCandidateKey] = useState("");
  const [selectedSavedScore, setSelectedSavedScore] = useState(null);
  const [selectedOpenJobKey, setSelectedOpenJobKey] = useState("");
  const [selectedOpenJob, setSelectedOpenJob] = useState(null);
  const [openJobRanking, setOpenJobRanking] = useState([]);
  const [isLoadingOpenJobRanking, setIsLoadingOpenJobRanking] = useState(false);

  async function loadSavedScores() {
    setError("");
    setSuccess("");
    setScore(null);
    setSelectedSavedScore(null);
    setSelectedJobKey("");
    setSelectedCandidateKey("");
    setIsLoadingSavedScores(true);

    try {
      const data = await apiRequest("/api/company/scores", {
        method: "GET",
        headers: { Authorization: `Bearer ${session.token}` },
      });

      const closedScores = [...(data.scores || [])]
        .filter(isClosedScoreItem)
        .sort((a, b) => {
          const titleDiff = String(a.jobTitle || "").localeCompare(String(b.jobTitle || ""));
          if (titleDiff !== 0) return titleDiff;

          const scoreDiff = Number(b.finalScore || 0) - Number(a.finalScore || 0);
          if (scoreDiff !== 0) return scoreDiff;

          return new Date(b.createdAtUtc || 0) - new Date(a.createdAtUtc || 0);
        });

      setSavedScores(closedScores);
    } catch (err) {
      setError(err.message);
    } finally {
      setIsLoadingSavedScores(false);
    }
  }

  async function loadOpenJobs() {
    setIsLoadingOpenJobs(true);
    setError("");

    try {
      const data = await apiRequest("/api/company/open-jobs", {
        method: "GET",
        headers: { Authorization: `Bearer ${session.token}` },
      });

      const activeJobs = filterActiveJobs(data.jobs || []).sort((a, b) =>
        String(a.jobTitle || a.title || "Untitled Job").localeCompare(
          String(b.jobTitle || b.title || "Untitled Job")
        )
      );

      setOpenJobs(activeJobs);
    } catch (err) {
      setError(err.message);
    } finally {
      setIsLoadingOpenJobs(false);
    }
  }

  useEffect(() => {
    loadSavedScores();
    loadOpenJobs();
  }, []);

  async function finishInterview() {
    if (isCalculatingScore) return;

    setError("");
    setSuccess("");
    setScore(null);
    setSelectedSavedScore(null);

    if (!shared.interviewId) {
      setError("No active interview was selected. Closed jobs and completed interview scores are loaded automatically below.");
      return;
    }

    setIsCalculatingScore(true);

    try {
      const data = await apiRequest(`/api/interviews/${shared.interviewId}/finish`, {
        method: "POST",
        headers: { Authorization: `Bearer ${session.token}` },
      });

      setScore(data);
      setSuccess("The fit score was calculated successfully.");
      setActiveScoreTab("closedJobs");
      await loadSavedScores();
    } catch (err) {
      setError(err.message);
    } finally {
      setIsCalculatingScore(false);
    }
  }

  const closedJobGroups = useMemo(() => {
    const map = new Map();

    for (const item of savedScores) {
      const jobKey = item.jobId || item.jobTitle || "unknown-job";

      if (!map.has(jobKey)) {
        map.set(jobKey, {
          jobKey,
          jobId: item.jobId || "",
          jobTitle: item.jobTitle || "Untitled Job",
          scores: [],
        });
      }

      map.get(jobKey).scores.push(item);
    }

    return Array.from(map.values())
      .map((job) => {
        const sortedScores = [...job.scores].sort((a, b) => {
          const scoreDiff = Number(b.finalScore || 0) - Number(a.finalScore || 0);
          if (scoreDiff !== 0) return scoreDiff;

          return new Date(b.createdAtUtc || 0) - new Date(a.createdAtUtc || 0);
        });

        return {
          ...job,
          scores: sortedScores,
          bestScore: sortedScores[0]?.finalScore ?? null,
        };
      })
      .sort((a, b) => a.jobTitle.localeCompare(b.jobTitle));
  }, [savedScores]);

  const selectedJob = closedJobGroups.find((job) => job.jobKey === selectedJobKey) || null;

  function selectJob(job) {
    setSelectedJobKey(job.jobKey);
    setSelectedCandidateKey("");
    setSelectedSavedScore(null);
    setScore(null);
    setError("");
    setSuccess("");
  }

  function openSavedScore(savedScore) {
    const candidateKey =
      savedScore.candidateId ||
      savedScore.candidateName ||
      savedScore.interviewId ||
      "unknown-candidate";

    setSelectedCandidateKey(candidateKey);
    setSelectedSavedScore(savedScore);
    setError("");
    setSuccess("");

    setScore({
      finalScore: savedScore.finalScore,
      summary: savedScore.summary,
      modelVersion: savedScore.modelVersion,
      rawJson: savedScore.rawJson,
    });
  }

  async function openOpenJob(job) {
    const jobKey = job.jobId || job.id || job.jobTitle || job.title;

    setSelectedOpenJobKey(jobKey);
    setSelectedOpenJob(job);
    setOpenJobRanking([]);
    setError("");
    setSuccess("");
    setIsLoadingOpenJobRanking(true);

    try {
      const data = await apiRequest(`/api/company/jobs/${job.jobId || job.id}/ranking`, {
        method: "GET",
        headers: { Authorization: `Bearer ${session.token}` },
      });

      const ranking = [...(data.ranking || [])].sort((a, b) => {
        const scoreDiff = Number(b.finalScore || 0) - Number(a.finalScore || 0);
        if (scoreDiff !== 0) return scoreDiff;
        return new Date(b.createdAtUtc || 0) - new Date(a.createdAtUtc || 0);
      });

      setOpenJobRanking(ranking);
    } catch (err) {
      setError(err.message);
    } finally {
      setIsLoadingOpenJobRanking(false);
    }
  }

  const openJobsCount = openJobs.length;
  const closedJobsCount = closedJobGroups.length;

  return (
    <main className="page-card wide scores-page">
      <div className="page-title-row">
        <div>
          <span className="eyebrow">Company Analytics</span>
          <h2>Candidate Scores</h2>
          <p className="muted">
            View interviews from open jobs, and review closed jobs with candidates ranked by final fit score.
          </p>
        </div>
      </div>

      <section className="score-tabs">
        <button
          type="button"
          className={activeScoreTab === "openJobs" ? "score-tab active" : "score-tab"}
          onClick={() => setActiveScoreTab("openJobs")}
        >
          Interviews from Open Jobs
          <span>{openJobsCount}</span>
        </button>

        <button
          type="button"
          className={activeScoreTab === "closedJobs" ? "score-tab active" : "score-tab"}
          onClick={() => setActiveScoreTab("closedJobs")}
        >
          Closed Jobs
          <span>{closedJobsCount}</span>
        </button>
      </section>

      {(isLoadingSavedScores || isLoadingOpenJobs) && (
        <div className="answer-loading-card score-loading-card">
          <div className="loader-ring" aria-hidden="true"></div>
          <div>
            <strong>Loading interview data</strong>
            <p>The system is loading open jobs and completed scores for this company.</p>
          </div>
        </div>
      )}

      {activeScoreTab === "openJobs" && (
        <section className="open-interviews-panel">
          {openJobs.length > 0 && (
            <div className="open-interviews-by-job">
              {openJobs.map((job) => {
                const deadline = getJobDeadlineValue(job);
                const jobKey = job.jobId || job.id || job.jobTitle || job.title;
                const isSelected = selectedOpenJobKey === jobKey;
                const jobTitle = job.jobTitle || job.title || "Untitled Job";

                return (
                  <div key={jobKey} className="open-job-item-stack">
                    <button
                      type="button"
                      className={
                        isSelected
                          ? "open-job-interviews-card open-job-button-card selected"
                          : "open-job-interviews-card open-job-button-card"
                      }
                      onClick={() => openOpenJob(job)}
                    >
                      <div className="open-job-main">
                        <span className="job-company-name">{job.companyName || "MindMatch Company"}</span>
                        <h3>{jobTitle}</h3>
                        <p>{job.jobDescription || job.description || "No description was provided."}</p>
                      </div>

                      <div className="open-job-side">
                        <span>Open until</span>
                        <strong>{deadline ? formatDisplayDate(deadline) : "Not set"}</strong>
                        <small>Click to view interview summaries for this job.</small>
                      </div>
                    </button>

                    {isSelected && isLoadingOpenJobRanking && (
                      <div className="answer-loading-card score-loading-card inline-open-job-loader">
                        <div className="loader-ring" aria-hidden="true"></div>
                        <div>
                          <strong>Loading interview summaries</strong>
                          <p>The system is loading completed interview summaries for this job.</p>
                        </div>
                      </div>
                    )}

                    {isSelected && selectedOpenJob && !isLoadingOpenJobRanking && (
                      <section className="open-job-summaries-panel inline-open-job-summaries-panel">
                        <div className="column-heading">
                          <span>Interview Summaries</span>
                          <h3>{jobTitle}</h3>
                        </div>

                        {openJobRanking.length === 0 ? (
                          <div className="soft-empty large">
                            No completed interview summaries exist for this open job yet.
                          </div>
                        ) : (
                          <div className="open-job-summary-list">
                            {openJobRanking.map((item, index) => (
                              <article key={item.finalScoreId || item.interviewId} className="score-card open-job-summary-card">
                                <div className="score-rank-badge">#{index + 1}</div>
                                <FinalScoreView
                                  score={{
                                    finalScore: item.finalScore,
                                    summary: item.summary,
                                    modelVersion: item.modelVersion,
                                    rawJson: item.rawJson,
                                  }}
                                  jobTitle={jobTitle}
                                />
                              </article>
                            ))}
                          </div>
                        )}
                      </section>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </section>
      )}

      {activeScoreTab === "closedJobs" && (
        <>
          {savedScores.length === 0 && !isLoadingSavedScores ? (
            <EmptyState
              title="No closed jobs yet"
              text="Only jobs whose interview deadline has passed or whose status is closed will appear here. Candidates are ranked by score inside each job."
            />
          ) : (
            <section className="scores-explorer">
              <aside className="scores-column jobs-column">
                <div className="column-heading">
                  <span>Closed Jobs</span>
                  <h3>Jobs</h3>
                </div>

                {closedJobGroups.map((job) => (
                  <button
                    type="button"
                    key={job.jobKey}
                    className={selectedJobKey === job.jobKey ? "job-score-button selected" : "job-score-button"}
                    onClick={() => selectJob(job)}
                  >
                    <div>
                      <strong>{job.jobTitle}</strong>
                      <span>{job.scores.length} completed interviews</span>
                    </div>

                    <em>
                      {job.bestScore !== null ? formatScoreNumber(job.bestScore) : "-"}
                    </em>
                  </button>
                ))}
              </aside>

              <aside className="scores-column candidates-column">
                <div className="column-heading">
                  <span>Ranked by Score</span>
                  <h3>Candidates</h3>
                </div>

                {!selectedJob ? (
                  <div className="soft-empty">
                    Select a closed job to view interviews ranked by score.
                  </div>
                ) : (
                  selectedJob.scores.map((item, index) => {
                    const candidateKey =
                      item.candidateId ||
                      item.candidateName ||
                      item.interviewId ||
                      "unknown-candidate";

                    return (
                      <button
                        type="button"
                        key={item.finalScoreId || item.interviewId}
                        className={
                          selectedCandidateKey === candidateKey
                            ? "candidate-score-button selected"
                            : "candidate-score-button"
                        }
                        onClick={() => openSavedScore(item)}
                      >
                        <div>
                          <strong>#{index + 1} {item.candidateName || "Candidate"}</strong>
                          <span>{item.candidateId ? `ID: ${item.candidateId}` : `Interview: ${item.interviewId}`}</span>
                          <small>
                            {item.createdAtUtc
                              ? new Date(item.createdAtUtc).toLocaleString()
                              : "Unknown date"}
                          </small>
                        </div>

                        <em>{formatScoreNumber(item.finalScore)}</em>
                      </button>
                    );
                  })
                )}
              </aside>

              <section className="score-details-panel">
                <div className="column-heading">
                  <span>Summary</span>
                  <h3>Interview Summary</h3>
                </div>

                {!score ? (
                  <div className="soft-empty large">
                    Select a candidate to view the full score summary.
                  </div>
                ) : (
                  <FinalScoreView
                    score={score}
                    jobTitle={selectedSavedScore?.jobTitle || shared.jobTitle}
                  />
                )}
              </section>
            </section>
          )}
        </>
      )}

      <Message error={error} success={success} />
    </main>
  );
}

function FinalScoreView({ score, jobTitle }) {
  const finalScore = Number(score?.finalScore ?? score?.FinalScore ?? 0);
  const summary = score?.summary || score?.Summary || "";
  const englishSummary = buildEnglishScoreSummary(score, summary, jobTitle);
  const categories = extractCategoryScores(summary);
  const strength = extractStrength(summary);
  const weakness = extractWeakness(summary);

  return (
    <section className="score-dashboard">
      <div className="score-hero-card">
        <div>
          <span className="score-eyebrow">Final Fit Score</span>
          <h3>{jobTitle || "Active Job"}</h3>
          <p>
            The score was calculated using a weighted evidence graph, based on job features, candidate answers, answer reliability, and recurring interview patterns.
          </p>
        </div>

        <div
  className="score-circle"
  style={{
    "--score-percent": `${Math.max(0, Math.min(100, Number(score.finalScore || 0)))}%`,
  }}
>
          <strong>{formatScoreNumber(finalScore)}</strong>
          <span>{scoreLevel(finalScore)}</span>
        </div>
      </div>

      <div className="score-insights-grid">
        <article className="score-insight-card positive">
          <span>Main Strength</span>
          <strong>{strength || "No main strength detected"}</strong>
          <p>The feature where the candidate showed the strongest evidence compared with the other features.</p>
        </article>

        <article className="score-insight-card warning">
          <span>Relative Weakness</span>
          <strong>{weakness || "No main weakness detected"}</strong>
          <p>An important job feature that requires stronger evidence or further review.</p>
        </article>

        <article className="score-insight-card model">
          <span>Scoring Method</span>
          <strong>Weighted Evidence Graph</strong>
          <p>The score connects the job, questions, answers, diagnostic models, and recurring patterns.</p>
        </article>
      </div>

      {categories.length > 0 && (
        <div className="score-breakdown-panel">
          <h3>Key Feature Breakdown</h3>

          {categories.map((category) => (
            <div className="score-meter-row" key={category.name}>
              <div className="score-meter-top">
                <span>{category.name}</span>
                <strong>{formatScoreNumber(category.value)}</strong>
              </div>
              <div className="score-meter-track">
                <div
                  className="score-meter-fill"
                  style={{ width: `${Math.max(0, Math.min(100, category.value))}%` }}
                />
              </div>
            </div>
          ))}
        </div>
      )}

      <div className="score-summary-panel">
        <h3>Written Summary</h3>
        <p>{englishSummary}</p>
      </div>
    </section>
  );
}


function buildEnglishScoreSummary(score, summary = "", jobTitle = "") {
  const finalScore = formatScoreNumber(score?.finalScore ?? score?.FinalScore ?? 0);
  const strength = extractStrength(summary);
  const weakness = extractWeakness(summary);
  const categories = extractCategoryScores(summary);
  const categoryText = categories.length
    ? ` Main evaluated features: ${categories.map((item) => `${item.name}: ${formatScoreNumber(item.value)}`).join(", ")}.`
    : "";
  const strengthText = strength ? ` The strongest feature identified in the interview is ${strength}.` : "";
  const weaknessText = weakness ? ` A relative weakness was found in an important job feature: ${weakness}.` : "";

  return `The final score for the candidate${jobTitle ? ` for the ${jobTitle} position` : ""} is ${finalScore}. The score was calculated using a weighted evidence graph, based on job feature importance, candidate answers, answer reliability, and recurring patterns throughout the interview.${categoryText}${strengthText}${weaknessText}`;
}

function Protected({ session, role, children }) {
  if (!session) {
    return (
      <main className="page-card">
        <h2>Login Required</h2>
        <p className="muted">You must log in to perform this action.</p>
      </main>
    );
  }

  if (role && session.role !== role) {
    return (
      <main className="page-card">
        <h2>Access Denied</h2>
        <p className="muted">This page is intended for users of type {role}.</p>
      </main>
    );
  }

  return children;
}

export default function App() {
  const [session, setSession] = useState(loadSession);
  const [prefilledEmail, setPrefilledEmail] = useState("");
  const [page, setPage] = useState(() => {
    const activeSession = loadSession();
    if (activeSession?.role === "Company") return "companyDashboard";
    if (activeSession?.role === "Candidate") return "candidateDashboard";
    return "login";
  });
  const [shared, setShared] = useState({
    jobId: "",
    jobTitle: "",
    jobDescription: "",
    interviewId: "",
    candidateId: "",
    suggestedQuestions: [],
    selectedQuestions: [],
    customQuestions: [],
    interviewQuestions: [],
    currentQuestionIndex: 0,
    questionId: "",
    questionText: "",
    attemptsByQuestion: {},
    parentAnswerByQuestion: {},
  });

  const currentPage = useMemo(() => {
    switch (page) {
      case "registerCompany":
        return <RegisterCompanyPage setSession={setSession} setPage={setPage} setPrefilledEmail={setPrefilledEmail} />;
      case "registerCandidate":
        return <RegisterCandidatePage setSession={setSession} setPage={setPage} setPrefilledEmail={setPrefilledEmail} />;
      case "companyDashboard":
        return <Protected session={session} role="Company"><CompanyDashboard shared={shared} setPage={setPage} /></Protected>;
      case "candidateDashboard":
        return <Protected session={session} role="Candidate"><CandidateDashboard shared={shared} setPage={setPage} /></Protected>;
      case "startInterview":
        return <Protected session={session} role="Company"><StartInterviewPage session={session} shared={shared} setShared={setShared} setPage={setPage} /></Protected>;
      case "suggestQuestions":
        return <Protected session={session} role="Company"><SuggestQuestionsPage session={session} shared={shared} setShared={setShared} /></Protected>;
      case "openJobs":
        return <Protected session={session} role="Candidate"><OpenJobsPage session={session} shared={shared} setShared={setShared} setPage={setPage} /></Protected>;
      case "answer":
        return <Protected session={session} role="Candidate"><CandidateAnswerPage session={session} shared={shared} setShared={setShared} /></Protected>;
      case "score":
        return <Protected session={session} role="Company"><ScorePage session={session} shared={shared} /></Protected>;
      case "login":
      default:
        return <LoginPage setSession={setSession} setPage={setPage} prefilledEmail={prefilledEmail} />;
    }
  }, [page, session, shared, prefilledEmail]);

  return (
    <div className="app-shell">
      <Header session={session} setSession={setSession} setPage={setPage} currentPageKey={page} shared={shared} />
      {currentPage}
    </div>
  );
}
function normalizeQuestionText(text) {
  return String(text || "")
    .trim()
    .toLowerCase()
    .replace(/[“”]/g, '"')
    .replace(/[‘’]/g, "'")
    .replace(/\s+/g, " ");
}

function getQuestionText(question) {
  if (typeof question === "string") return question;
  return question?.questionText || question?.text || question?.title || "";
}

function uniqueQuestionsByText(questions) {
  const seen = new Set();

  return (questions || []).filter((question) => {
    const key = normalizeQuestionText(getQuestionText(question));

    if (!key || seen.has(key)) {
      return false;
    }

    seen.add(key);
    return true;
  });
}

function formatScoreNumber(value) {
  const number = Number(value);
  if (Number.isNaN(number)) return value || "-";
  return Number.isInteger(number) ? String(number) : number.toFixed(2);
}

function extractCategoryScores(summary = "") {
  const categories = ["PracticalAbilities", "ThinkingQuality", "Experience"];
  return categories
    .map((name) => {
      const match = summary.match(new RegExp(`${name}:\\s*([0-9]+(?:\\.[0-9]+)?)`, "i"));
      return match ? { name, value: Number(match[1]) } : null;
    })
    .filter(Boolean);
}

function extractStrength(summary = "") {
  const englishMatch = summary.match(/strongest feature(?: identified)?(?: in the interview)? is\s+([^\.]+)\./i);
  if (englishMatch?.[1]) return englishMatch[1].trim();

  const hebrewMatch = summary.match(new RegExp("\\u05d4\\u05d7\\u05d5\\u05d6\\u05e7\\u05d4 \\u05d4\\u05d1\\u05d5\\u05dc\\u05d8\\u05ea \\u05d1\\u05d9\\u05d5\\u05ea\\u05e8 \\u05e9\\u05e2\\u05dc\\u05ea\\u05d4 \\u05de\\u05d4\\u05e8\\u05d0\\u05d9\\u05d5\\u05df \\u05d4\\u05d9\\u05d0\\s+([^\\.]+)\\."));
  return hebrewMatch?.[1]?.trim() || "";
}

function extractWeakness(summary = "") {
  const englishMatch = summary.match(/relative weakness(?: was found)?(?: in an important job feature)?:\s+([^\.]+)\./i);
  if (englishMatch?.[1]) return englishMatch[1].trim();

  const hebrewMatch = summary.match(new RegExp("\\u05d7\\u05d5\\u05dc\\u05e9\\u05d4 \\u05d9\\u05d7\\u05e1\\u05d9\\u05ea \\u05d1\\u05de\\u05d0\\u05e4\\u05d9\\u05d9\\u05df \\u05d7\\u05e9\\u05d5\\u05d1 \\u05dc\\u05de\\u05e9\\u05e8\\u05d4:\\s+([^\\.]+)\\."));
  return hebrewMatch?.[1]?.trim() || "";
}

function scoreLevel(score) {
  const value = Number(score);
  if (Number.isNaN(value)) return "Calculation completed";
  if (value >= 85) return "High fit";
  if (value >= 70) return "Good partial fit";
  if (value >= 55) return "Medium fit";
  return "Low fit";
}
