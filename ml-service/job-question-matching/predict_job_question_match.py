import io
import json
import os
import sys

import torch
import torch.nn as nn
from transformers import AutoModel, AutoTokenizer


sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")


BASE_DIR = os.path.dirname(os.path.abspath(__file__))

ROBERTA_BASE_DIR = os.getenv(
    "ROBERTA_BASE_DIR",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "models", "roberta-base"))
)

WEIGHTS_PATH = os.getenv(
    "JOB_QUESTION_MATCHER_WEIGHTS",
    os.path.abspath(
        os.path.join(
            BASE_DIR,
            "..",
            "..",
            "models",
            "job-question-matcher",
            "job_question_matcher_v2.pt"
        )
    )
)

DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")
MAX_LEN = int(os.getenv("JOB_QUESTION_MATCHER_MAX_LEN", "256"))


class JobQuestionMatcher(nn.Module):
    def __init__(self, roberta_base_dir):
        super().__init__()

        self.encoder = AutoModel.from_pretrained(roberta_base_dir)
        hidden_size = self.encoder.config.hidden_size

        self.regressor = nn.Sequential(
            nn.Dropout(0.15),
            nn.Linear(hidden_size, 256),
            nn.Tanh(),
            nn.Dropout(0.10),
            nn.Linear(256, 1),
            nn.Sigmoid()
        )

    def forward(self, input_ids, attention_mask):
        outputs = self.encoder(
            input_ids=input_ids,
            attention_mask=attention_mask
        )

        cls_token = outputs.last_hidden_state[:, 0, :]
        score = self.regressor(cls_token).squeeze(-1)

        return score


def print_ranked_result(result):
    print("RANKED_QUESTIONS_JSON:" + json.dumps(result, ensure_ascii=False))


def safe_json_loads(value, default):
    if value is None:
        return default

    if isinstance(value, (dict, list)):
        return value

    if not isinstance(value, str):
        return default

    text = value.strip()

    if not text:
        return default

    try:
        return json.loads(text)
    except Exception:
        return default


def tags_to_text(tags, max_items=12):
    tags = safe_json_loads(tags, {})

    if not isinstance(tags, dict):
        return ""

    items = []

    for key, value in tags.items():
        try:
            numeric_value = float(value)
            items.append((str(key), numeric_value))
        except Exception:
            continue

    items = sorted(items, key=lambda item: item[1], reverse=True)[:max_items]

    return ", ".join([f"{key}={value:.2f}" for key, value in items])


def list_to_text(values):
    values = safe_json_loads(values, [])

    if not isinstance(values, list):
        return ""

    return ", ".join([str(value) for value in values])


def get_first(question, names, default=None):
    for name in names:
        if name in question and question[name] is not None:
            return question[name]

    return default


def normalize_question(raw_question):
    question_id = get_first(
        raw_question,
        [
            "questionBankItemId",
            "QuestionBankItemId",
            "id",
            "Id",
            "questionCode",
            "QuestionCode",
            "code",
            "Code"
        ],
        ""
    )

    question_code = get_first(
        raw_question,
        [
            "questionCode",
            "QuestionCode",
            "code",
            "Code"
        ],
        str(question_id)
    )

    question_text = get_first(
        raw_question,
        [
            "questionText",
            "QuestionText"
        ],
        ""
    )

    diagnostic_target = get_first(
        raw_question,
        [
            "questionDiagnosticTarget",
            "QuestionDiagnosticTarget",
            "diagnosticTarget",
            "DiagnosticTarget"
        ],
        ""
    )

    content_tags = get_first(
        raw_question,
        [
            "questionContentTags",
            "QuestionContentTags",
            "contentCoverageJson",
            "ContentCoverageJson",
            "contentCoverage",
            "ContentCoverage"
        ],
        {}
    )

    scoring_models = get_first(
        raw_question,
        [
            "questionRecommendedScoringModels",
            "QuestionRecommendedScoringModels",
            "recommendedScoringModelsJson",
            "RecommendedScoringModelsJson",
            "recommendedScoringModels",
            "RecommendedScoringModels"
        ],
        []
    )

    return {
        "questionBankItemId": str(question_id),
        "questionCode": str(question_code),
        "questionText": str(question_text),
        "questionDiagnosticTarget": str(diagnostic_target),
        "questionContentTags": safe_json_loads(content_tags, {}),
        "questionRecommendedScoringModels": safe_json_loads(scoring_models, [])
    }


