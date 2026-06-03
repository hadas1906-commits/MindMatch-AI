from __future__ import annotations

import io
import json
import os
import sys
import traceback
from typing import Any, Dict, List

import joblib
import numpy as np
import torch
import torch.nn as nn
from fastapi import FastAPI
from pydantic import BaseModel

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")


BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MODELS_DIR = os.getenv(
    "MODELS_DIR",
    os.path.abspath(os.path.join(BASE_DIR, "models"))
)

MY_ROBERTA_CODE = os.getenv(
    "MY_ROBERTA_CODE",
    os.path.abspath(os.path.join(MODELS_DIR, "my-roberta"))
)

ROBERTA_BASE_DIR = os.getenv(
    "ROBERTA_BASE_DIR",
    os.path.abspath(os.path.join(MODELS_DIR, "roberta-base"))
)

FAILURE_REASONS_DIR = os.getenv(
    "FAILURE_REASONS_DIR",
    os.path.abspath(os.path.join(BASE_DIR, "services", "failure-reasons"))
)

for path in [MY_ROBERTA_CODE, FAILURE_REASONS_DIR]:
    if path and path not in sys.path:
        sys.path.insert(0, path)

from roberta_model import Transformer
from Tokenization import MyRobertaTokenizer

try:
    from transformers import AutoModel, AutoTokenizer
except Exception as import_error:
    AutoModel = None
    AutoTokenizer = None
    print(f"Warning: transformers import failed: {import_error}")

try:
    from faster_whisper import WhisperModel
except Exception as whisper_import_error:
    WhisperModel = None
    print(f"Warning: faster_whisper import failed: {whisper_import_error}")

try:
    from diagnosis_model import MisunderstandingDiagnoser
except Exception as diagnosis_import_error:
    MisunderstandingDiagnoser = None
    print(f"Warning: diagnosis model import failed: {diagnosis_import_error}")


app = FastAPI(title="MindMatch AI Warm Model Server")

DEVICE_AUTO = torch.device("cuda" if torch.cuda.is_available() else "cpu")
DEVICE_CPU = torch.device("cpu")

CACHE: Dict[str, Any] = {}
LOAD_ERRORS: Dict[str, str] = {}


class QARequest(BaseModel):
    question: str
    answer: str


class DiagnosticRequest(BaseModel):
    diagnosticType: str
    question: str
    answer: str


class JobQuestionMatchRequest(BaseModel):
    jobTitle: str
    jobDescription: str
    questions: List[dict]
    topK: int = 10


class TranscribeFileRequest(BaseModel):
    audioPath: str


def remember_error(name: str, error: Exception) -> None:
    LOAD_ERRORS[name] = str(error)
    print(f"[{name}] error: {error}")
    traceback.print_exc()


def require_not_empty(question: str, answer: str) -> tuple[str, str]:
    clean_question = (question or "").strip()
    clean_answer = (answer or "").strip()

    if not clean_question or not clean_answer:
        raise ValueError("Question and answer are required.")

    return clean_question, clean_answer


def get_model_path(env_name: str, *relative_parts: str) -> str:
    return os.getenv(
        env_name,
        os.path.abspath(os.path.join(MODELS_DIR, *relative_parts))
    )


def get_my_roberta_tokenizer() -> MyRobertaTokenizer:
    vocab_path = os.path.join(MY_ROBERTA_CODE, "vocab.json")
    merges_path = os.path.join(MY_ROBERTA_CODE, "merges.txt")

    if not os.path.exists(vocab_path):
        raise FileNotFoundError(f"vocab.json was not found: {vocab_path}")

    if not os.path.exists(merges_path):
        raise FileNotFoundError(f"merges.txt was not found: {merges_path}")

    return MyRobertaTokenizer(vocab_path, merges_path)


