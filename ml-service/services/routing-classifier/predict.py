import io
import json
import os
import sys

import joblib
import numpy as np


sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")


BASE_DIR = os.path.dirname(os.path.abspath(__file__))

MODEL_PATH = os.getenv(
    "ROUTING_MODEL_PATH",
    os.path.abspath(
        os.path.join(
            BASE_DIR,
            "..",
            "..",
            "models",
            "routing-classifier",
            "router_model.pkl"
        )
    )
)

THRESHOLD = float(os.getenv("ROUTING_THRESHOLD", "0.35"))
MAX_PREDICTED_MODELS = int(os.getenv("ROUTING_MAX_PREDICTED_MODELS", "2"))


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


def normalize_label(label):
    normalized = str(label).strip()
    normalized = normalized.replace(" ", "_")
    normalized = normalized.replace("-", "_")
    normalized = normalized.lower()

    return LABEL_ALIASES.get(normalized, normalized)


def print_routing_result(result):
    print("ROUTING_JSON:" + json.dumps(result, ensure_ascii=False))


def load_router():
    if not os.path.exists(MODEL_PATH):
        raise FileNotFoundError(f"Routing model file was not found: {MODEL_PATH}")

    bundle = joblib.load(MODEL_PATH)

    if "model" not in bundle:
        raise KeyError("router_model.pkl is missing the 'model' key.")

    if "label_cols" not in bundle:
        raise KeyError("router_model.pkl is missing the 'label_cols' key.")

    model = bundle["model"]
    label_cols = [normalize_label(label) for label in bundle["label_cols"]]

    return model, label_cols


def extract_probabilities(model, text, label_cols):
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


def build_input_text(question, answer):
    return f"Question: {question}\nAnswer: {answer}"


def predict_routing(question, answer):
    model, label_cols = load_router()
    text = build_input_text(question, answer)

    probabilities_list = extract_probabilities(model, text, label_cols)

    probabilities = {
        label: 0.0
        for label in OUTPUT_LABELS
    }

    for label, probability in zip(label_cols, probabilities_list):
        normalized_label = normalize_label(label)

        if normalized_label in probabilities:
            probabilities[normalized_label] = float(probability)

    predicted = {
        label: 1 if probability >= THRESHOLD else 0
        for label, probability in probabilities.items()
    }

    selected_labels = [
        label
        for label, value in predicted.items()
        if value == 1
    ]

    if len(selected_labels) > MAX_PREDICTED_MODELS:
        top_labels = sorted(
            probabilities.keys(),
            key=lambda label: probabilities[label],
            reverse=True
        )[:MAX_PREDICTED_MODELS]

        predicted = {
            label: 1 if label in top_labels else 0
            for label in OUTPUT_LABELS
        }

    return {
        "status": "success",
        "threshold": THRESHOLD,
        "model_path": MODEL_PATH,
        "question": question,
        "answer": answer,
        "probabilities": {
            label: round(float(probabilities[label]), 4)
            for label in OUTPUT_LABELS
        },
        "predicted": predicted
    }


def main():
    if len(sys.argv) < 3:
        print_routing_result({
            "status": "error",
            "message": "Expected arguments: question answer"
        })
        sys.exit(1)

    question = sys.argv[1]
    answer = sys.argv[2]

    if not answer.strip():
        print_routing_result({
            "status": "error",
            "message": "Answer is empty"
        })
        sys.exit(1)

    try:
        result = predict_routing(question, answer)
        print_routing_result(result)

    except Exception as error:
        print_routing_result({
            "status": "error",
            "message": str(error),
            "model_path": MODEL_PATH
        })
        sys.exit(1)


if __name__ == "__main__":
    main()