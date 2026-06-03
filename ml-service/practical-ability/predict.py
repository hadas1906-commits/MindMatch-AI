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

PRACTICAL_ABILITY_MODEL_DIR = os.getenv(
    "PRACTICAL_ABILITY_MODEL_DIR",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "models", "practical-ability"))
)

MODEL_FILE = os.getenv(
    "PRACTICAL_ABILITY_MODEL_FILE",
    "abilities_base_model_final.pt"
)

MODEL_WEIGHTS = os.path.join(PRACTICAL_ABILITY_MODEL_DIR, MODEL_FILE)

sys.path.insert(0, MY_ROBERTA_CODE)

from roberta_model import Transformer
from Tokenization import MyRobertaTokenizer


DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")

LABEL_COLS = [
    "Specificity",
    "Ownership",
    "Impact"
]


class MyCustomAbilitiesRegressor(nn.Module):
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


def get_prediction(question, answer, model, tokenizer, max_len=128):
    text = f"<s>Question: {question}</s></s>Answer: {answer}</s>"

    token_ids = tokenizer.encode(text)

    if len(token_ids) > max_len:
        token_ids = token_ids[:max_len - 1] + [2]
    else:
        token_ids = token_ids + [1] * (max_len - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE)

    with torch.no_grad():
        logits = model(input_ids).cpu().numpy()[0]

    scores = (logits * 4.0) + 1.0

    clipped_scores = [
        float(max(1.0, min(5.0, score)))
        for score in scores
    ]

    return clipped_scores


def predict_once(question, answer, model, tokenizer):
    scores = get_prediction(question, answer, model, tokenizer)

    results = {}

    for label, score in zip(LABEL_COLS, scores):
        results[label] = round(score, 4)

    average_score = round(sum(results.values()) / len(results), 4)

    output = {
        "status": "success",
        "diagnostic_type": "PracticalAbilities",
        "model": "MyCustomAbilitiesRegressor",
        "model_path": MODEL_WEIGHTS,
        "question": question,
        "answer": answer,
        "scores": results,
        "score": average_score
    }

    print(f"SCORE:{average_score}")
    print("DIAGNOSTIC_JSON:" + json.dumps(output, ensure_ascii=False))

    return output


def load_model_and_tokenizer():
    vocab_path = os.path.join(MY_ROBERTA_CODE, "vocab.json")
    merges_path = os.path.join(MY_ROBERTA_CODE, "merges.txt")

    if not os.path.exists(MY_ROBERTA_CODE):
        raise FileNotFoundError(f"My_Roberta folder was not found: {MY_ROBERTA_CODE}")

    if not os.path.exists(vocab_path):
        raise FileNotFoundError(f"vocab.json was not found: {vocab_path}")

    if not os.path.exists(merges_path):
        raise FileNotFoundError(f"merges.txt was not found: {merges_path}")

    if not os.path.exists(MODEL_WEIGHTS):
        raise FileNotFoundError(f"Practical ability model weights were not found: {MODEL_WEIGHTS}")

    tokenizer = MyRobertaTokenizer(vocab_path, merges_path)

    base_transformer = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12
    )

    model = MyCustomAbilitiesRegressor(base_transformer).to(DEVICE)

    state_dict = torch.load(MODEL_WEIGHTS, map_location=DEVICE)
    model.load_state_dict(state_dict)
    model.eval()

    return model, tokenizer


def print_error(message):
    output = {
        "status": "error",
        "message": message
    }

    print("DIAGNOSTIC_JSON:" + json.dumps(output, ensure_ascii=False))


def main():
    if len(sys.argv) < 3:
        print_error("Expected arguments: question answer")
        sys.exit(1)

    question = sys.argv[1]
    answer = sys.argv[2]

    if not answer.strip():
        print_error("Answer is empty")
        sys.exit(1)

    try:
        model, tokenizer = load_model_and_tokenizer()
        predict_once(question, answer, model, tokenizer)

    except Exception as error:
        print_error(str(error))
        sys.exit(1)


if __name__ == "__main__":
    main()