class RelevanceClassifier(nn.Module):
    def __init__(self, base_transformer):
        super().__init__()

        self.transformer = base_transformer

        self.classifier = nn.Sequential(
            nn.Linear(768, 512),
            nn.ReLU(),
            nn.Dropout(0.2),
            nn.Linear(512, 1),
            nn.Sigmoid()
        )

    def forward(self, input_ids):
        hidden_states = self.transformer(input_ids)

        mask = (input_ids != 1).unsqueeze(-1).expand(hidden_states.size()).float()
        sum_embeddings = torch.sum(hidden_states * mask, dim=1)
        sum_mask = torch.clamp(mask.sum(1), min=1e-9)
        mean_pooling = sum_embeddings / sum_mask

        return self.classifier(mean_pooling)


def load_relevance():
    if "relevance" in CACHE:
        return CACHE["relevance"]

    tokenizer = get_my_roberta_tokenizer()

    base = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12,
        d_ff=3072
    )

    model = RelevanceClassifier(base).to(DEVICE_AUTO)

    weights_path = get_model_path(
        "RELEVANCE_MODEL_PATH",
        "relevance-check",
        "relevance_model_v5_logic.pt"
    )

    if not os.path.exists(weights_path):
        raise FileNotFoundError(f"Relevance model weights were not found: {weights_path}")

    model.load_state_dict(torch.load(weights_path, map_location=DEVICE_AUTO))
    model.eval()

    CACHE["relevance"] = {
        "model": model,
        "tokenizer": tokenizer,
        "weights_path": weights_path
    }

    return CACHE["relevance"]


def predict_relevance(question: str, answer: str) -> float:
    state = load_relevance()
    model = state["model"]
    tokenizer = state["tokenizer"]

    text = f"<s>{question}</s></s>{answer}</s>"
    token_ids = tokenizer.encode(text)

    max_len = 256

    if len(token_ids) > max_len:
        token_ids = token_ids[:max_len - 1] + [2]
    else:
        token_ids = token_ids + [1] * (max_len - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE_AUTO)

    with torch.no_grad():
        score = model(input_ids).item()

    return float(score)


BIGFIVE_TRAITS = [
    "Openness",
    "Conscientiousness",
    "Extraversion",
    "Agreeableness",
    "Neuroticism"
]


class OceanRegressor(nn.Module):
    def __init__(self, base_transformer):
        super().__init__()

        self.transformer = base_transformer

        self.heads = nn.ModuleList([
            nn.Sequential(
                nn.Linear(768, 512),
                nn.Tanh(),
                nn.Dropout(0.1),
                nn.Linear(512, 1),
                nn.Sigmoid()
            )
            for _ in range(5)
        ])

    def forward(self, input_ids):
        hidden_states = self.transformer(input_ids)

        mask = (input_ids != 1).unsqueeze(-1).expand(hidden_states.size()).float()
        pooled = torch.sum(hidden_states * mask, dim=1) / torch.clamp(mask.sum(1), min=1e-9)

        return torch.cat([head(pooled) for head in self.heads], dim=1)


def load_bigfive():
    if "bigfive" in CACHE:
        return CACHE["bigfive"]

    tokenizer = get_my_roberta_tokenizer()

    base = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12,
        d_ff=3072
    )

    model = OceanRegressor(base).to(DEVICE_AUTO)

    weights_path = get_model_path(
        "PERSONALITY_MODEL_PATH",
        "personality-traits",
        "ocean_model_gpu_e5.pt"
    )

    if not os.path.exists(weights_path):
        raise FileNotFoundError(f"Big Five model weights were not found: {weights_path}")

    model.load_state_dict(torch.load(weights_path, map_location=DEVICE_AUTO))
    model.eval()

    CACHE["bigfive"] = {
        "model": model,
        "tokenizer": tokenizer,
        "weights_path": weights_path
    }

    return CACHE["bigfive"]