def build_input_text(job_title, job_description, question):
    question_tags = tags_to_text(question.get("questionContentTags", {}))
    question_models = list_to_text(question.get("questionRecommendedScoringModels", []))

    parts = [
        f"Job title: {job_title}",
        f"Job description: {job_description}",
        "",
        f"Question: {question.get('questionText', '')}",
        f"Question diagnostic target: {question.get('questionDiagnosticTarget', '')}",
    ]

    if question_tags:
        parts.append(f"Question content tags: {question_tags}")

    if question_models:
        parts.append(f"Question scoring models: {question_models}")

    return "\n".join(parts)


def load_questions_json(value):
    if value.startswith("@"):
        path = value[1:]

        with open(path, "r", encoding="utf-8") as file:
            return json.load(file)

    return json.loads(value)


def load_model_and_tokenizer():
    if not os.path.exists(ROBERTA_BASE_DIR):
        raise FileNotFoundError(f"RoBERTa base directory was not found: {ROBERTA_BASE_DIR}")

    if not os.path.exists(WEIGHTS_PATH):
        raise FileNotFoundError(f"Model weights were not found: {WEIGHTS_PATH}")

    tokenizer = AutoTokenizer.from_pretrained(ROBERTA_BASE_DIR)
    model = JobQuestionMatcher(ROBERTA_BASE_DIR).to(DEVICE)

    state_dict = torch.load(
        WEIGHTS_PATH,
        map_location=DEVICE
    )

    model.load_state_dict(state_dict)
    model.eval()

    return model, tokenizer


def predict_score(model, tokenizer, job_title, job_description, question):
    text = build_input_text(job_title, job_description, question)

    encoded = tokenizer(
        text,
        truncation=True,
        padding="max_length",
        max_length=MAX_LEN,
        return_tensors="pt"
    )

    input_ids = encoded["input_ids"].to(DEVICE)
    attention_mask = encoded["attention_mask"].to(DEVICE)

    with torch.no_grad():
        score = model(input_ids, attention_mask).item()

    return float(score)


def rank_questions(job_title, job_description, raw_questions, top_k=20):
    model, tokenizer = load_model_and_tokenizer()

    ranked_questions = []

    for raw_question in raw_questions:
        question = normalize_question(raw_question)

        if not question["questionText"].strip():
            continue

        score = predict_score(
            model,
            tokenizer,
            job_title,
            job_description,
            question
        )

        ranked_questions.append({
            "questionBankItemId": question["questionBankItemId"],
            "questionCode": question["questionCode"],
            "questionText": question["questionText"],
            "diagnosticTarget": question["questionDiagnosticTarget"],
            "matchScore": round(score, 4),
            "questionContentTags": question["questionContentTags"],
            "questionRecommendedScoringModels": question["questionRecommendedScoringModels"]
        })

    ranked_questions = sorted(
        ranked_questions,
        key=lambda item: item["matchScore"],
        reverse=True
    )

    return ranked_questions[:top_k]


def main():
    if len(sys.argv) < 4:
        print_ranked_result({
            "status": "error",
            "message": "Expected arguments: jobTitle jobDescription questionsJson [topK]",
            "example": 'python predict_job_question_match.py "Backend Developer" "SQL REST API debugging" "@questions.json" 5'
        })
        sys.exit(1)

    job_title = sys.argv[1]
    job_description = sys.argv[2]
    questions_arg = sys.argv[3]

    top_k = 10

    if len(sys.argv) >= 5:
        try:
            top_k = int(sys.argv[4])
        except Exception:
            top_k = 10

    try:
        raw_questions = load_questions_json(questions_arg)

        if not isinstance(raw_questions, list):
            raise ValueError("questionsJson must be a JSON array")

        ranked_questions = rank_questions(
            job_title=job_title,
            job_description=job_description,
            raw_questions=raw_questions,
            top_k=top_k
        )

        print_ranked_result({
            "status": "success",
            "model": "job_question_matcher_roberta_base_v2",
            "robertaBaseDir": ROBERTA_BASE_DIR,
            "weightsPath": WEIGHTS_PATH,
            "jobTitle": job_title,
            "count": len(ranked_questions),
            "questions": ranked_questions
        })

    except Exception as error:
        print_ranked_result({
            "status": "error",
            "message": str(error),
            "robertaBaseDir": ROBERTA_BASE_DIR,
            "weightsPath": WEIGHTS_PATH
        })
        sys.exit(1)


if __name__ == "__main__":
    main()