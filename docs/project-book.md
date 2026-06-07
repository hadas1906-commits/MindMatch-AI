# MindMatch-AI Project Book

## Project Overview

MindMatch-AI is an AI-based system for managing and analyzing job interviews.  
The system allows companies to create open jobs, enter free-text job descriptions, receive job-specific interview questions, conduct candidate interviews, analyze candidate answers, and rank candidates by final fit score.

The project combines a React frontend, an ASP.NET Core backend, a PostgreSQL database, and Python-based AI/ML services.

## Purpose of the Project

The project was built to make the initial candidate screening process more structured, consistent, and data-driven.

Instead of relying only on manual interview evaluation, the system collects evidence from candidate answers, checks whether answers are relevant to the questions, runs diagnostic models, and produces a final candidate fit score with a written summary.

## Main System Users

### Company User

The company user can:

- Register and log in.
- Create open jobs.
- Define job descriptions and interview deadlines.
- Generate recommended interview questions.
- Add custom questions.
- Review completed candidate interviews.
- View candidate scores, summaries, strengths, weaknesses, and ranking.

### Candidate User

The candidate user can:

- Register and log in.
- Browse open jobs.
- Join an interview.
- Answer interview questions using text or voice.
- Submit answers for AI-based relevance and diagnostic analysis.

## Main System Flow

1. A company creates a job opening.
2. The company enters the job title, description, requirements, and deadline.
3. The system analyzes the job description.
4. The system suggests relevant interview questions.
5. The candidate joins the interview.
6. The candidate answers the interview questions.
7. The backend sends the answer data to Python AI services.
8. The system checks answer relevance.
9. Diagnostic models evaluate different candidate qualities.
10. The scoring engine calculates a final fit score.
11. The company views the candidate ranking and interview summary.

## Technologies Used

### Frontend

- React
- JavaScript
- HTML5
- CSS3

### Backend

- ASP.NET Core
- C#
- Entity Framework Core
- REST API
- JWT authentication
- Data protection services

### Database

- PostgreSQL
- Neon DB

### Machine Learning Service

- Python
- FastAPI
- PyTorch
- NLP models
- Custom RoBERTa-based models

### Tools

- Git
- GitHub
- Postman
- Visual Studio
- VS Code

## AI and Machine Learning Components

The system includes several AI-based components:

- Job-question matching model
- Answer relevance checking model
- Diagnostic routing model
- Personality traits model
- Thinking quality model
- Practical ability model
- Experience evaluation model
- Failure-reason analysis model
- Audio transcription service

## Backend Architecture

The backend is built with a service-layer architecture.  
The main backend modules include:

- Authentication and authorization services
- Personal data protection services
- Interview flow services
- Question suggestion services
- Python model server client
- Diagnostic model runner
- Routing service
- Scoring service
- Candidate evidence graph builder

## Scoring Engine

The final score is calculated using a weighted evidence graph.

The scoring engine considers:

- Candidate answers
- Question relevance
- Diagnostic model outputs
- Job feature priorities
- Answer reliability
- Evidence coverage
- Repeated answer patterns
- Final failed answers

The output includes both a numeric final score and a written summary for the company.

## Security

The system includes security mechanisms such as:

- JWT authentication
- Role-based access for company and candidate users
- Password hashing
- Personal data protection
- Search hashes for sensitive information
- Authorization checks on company and candidate endpoints

## Repository Structure

```text
MindMatch-AI/
├── backend/
├── frontend/
├── ml-service/
├── docs/
├── README.md
└── .gitignore
```

## Screenshots

Screenshots are available in:

```text
docs/screenshots/
```

They include:

- Login screen
- Company dashboard
- Create job screen
- Question suggestions
- Candidate dashboard
- Open jobs
- Candidate answer screen
- Score and ranking screens

## Model Weights

Large model weights are not included in the GitHub repository.

To run the full AI pipeline locally, the required model files should be placed under:

```text
ml-service/models/
```

More details are available in:

```text
ml-service/models/README.md
```

## Main Challenges

During development, the main challenges were:

- Connecting a web system to Python AI models.
- Sending interview answers from ASP.NET Core to Python services.
- Handling voice input and audio transcription.
- Building a relevance-checking flow for free-text answers.
- Combining several model outputs into one final candidate score.
- Protecting candidate and company data.
- Managing permissions so each company can view only its own interviews and scores.

## Future Improvements

Possible future improvements include:

- Improving the accuracy of the diagnostic models.
- Adding more job categories.
- Adding more advanced interview analytics.
- Improving the explanation of model decisions.
- Adding deployment automation.
- Supporting more languages.
- Improving the UI for long interview summaries.

## GitHub Repository

[MindMatch-AI GitHub Repository](https://github.com/hadas1906-commits/MindMatch-AI)