def predict_bigfive(question: str, answer: str) -> dict:
    state = load_bigfive()
    model = state["model"]
    tokenizer = state["tokenizer"]
    weights_path = state["weights_path"]

    text = f"Question: {question}\nAnswer: {answer}"
    token_ids = tokenizer.encode(text)

    max_len = 128

    if len(token_ids) > max_len:
        token_ids = token_ids[:max_len - 1] + [2]
    else:
        token_ids = token_ids + [1] * (max_len - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE_AUTO)

    with torch.no_grad():
        outputs = model(input_ids).cpu().numpy()[0]

    raw_scores = outputs * 5.0
    scores = {}

    for trait, score in zip(BIGFIVE_TRAITS, raw_scores):
        scores[trait] = round(float(max(1.0, min(5.0, score))), 4)

    emotional_stability = round(6.0 - scores.get("Neuroticism", 3.0), 4)

    overall_score = round((
        scores.get("Openness", 3.0) +
        scores.get("Conscientiousness", 3.0) +
        scores.get("Extraversion", 3.0) +
        scores.get("Agreeableness", 3.0) +
        emotional_stability
    ) / 5.0, 4)

    return {
        "status": "success",
        "diagnostic_type": "Traits",
        "model": "BIG_FIVE",
        "model_path": weights_path,
        "question": question,
        "answer": answer,
        "scores": scores,
        "derivedScores": {
            "EmotionalStability": emotional_stability
        },
        "score": overall_score
    }


PRACTICAL_LABELS = [
    "Specificity",
    "Ownership",
    "Impact"
]


class PracticalAbilitiesRegressor(nn.Module):
    def __init__(self, base_transformer):
        super().__init__()

        self.transformer = base_transformer

        self.heads = nn.ModuleList([
            nn.Sequential(
                nn.Linear(768, 512),
                nn.ReLU(),
                nn.Dropout(0.1),
                nn.Linear(512, 1)
            )
            for _ in range(3)
        ])

    def forward(self, input_ids):
        hidden_states = self.transformer(input_ids)
        cls_token = hidden_states[:, 0, :]
        outputs = [head(cls_token) for head in self.heads]

        return torch.cat(outputs, dim=1)


def load_practical():
    if "practical" in CACHE:
        return CACHE["practical"]

    tokenizer = get_my_roberta_tokenizer()

    base = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12
    )

    model = PracticalAbilitiesRegressor(base).to(DEVICE_AUTO)

    weights_path = get_model_path(
        "PRACTICAL_ABILITY_MODEL_PATH",
        "practical-ability",
        "abilities_base_model_final.pt"
    )

    if not os.path.exists(weights_path):
        raise FileNotFoundError(f"Practical ability model weights were not found: {weights_path}")

    model.load_state_dict(torch.load(weights_path, map_location=DEVICE_AUTO))
    model.eval()

    CACHE["practical"] = {
        "model": model,
        "tokenizer": tokenizer,
        "weights_path": weights_path
    }

    return CACHE["practical"]


def predict_practical(question: str, answer: str) -> dict:
    state = load_practical()
    model = state["model"]
    tokenizer = state["tokenizer"]
    weights_path = state["weights_path"]

    text = f"<s>Question: {question}</s></s>Answer: {answer}</s>"
    token_ids = tokenizer.encode(text)

    max_len = 128

    if len(token_ids) > max_len:
        token_ids = token_ids[:max_len - 1] + [2]
    else:
        token_ids = token_ids + [1] * (max_len - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE_AUTO)

    with torch.no_grad():
        logits = model(input_ids).cpu().numpy()[0]

    raw_scores = (logits * 4.0) + 1.0

    scores = {
        label: round(float(max(1.0, min(5.0, score))), 4)
        for label, score in zip(PRACTICAL_LABELS, raw_scores)
    }

    average_score = round(sum(scores.values()) / len(scores), 4)

    return {
        "status": "success",
        "diagnostic_type": "PracticalAbilities",
        "model": "PracticalAbilitiesRegressor",
        "model_path": weights_path,
        "question": question,
        "answer": answer,
        "scores": scores,
        "score": average_score
    }


THINKING_LABELS = [
    "logic",
    "clarity",
    "depth",
    "structure",
    "metacognition",
    "applied"
]


class ThinkingQualityClassifier(nn.Module):
    def __init__(self, base_transformer, num_tasks=6, num_classes=5):
        super().__init__()

        self.transformer = base_transformer

        self.classification_heads = nn.ModuleList([
            nn.Sequential(
                nn.Linear(768, 512),
                nn.Tanh(),
                nn.Dropout(0.1),
                nn.Linear(512, num_classes)
            )
            for _ in range(num_tasks)
        ])

    def forward(self, input_ids):
        hidden_states = self.transformer(input_ids)

        mask = (input_ids != 1).unsqueeze(-1).expand(hidden_states.size()).float()
        pooled = torch.sum(hidden_states * mask, dim=1) / torch.clamp(mask.sum(1), min=1e-9)

        return [head(pooled) for head in self.classification_heads]


