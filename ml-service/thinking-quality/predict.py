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

THINKING_QUALITY_MODEL_DIR = os.getenv(
    "THINKING_QUALITY_MODEL_DIR",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "models", "thinking-quality"))
)

MODEL_FILE = os.getenv(
    "THINKING_QUALITY_MODEL_FILE",
    "thinking_quality_v1_classifier.pt"
)

MODEL_PATH = os.path.join(THINKING_QUALITY_MODEL_DIR, MODEL_FILE)

sys.path.insert(0, MY_ROBERTA_CODE)

from roberta_model import Transformer
from Tokenization import MyRobertaTokenizer


DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")
MAX_LEN = int(os.getenv("THINKING_QUALITY_MAX_LEN", "128"))

LABEL_COLS = [
    "logic",
    "clarity",
    "depth",
    "structure",
    "metacognition",
    "applied"
]


class MyThinkingClassifier(nn.Module):
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

        logits = [
            head(pooled)
            for head in self.classification_heads
        ]

        return logits


def print_diagnostic_result(result):
    print("DIAGNOSTIC_JSON:" + json.dumps(result, ensure_ascii=False))


def predict_thinking_quality(text, model, tokenizer):
    model.eval()

    token_ids = tokenizer.encode(text)

    if len(token_ids) > MAX_LEN:
        token_ids = token_ids[:MAX_LEN - 1] + [2]
    else:
        token_ids = token_ids + [1] * (MAX_LEN - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE)

    with torch.no_grad():
        logits_list = model(input_ids)

    results = {}

    for index, label_name in enumerate(LABEL_COLS):
        probabilities = torch.softmax(logits_list[index], dim=1)
        predicted_label = torch.argmax(probabilities, dim=1).item() + 1
        results[label_name] = int(predicted_label)

    return results


def load_model_and_tokenizer():
    vocab_path = os.path.join(MY_ROBERTA_CODE, "vocab.json")
    merges_path = os.path.join(MY_ROBERTA_CODE, "merges.txt")

    if not os.path.exists(MY_ROBERTA_CODE):
        raise FileNotFoundError(f"My_Roberta folder was not found: {MY_ROBERTA_CODE}")

    if not os.path.exists(vocab_path):
        raise FileNotFoundError(f"vocab.json was not found: {vocab_path}")

    if not os.path.exists(merges_path):
        raise FileNotFoundError(f"merges.txt was not found: {merges_path}")

    if not os.path.exists(MODEL_PATH):
        raise FileNotFoundError(f"Thinking quality model weights were not found: {MODEL_PATH}")

    tokenizer = MyRobertaTokenizer(vocab_path, merges_path)

    base_transformer = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12,
        d_ff=3072
    )

    model = MyThinkingClassifier(base_transformer).to(DEVICE)

    state_dict = torch.load(MODEL_PATH, map_location=DEVICE)
    model.load_state_dict(state_dict)
    model.eval()

    return model, tokenizer


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
        scores = predict_thinking_quality(text, model, tokenizer)

        score = round(sum(scores.values()) / len(scores), 4)

        diagnostic_json = {
            "status": "success",
            "diagnostic_type": "ThinkingQuality",
            "model": "ThinkingQualityClassifier",
            "model_path": MODEL_PATH,
            "question": question,
            "answer": answer,
            "scores": scores,
            "score": score
        }

        print(f"SCORE:{score}")
        print_diagnostic_result(diagnostic_json)

    except Exception as error:
        print_diagnostic_result({
            "status": "error",
            "message": str(error)
        })
        sys.exit(1)


if __name__ == "__main__":
    main()