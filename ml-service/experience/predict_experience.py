import os
import sys
import json
import torch
import torch.nn as nn
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MY_ROBERTA_CODE = os.getenv(
    "MY_ROBERTA_CODE",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "shared", "my-roberta"))
)

MODEL_PATH = os.getenv(
    "ROBERTA_BASE_PATH",
    os.path.abspath(os.path.join(BASE_DIR, "..", "..", "models", "roberta-base"))
)
TRAINED_MODEL_WEIGHTS = os.path.join(
    BASE_DIR,
    "experience_custom_model.pt"
)
DEVICE = torch.device("cpu")
LABEL_COLS = ["exposure", "complexity", "responsibility"]
from roberta_model import Transformer
from Tokenization import MyRobertaTokenizer
class ExperienceCustomRegressor(nn.Module):
    def __init__(self, base_transformer):
        super().__init__()
        self.transformer = base_transformer
        self.heads = nn.ModuleList([
            nn.Sequential(
                nn.Linear(768, 512),
                nn.Tanh(),
                nn.Dropout(0.1),
                nn.Linear(512, 1)
            ) for _ in range(3)
        ])
    def forward(self, input_ids):
        hidden_states = self.transformer(input_ids)
        cls_token = hidden_states[:, 0, :]
        outputs = [head(cls_token) for head in self.heads]
        preds = torch.cat(outputs, dim=1)
        return preds
def predict_experience(question, answer, model, tokenizer, max_len=128):
    model.eval()
    text = f"Question: {question} Answer: {answer}"
    ids = tokenizer.encode(text)
    if len(ids) > max_len:
        ids = ids[:max_len]
    else:
        ids = ids + [1] * (max_len - len(ids))
    ids_tensor = torch.tensor([ids], dtype=torch.long).to(DEVICE)
    with torch.no_grad():
        logits = model(ids_tensor)
    final_scores = (logits.squeeze().cpu().numpy() * 4.0) + 1.0
    results = {}
    print(f"Q: {question}")
    print(f"A: {answer[:100]}...")
    for name, score in zip(LABEL_COLS, final_scores):
        score = float(max(1.0, min(5.0, score)))
        results[name] = round(score, 4)
    average_score = round(sum(results.values()) / len(results), 4)
    print(f"SCORE:{average_score}")
    output = {
        "status": "success",
        "diagnostic_type": "Experience",
        "model": "ExperienceCustomRegressor",
        "model_path": TRAINED_MODEL_WEIGHTS,
        "question": question,
        "answer": answer,
        "scores": results,
        "score": average_score
    }
    print("DIAGNOSTIC_JSON:" + json.dumps(output, ensure_ascii=False))
    return output
def main():
    if len(sys.argv) >= 3:
        question = sys.argv[1]
        answer = sys.argv[2]
    vocab_file = os.path.join(MODEL_PATH, "vocab.json")
    merges_file = os.path.join(MODEL_PATH, "merges.txt")
    if not os.path.exists(vocab_file):
        sys.exit(1)
    if not os.path.exists(merges_file):
        sys.exit(1)
    tokenizer = MyRobertaTokenizer(vocab_file, merges_file)
    base_trans = Transformer(
        vocab_size=50265,
        d_model=768,
        num_layers=12,
        num_heads=12
    )
    model = ExperienceCustomRegressor(base_trans).to(DEVICE)
    if os.path.exists(TRAINED_MODEL_WEIGHTS):
        model.load_state_dict(torch.load(TRAINED_MODEL_WEIGHTS, map_location=DEVICE))
    else:
        sys.exit(1)
    predict_experience(question, answer, model, tokenizer)
if __name__ == "__main__":
    main()