def load_thinking():
    if "thinking" in CACHE:
        return CACHE["thinking"]

    tokenizer = get_my_roberta_tokenizer()

    base = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12,
        d_ff=3072
    )

    model = ThinkingQualityClassifier(base).to(DEVICE_CPU)

    weights_path = get_model_path(
        "THINKING_QUALITY_MODEL_PATH",
        "thinking-quality",
        "thinking_quality_v1_classifier.pt"
    )

    if not os.path.exists(weights_path):
        raise FileNotFoundError(f"Thinking quality model weights were not found: {weights_path}")

    model.load_state_dict(torch.load(weights_path, map_location=DEVICE_CPU))
    model.eval()

    CACHE["thinking"] = {
        "model": model,
        "tokenizer": tokenizer,
        "weights_path": weights_path
    }

    return CACHE["thinking"]


def predict_thinking(question: str, answer: str) -> dict:
    state = load_thinking()
    model = state["model"]
    tokenizer = state["tokenizer"]
    weights_path = state["weights_path"]

    text = f"Question: {question}\nAnswer: {answer}"
    token_ids = tokenizer.encode(text)

    max_len = 128

    if len(token_ids) > max_len:
        token_ids = token_ids[:max_len - 1] + [2]
    else:
        token_ids = token_ids + [1] * (max_len - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE_CPU)

    with torch.no_grad():
        logits_list = model(input_ids)

    scores = {}

    for index, label_name in enumerate(THINKING_LABELS):
        probabilities = torch.softmax(logits_list[index], dim=1)
        predicted_label = torch.argmax(probabilities, dim=1).item() + 1
        scores[label_name] = int(predicted_label)

    average_score = round(sum(scores.values()) / len(scores), 4)

    return {
        "status": "success",
        "diagnostic_type": "ThinkingQuality",
        "model": "ThinkingQualityClassifier",
        "model_path": weights_path,
        "question": question,
        "answer": answer,
        "scores": scores,
        "score": average_score
    }


EXPERIENCE_LABELS = [
    "exposure",
    "complexity",
    "responsibility"
]


class ExperienceRegressor(nn.Module):
    def __init__(self, base_transformer):
        super().__init__()

        self.transformer = base_transformer

        self.heads = nn.ModuleList([
            nn.Sequential(
                nn.Linear(768, 512),
                nn.Tanh(),
                nn.Dropout(0.1),
                nn.Linear(512, 1)
            )
            for _ in range(3)
        ])

    def forward(self, input_ids):
        hidden_states = self.transformer(input_ids)
        cls_token = hidden_states[:, 0, :]
        outputs = [head(cls_token) for head in self.heads]

        return torch.cat(outputs, dim=1)


def load_experience():
    if "experience" in CACHE:
        return CACHE["experience"]

    tokenizer = get_my_roberta_tokenizer()

    base = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12
    )

    model = ExperienceRegressor(base).to(DEVICE_CPU)

    weights_path = get_model_path(
        "EXPERIENCE_MODEL_PATH",
        "experience",
        "experience_custom_model.pt"
    )

    if not os.path.exists(weights_path):
        raise FileNotFoundError(f"Experience model weights were not found: {weights_path}")

    model.load_state_dict(torch.load(weights_path, map_location=DEVICE_CPU))
    model.eval()

    CACHE["experience"] = {
        "model": model,
        "tokenizer": tokenizer,
        "weights_path": weights_path
    }

    return CACHE["experience"]


