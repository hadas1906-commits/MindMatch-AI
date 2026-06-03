import io
import json
import os
import sys

import torch
import torch.nn as nn

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")


BASE_DIR = os.path.dirname(os.path.abspath(__file__))

MY_ROBERTA_CODE = os.getenv(
    "MY_ROBERTA_CODE",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "models", "my-roberta"))
)

PERSONALITY_MODEL_DIR = os.getenv(
    "PERSONALITY_MODEL_DIR",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "models", "personality-traits"))
)

MODEL_FILE = os.getenv(
    "PERSONALITY_MODEL_FILE",
    "ocean_model_gpu_e5.pt"
)

MODEL_PATH = os.path.join(PERSONALITY_MODEL_DIR, MODEL_FILE)

sys.path.insert(0, MY_ROBERTA_CODE)

from roberta_model import Transformer
from Tokenization import MyRobertaTokenizer


DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")

TRAITS = [
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


def predict_personality(text, model, tokenizer, max_len=128):
    model.eval()

    tokens = tokenizer.encode(str(text))

    if len(tokens) > max_len:
        tokens = tokens[:max_len - 1] + [2]
    else:
        tokens = tokens + [1] * (max_len - len(tokens))

    input_ids = torch.tensor([tokens], dtype=torch.long).to(DEVICE)

    with torch.no_grad():
        outputs = model(input_ids).cpu().numpy()[0]

    raw_scores = outputs * 5.0
    results = {}

    for trait, score in zip(TRAITS, raw_scores):
        final_score = float(max(1.0, min(5.0, score)))
        results[trait] = round(final_score, 4)

    return results


def load_model_and_tokenizer():
    vocab_file = os.path.join(MY_ROBERTA_CODE, "vocab.json")
    merges_file = os.path.join(MY_ROBERTA_CODE, "merges.txt")

    if not os.path.exists(MY_ROBERTA_CODE):
        raise FileNotFoundError(f"My_Roberta folder was not found: {MY_ROBERTA_CODE}")

    if not os.path.exists(vocab_file):
        raise FileNotFoundError(f"vocab.json was not found: {vocab_file}")

    if not os.path.exists(merges_file):
        raise FileNotFoundError(f"merges.txt was not found: {merges_file}")

    if not os.path.exists(MODEL_PATH):
        raise FileNotFoundError(f"Personality model weights were not found: {MODEL_PATH}")

    tokenizer = MyRobertaTokenizer(vocab_file, merges_file)

    base_transformer = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12,
        d_ff=3072
    )

    model = OceanRegressor(base_transformer).to(DEVICE)

    state_dict = torch.load(MODEL_PATH, map_location=DEVICE)
    model.load_state_dict(state_dict)
    model.eval()

    return model, tokenizer


def calculate_overall_score(scores):
    openness = scores.get("Openness", 3.0)
    conscientiousness = scores.get("Conscientiousness", 3.0)
    extraversion = scores.get("Extraversion", 3.0)
    agreeableness = scores.get("Agreeableness", 3.0)
    neuroticism = scores.get("Neuroticism", 3.0)

    emotional_stability = 6.0 - neuroticism

    overall_score = (
        openness +
        conscientiousness +
        extraversion +
        agreeableness +
        emotional_stability
    ) / 5.0

    return round(overall_score, 4)


def print_diagnostic_result(result):
    print("DIAGNOSTIC_JSON:" + json.dumps(result, ensure_ascii=False))


def main():
    if len(sys.argv) < 3:
        print_diagnostic_result({
            "status": "error",
            "message": "Expected arguments: question answer"
        })
        sys.exit(1)

    question = sys.argv[1]
    answer = sys.argv[2]

    if not answer.strip():
        print_diagnostic_result({
            "status": "error",
            "message": "Answer is empty"
        })
        sys.exit(1)

    try:
        text = f"Question: {question}\nAnswer: {answer}"

        model, tokenizer = load_model_and_tokenizer()
        scores = predict_personality(text, model, tokenizer)
        overall_score = calculate_overall_score(scores)

        diagnostic_json = {
            "status": "success",
            "diagnostic_type": "Traits",
            "model": "BIG_FIVE",
            "model_path": MODEL_PATH,
            "question": question,
            "answer": answer,
            "scores": scores,
            "derivedScores": {
                "EmotionalStability": round(6.0 - scores.get("Neuroticism", 3.0), 4)
            },
            "score": overall_score
        }

        print(f"SCORE:{overall_score}")
        print_diagnostic_result(diagnostic_json)

    except Exception as error:
        print_diagnostic_result({
            "status": "error",
            "message": str(error)
        })
        sys.exit(1)


if __name__ == "__main__":
    main()