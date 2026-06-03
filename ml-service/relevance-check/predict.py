import io
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

RELEVANCE_MODEL_DIR = os.getenv(
    "RELEVANCE_MODEL_DIR",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "models", "relevance-check"))
)

MODEL_FILE = os.getenv(
    "RELEVANCE_MODEL_FILE",
    "relevance_model_v5_logic.pt"
)

MODEL_WEIGHTS = os.path.join(RELEVANCE_MODEL_DIR, MODEL_FILE)

sys.path.insert(0, MY_ROBERTA_CODE)

from roberta_model import Transformer
from Tokenization import MyRobertaTokenizer


DEVICE = torch.device("cuda" if torch.cuda.is_available() else "cpu")
MAX_LEN = int(os.getenv("RELEVANCE_MAX_LEN", "256"))
THRESHOLD = float(os.getenv("RELEVANCE_THRESHOLD", "0.5"))


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


def load_model():
    vocab_path = os.path.join(MY_ROBERTA_CODE, "vocab.json")
    merges_path = os.path.join(MY_ROBERTA_CODE, "merges.txt")

    if not os.path.exists(MY_ROBERTA_CODE):
        raise FileNotFoundError(f"My_Roberta folder was not found: {MY_ROBERTA_CODE}")

    if not os.path.exists(vocab_path):
        raise FileNotFoundError(f"vocab.json was not found: {vocab_path}")

    if not os.path.exists(merges_path):
        raise FileNotFoundError(f"merges.txt was not found: {merges_path}")

    if not os.path.exists(MODEL_WEIGHTS):
        raise FileNotFoundError(f"Relevance model weights were not found: {MODEL_WEIGHTS}")

    tokenizer = MyRobertaTokenizer(vocab_path, merges_path)

    base_transformer = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12,
        d_ff=3072
    )

    model = RelevanceClassifier(base_transformer).to(DEVICE)

    state_dict = torch.load(MODEL_WEIGHTS, map_location=DEVICE)
    model.load_state_dict(state_dict)
    model.eval()

    return model, tokenizer


def predict(question, answer, model, tokenizer):
    text = f"<s>{question}</s></s>{answer}</s>"
    token_ids = tokenizer.encode(text)

    if len(token_ids) > MAX_LEN:
        token_ids = token_ids[:MAX_LEN - 1] + [2]
    else:
        token_ids = token_ids + [1] * (MAX_LEN - len(token_ids))

    input_ids = torch.tensor([token_ids], dtype=torch.long).to(DEVICE)

    with torch.no_grad():
        score = model(input_ids).item()

    return float(score)


def main():
    if len(sys.argv) < 3:
        print("ERROR: Expected arguments: question answer")
        sys.exit(1)

    question = sys.argv[1]
    answer = sys.argv[2]

    if not answer.strip():
        print("ERROR: Answer is empty")
        sys.exit(1)

    try:
        model, tokenizer = load_model()
        score = predict(question, answer, model, tokenizer)
        status = "RELEVANT" if score > THRESHOLD else "IRRELEVANT"

        print(f"RESULT:{status}|SCORE:{score:.4f}")

    except Exception as error:
        print(f"ERROR: {error}")
        sys.exit(1)


if __name__ == "__main__":
    main()