def predict_experience(question: str, answer: str) -> dict:
    state = load_experience()
    model = state["model"]
    tokenizer = state["tokenizer"]
    weights_path = state["weights_path"]

    text = f"Question: {question} Answer: {answer}"
    token_ids = tokenizer.encode(text)

    max_len = 128

    if len(token_ids) > max_len:
        token_ids = token_ids[:max_len - 1] + [2]
    else:
        token_ids = token_ids + [1] * (max_len - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE_CPU)

    with torch.no_grad():
        logits = model(input_ids)

    final_scores = (logits.squeeze().cpu().numpy() * 4.0) + 1.0

    scores = {}

    for label, score in zip(EXPERIENCE_LABELS, final_scores):
        scores[label] = round(float(max(1.0, min(5.0, score))), 4)

    average_score = round(sum(scores.values()) / len(scores), 4)

    return {
        "status": "success",
        "diagnostic_type": "Experience",
        "model": "ExperienceRegressor",
        "model_path": weights_path,
        "question": question,
        "answer": answer,
        "scores": scores,
        "score": average_score
    }


LABEL_ALIASES = {
    "traits": "personality",
    "trait": "personality",
    "personality": "personality",
    "big_five": "personality",
    "bigfive": "personality",
    "thinking": "thinking",
    "thinking_quality": "thinking",
    "thinkingquality": "thinking",
    "abilities": "abilities",
    "ability": "abilities",
    "practical_abilities": "abilities",
    "practicalabilities": "abilities",
    "practical_ability": "abilities",
    "experience": "experience",
    "work_experience": "experience"
}

OUTPUT_LABELS = [
    "personality",
    "thinking",
    "abilities",
    "experience"
]


def normalize_router_label(label: str) -> str:
    clean = str(label).strip()
    clean = clean.replace(" ", "_")
    clean = clean.replace("-", "_")
    clean = clean.lower()

    return LABEL_ALIASES.get(clean, clean)


def load_router():
    if "router" in CACHE:
        return CACHE["router"]

    model_path = get_model_path(
        "ROUTING_MODEL_PATH",
        "routing-classifier",
        "router_model.pkl"
    )

    if not os.path.exists(model_path):
        raise FileNotFoundError(f"Router model was not found: {model_path}")

    bundle = joblib.load(model_path)

    if "model" not in bundle:
        raise KeyError("router_model.pkl is missing the 'model' key.")

    if "label_cols" not in bundle:
        raise KeyError("router_model.pkl is missing the 'label_cols' key.")

    CACHE["router"] = {
        "model": bundle["model"],
        "label_cols": [normalize_router_label(label) for label in bundle["label_cols"]],
        "model_path": model_path
    }

    return CACHE["router"]


def extract_router_probabilities(model, text: str, label_cols: List[str]) -> List[float]:
    raw_proba = model.predict_proba([text])

    if isinstance(raw_proba, list):
        probabilities = []

        for item in raw_proba:
            array = np.asarray(item)

            if array.ndim == 2 and array.shape[1] >= 2:
                probabilities.append(float(array[0][1]))
            elif array.ndim == 2:
                probabilities.append(float(array[0][0]))
            else:
                probabilities.append(float(array[0]))

        return probabilities

    array = np.asarray(raw_proba)

    if array.ndim == 2 and array.shape[0] == 1:
        probabilities = array[0].astype(float).tolist()
    else:
        probabilities = array.astype(float).flatten().tolist()

    if len(probabilities) != len(label_cols):
        raise ValueError(
            f"Probability count does not match label count. "
            f"probabilities={len(probabilities)}, labels={len(label_cols)}"
        )

    return probabilities


def predict_routing(question: str, answer: str) -> dict:
    state = load_router()
    model = state["model"]
    label_cols = state["label_cols"]
    model_path = state["model_path"]

    threshold = float(os.getenv("ROUTING_THRESHOLD", "0.35"))
    max_predicted_models = int(os.getenv("ROUTING_MAX_PREDICTED_MODELS", "2"))

    text = f"Question: {question}\nAnswer: {answer}"
    probabilities_list = extract_router_probabilities(model, text, label_cols)

    probabilities = {
        label: 0.0
        for label in OUTPUT_LABELS
    }

    for label, probability in zip(label_cols, probabilities_list):
        normalized_label = normalize_router_label(label)

        if normalized_label in probabilities:
            probabilities[normalized_label] = float(probability)

    predicted = {
        label: 1 if probability >= threshold else 0
        for label, probability in probabilities.items()
    }

    selected_labels = [
        label
        for label, value in predicted.items()
        if value == 1
    ]

    if len(selected_labels) > max_predicted_models:
        top_labels = sorted(
            probabilities.keys(),
            key=lambda label: probabilities[label],
            reverse=True
        )[:max_predicted_models]

        predicted = {
            label: 1 if label in top_labels else 0
            for label in OUTPUT_LABELS
        }

    return {
        "status": "success",
        "threshold": threshold,
        "model_path": model_path,
        "question": question,
        "answer": answer,
        "probabilities": {
            label: round(float(probabilities[label]), 4)
            for label in OUTPUT_LABELS
        },
        "predicted": predicted
    }


def load_diagnosis():
    if "diagnosis" in CACHE:
        return CACHE["diagnosis"]

    if MisunderstandingDiagnoser is None:
        raise RuntimeError("MisunderstandingDiagnoser could not be imported.")

    diagnoser = MisunderstandingDiagnoser(MODELS_DIR)
    CACHE["diagnosis"] = diagnoser

    return CACHE["diagnosis"]


def predict_diagnosis(question: str, answer: str) -> dict:
    diagnoser = load_diagnosis()
    diagnosis = diagnoser.diagnose(question, answer)

    return {
        "status": "success",
        "reason": diagnosis.reason.value,
        "reasons": [reason.value for reason in diagnosis.reasons],
        "feedback": diagnosis.rewritten_question,
        "rewriteNote": diagnosis.rewrite_note,
        "why": diagnosis.why,
        "confidence": diagnosis.confidence_softmax,
        "entailmentTop1": diagnosis.entailment_top1,
        "marginTop1Top2": diagnosis.margin_top1_top2,
        "evidenceQuestion": diagnosis.evidence_q,
        "evidenceAnswer": diagnosis.evidence_a,
        "debugMeta": diagnosis.debug_meta
    }


class JobQuestionMatcher(nn.Module):
    def __init__(self, roberta_base_dir):
        super().__init__()

        if AutoModel is None:
            raise RuntimeError("transformers AutoModel is unavailable.")

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

        return self.regressor(cls_token).squeeze(-1)


def jq_safe_json_loads(value, default):
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


def jq_tags_to_text(tags, max_items=12):
    tags = jq_safe_json_loads(tags, {})

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


def jq_list_to_text(values):
    values = jq_safe_json_loads(values, [])

    if not isinstance(values, list):
        return ""

    return ", ".join([str(value) for value in values])


def jq_get_first(question, names, default=None):
    for name in names:
        if name in question and question[name] is not None:
            return question[name]

    return default


def jq_normalize_question(raw_question):
    question_id = jq_get_first(
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

    question_code = jq_get_first(
        raw_question,
        [
            "questionCode",
            "QuestionCode",
            "code",
            "Code"
        ],
        str(question_id)
    )

    question_text = jq_get_first(
        raw_question,
        [
            "questionText",
            "QuestionText"
        ],
        ""
    )

    diagnostic_target = jq_get_first(
        raw_question,
        [
            "questionDiagnosticTarget",
            "QuestionDiagnosticTarget",
            "diagnosticTarget",
            "DiagnosticTarget"
        ],
        ""
    )

    content_tags = jq_get_first(
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

    scoring_models = jq_get_first(
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
        "questionContentTags": jq_safe_json_loads(content_tags, {}),
        "questionRecommendedScoringModels": jq_safe_json_loads(scoring_models, [])
    }


def jq_build_input_text(job_title, job_description, question):
    question_tags = jq_tags_to_text(question.get("questionContentTags", {}))
    question_models = jq_list_to_text(question.get("questionRecommendedScoringModels", []))

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


def load_job_matcher():
    if "job_matcher" in CACHE:
        return CACHE["job_matcher"]

    if AutoTokenizer is None:
        raise RuntimeError("transformers AutoTokenizer is unavailable.")

    weights_path = get_model_path(
        "JOB_QUESTION_MATCHER_MODEL_PATH",
        "job-question-matcher",
        "job_question_matcher_v2.pt"
    )

    if not os.path.exists(ROBERTA_BASE_DIR):
        raise FileNotFoundError(f"RoBERTa base directory was not found: {ROBERTA_BASE_DIR}")

    if not os.path.exists(weights_path):
        raise FileNotFoundError(f"Job question matcher model weights were not found: {weights_path}")

    tokenizer = AutoTokenizer.from_pretrained(ROBERTA_BASE_DIR)
    model = JobQuestionMatcher(ROBERTA_BASE_DIR).to(DEVICE_AUTO)

    state_dict = torch.load(weights_path, map_location=DEVICE_AUTO)
    model.load_state_dict(state_dict)
    model.eval()

    CACHE["job_matcher"] = {
        "model": model,
        "tokenizer": tokenizer,
        "weights_path": weights_path
    }

    return CACHE["job_matcher"]


def jq_predict_score(model, tokenizer, job_title, job_description, question):
    max_len = int(os.getenv("JOB_QUESTION_MATCHER_MAX_LEN", "256"))
    text = jq_build_input_text(job_title, job_description, question)

    encoded = tokenizer(
        text,
        truncation=True,
        padding="max_length",
        max_length=max_len,
        return_tensors="pt"
    )

    input_ids = encoded["input_ids"].to(DEVICE_AUTO)
    attention_mask = encoded["attention_mask"].to(DEVICE_AUTO)

    with torch.no_grad():
        score = model(input_ids, attention_mask).item()

    return float(score)


def rank_questions(job_title: str, job_description: str, raw_questions: List[dict], top_k: int = 10) -> dict:
    state = load_job_matcher()
    model = state["model"]
    tokenizer = state["tokenizer"]
    weights_path = state["weights_path"]

    ranked_questions = []

    for raw_question in raw_questions:
        question = jq_normalize_question(raw_question)

        if not question["questionText"].strip():
            continue

        score = jq_predict_score(
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
    )[:top_k]

    return {
        "status": "success",
        "model": "job_question_matcher_roberta_base_v2",
        "robertaBaseDir": ROBERTA_BASE_DIR,
        "weightsPath": weights_path,
        "jobTitle": job_title,
        "count": len(ranked_questions),
        "questions": ranked_questions
    }


def load_whisper():
    if "whisper" in CACHE:
        return CACHE["whisper"]

    if WhisperModel is None:
        raise RuntimeError("faster_whisper is not installed or could not be imported.")

    whisper_model_path = os.getenv(
        "WHISPER_MODEL_PATH",
        os.path.abspath(os.path.join(MODELS_DIR, "whisper-small"))
    )

    ffmpeg_path = os.getenv("FFMPEG_PATH", "")

    if ffmpeg_path:
        os.environ["PATH"] += os.pathsep + ffmpeg_path

    if not os.path.exists(whisper_model_path):
        raise FileNotFoundError(f"Whisper model directory was not found: {whisper_model_path}")

    model = WhisperModel(
        whisper_model_path,
        device=os.getenv("WHISPER_DEVICE", "cpu"),
        compute_type=os.getenv("WHISPER_COMPUTE_TYPE", "int8"),
        local_files_only=True
    )

    CACHE["whisper"] = model

    return model


def transcribe_file_warm(audio_path: str) -> dict:
    model = load_whisper()

    if not audio_path or not os.path.exists(audio_path):
        raise FileNotFoundError(f"Audio file was not found: {audio_path}")

    segments, _ = model.transcribe(
        audio_path,
        beam_size=5,
        language=os.getenv("WHISPER_LANGUAGE", "en"),
        vad_filter=True,
        no_speech_threshold=0.6,
        condition_on_previous_text=False
    )

    text = " ".join([
        segment.text.strip()
        for segment in segments
        if segment.text and segment.text.strip()
    ]).strip()

    return {
        "status": "success",
        "text": text
    }


@app.get("/health")
def health():
    return {
        "status": "ok",
        "service": "MindMatch AI Warm Model Server",
        "deviceAuto": str(DEVICE_AUTO),
        "modelsDir": MODELS_DIR,
        "myRobertaCode": MY_ROBERTA_CODE,
        "loadedModels": sorted(list(CACHE.keys())),
        "loadErrors": LOAD_ERRORS
    }


@app.post("/warmup")
def warmup():
    loaders = {
        "relevance": load_relevance,
        "bigfive": load_bigfive,
        "practical": load_practical,
        "thinking": load_thinking,
        "experience": load_experience,
        "diagnosis": load_diagnosis,
        "router": load_router,
        "job_matcher": load_job_matcher,
        "whisper": load_whisper
    }

    results = {}

    for name, loader in loaders.items():
        try:
            loader()
            results[name] = "loaded"
        except Exception as error:
            remember_error(name, error)
            results[name] = f"error: {error}"

    return {
        "status": "done",
        "results": results,
        "loadedModels": sorted(list(CACHE.keys())),
        "loadErrors": LOAD_ERRORS
    }


@app.post("/relevance")
def relevance(request: QARequest):
    try:
        question, answer = require_not_empty(request.question, request.answer)
        score = predict_relevance(question, answer)
        is_relevant = score > float(os.getenv("RELEVANCE_THRESHOLD", "0.5"))

        return {
            "status": "success",
            "relevanceStatus": "Relevant" if is_relevant else "NotRelevant",
            "isRelevant": is_relevant,
            "score": round(float(score), 4),
            "modelName": "RelevanceClassifier",
            "modelVersion": "warm-relevance-v5-logic"
        }

    except Exception as error:
        remember_error("relevance", error)

        return {
            "status": "error",
            "message": str(error),
            "trace": traceback.format_exc()
        }


@app.post("/diagnosis")
def diagnosis(request: QARequest):
    try:
        question, answer = require_not_empty(request.question, request.answer)
        return predict_diagnosis(question, answer)

    except Exception as error:
        remember_error("diagnosis", error)

        return {
            "status": "error",
            "message": str(error),
            "trace": traceback.format_exc()
        }


@app.post("/routing")
def routing(request: QARequest):
    try:
        question, answer = require_not_empty(request.question, request.answer)
        return predict_routing(question, answer)

    except Exception as error:
        remember_error("routing", error)

        return {
            "status": "error",
            "message": str(error),
            "error": str(error),
            "trace": traceback.format_exc()
        }


@app.post("/diagnostic")
def diagnostic(request: DiagnosticRequest):
    try:
        question, answer = require_not_empty(request.question, request.answer)
        diagnostic_type = (request.diagnosticType or "").strip().lower()

        if diagnostic_type in ["traits", "big_five", "bigfive", "personality"]:
            return predict_bigfive(question, answer)

        if diagnostic_type in ["practicalabilities", "practical_abilities", "abilities", "practical"]:
            return predict_practical(question, answer)

        if diagnostic_type in ["thinkingquality", "thinking_quality", "thinking"]:
            return predict_thinking(question, answer)

        if diagnostic_type in ["experience"]:
            return predict_experience(question, answer)

        return {
            "status": "error",
            "message": f"Unknown diagnostic type: {request.diagnosticType}"
        }

    except Exception as error:
        remember_error("diagnostic", error)

        return {
            "status": "error",
            "message": str(error),
            "trace": traceback.format_exc()
        }


@app.post("/job-question-match")
def job_question_match(request: JobQuestionMatchRequest):
    try:
        top_k = max(1, min(50, int(request.topK or 10)))

        return rank_questions(
            job_title=request.jobTitle,
            job_description=request.jobDescription,
            raw_questions=request.questions,
            top_k=top_k
        )

    except Exception as error:
        remember_error("job_question_match", error)

        return {
            "status": "error",
            "message": str(error),
            "trace": traceback.format_exc()
        }


@app.post("/transcribe-file")
def transcribe_file(request: TranscribeFileRequest):
    try:
        return transcribe_file_warm(request.audioPath)

    except Exception as error:
        remember_error("whisper", error)

        return {
            "status": "error",
            "message": str(error),
            "trace": traceback.format_exc()
        }


if __name__ == "__main__":
    import uvicorn

    uvicorn.run(
        "model_server:app",
        host=os.getenv("MODEL_SERVER_HOST", "127.0.0.1"),
        port=int(os.getenv("MODEL_SERVER_PORT", "8001")),
        reload=False
    )