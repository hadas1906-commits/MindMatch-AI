from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Dict, List, Optional, Tuple

import json
import os
import re
import sys

import numpy as np
import torch
from sentence_transformers import SentenceTransformer
from transformers import AutoModelForSequenceClassification, AutoTokenizer

from config_loader import load_config


CFG = load_config()


class Reason(str, Enum):
    topic_drift = "topic_drift"
    task_mismatch = "task_mismatch"
    abstraction_mismatch = "abstraction_mismatch"
    constraint_or_assumption_mismatch = "constraint_or_assumption_mismatch"
    ambiguity_resolution = "ambiguity_resolution"
    incomplete_coverage = "incomplete_coverage"
    concept_confusion = "concept_confusion"
    knowledge_gap = "knowledge_gap"
    evasive_nonanswer = "evasive_nonanswer"
    no_example = "no_example"
    none_match = "none_match"


@dataclass
class Diagnosis:
    reason: Reason
    reasons: List[Reason]
    rewritten_question: str
    rewrite_note: str
    entailment_top1: float
    confidence_softmax: float
    margin_top1_top2: float
    why: str
    evidence_q: str
    evidence_a: List[str]
    evidence_a_sims: List[float]
    debug_entailment: Dict[str, float]
    debug_softmax: Dict[str, float]
    debug_meta: Dict[str, object]


HYPOTHESES: Dict[Reason, str] = {
    Reason(key): value for key, value in CFG["hypotheses"].items()
}

REASON_PRIORITY = {
    Reason(key): int(value) for key, value in CFG["reason_priority"].items()
}


def detect_kg_flags(answer_lower: str) -> Tuple[bool, bool, bool, int]:
    kg_strong_pattern = CFG["regex"]["KG_STRONG"]
    kg_soft_pattern = CFG["regex"]["KG_SOFT"]
    recovery_pattern = CFG["regex"]["RECOVERY"]
    min_recovery_tokens = int(CFG["thresholds"]["MIN_RECOVERY_TOKENS"])

    strong = re.search(kg_strong_pattern, answer_lower) is not None
    soft = re.search(kg_soft_pattern, answer_lower) is not None
    recovery = re.search(recovery_pattern, answer_lower) is not None

    post_len = 0

    for transition in [" but ", " however ", " though ", " still "]:
        if transition in answer_lower:
            post_len = len(answer_lower.split(transition, 1)[1].split())
            break

    has_recovery_content = recovery and post_len >= min_recovery_tokens

    return strong, soft, has_recovery_content, post_len


def looks_like_definition(answer_lower: str) -> bool:
    definition_pattern = CFG["regex"]["DEF"]
    concrete_pattern = CFG["regex"]["CONCRETE"]
    max_words = int(CFG["thresholds"]["DEF_MAX_WORDS"])

    return (
        re.search(definition_pattern, answer_lower) is not None
        and len(answer_lower.split()) <= max_words
        and re.search(concrete_pattern, answer_lower) is None
    )


def split_sentences(text: str) -> List[str]:
    parts = re.split(r"(?<=[.!?])\s+", text.strip())
    return [part.strip() for part in parts if part.strip()]


def qa_similarity(
    question: str,
    answer: str,
    embedder: SentenceTransformer
) -> float:
    question_embedding = embedder.encode([question], normalize_embeddings=True)[0]
    answer_embedding = embedder.encode([answer], normalize_embeddings=True)[0]

    return float(np.dot(question_embedding, answer_embedding))


def softmax(values: List[float], alpha: float = 8.0) -> List[float]:
    array = np.array(values, dtype=np.float64) * float(alpha)
    array = array - np.max(array)

    exp_values = np.exp(array)
    probabilities = exp_values / (np.sum(exp_values) + 1e-12)

    return probabilities.tolist()


def pick_topk_evidence(
    answer: str,
    question: str,
    embedder: SentenceTransformer,
    k: int,
    min_sim: float,
    max_chars: int
) -> Tuple[List[str], List[float]]:
    answer_sentences = split_sentences(answer)

    if not answer_sentences:
        return [answer[:max_chars]], [0.0]

    question_embedding = embedder.encode([question], normalize_embeddings=True)[0]
    answer_embeddings = embedder.encode(answer_sentences, normalize_embeddings=True)

    similarities = [
        float(np.dot(question_embedding, embedding))
        for embedding in answer_embeddings
    ]

    ranked_indices = sorted(
        range(len(answer_sentences)),
        key=lambda index: similarities[index],
        reverse=True
    )

    picked_sentences = []
    picked_similarities = []

    for index in ranked_indices[:k]:
        if similarities[index] >= min_sim:
            picked_sentences.append(answer_sentences[index][:max_chars])
            picked_similarities.append(float(similarities[index]))

    if not picked_sentences:
        best_index = ranked_indices[0]
        return (
            [answer_sentences[best_index][:max_chars]],
            [float(similarities[best_index])]
        )

    return picked_sentences, picked_similarities


EVASIVE_PROTOTYPES = CFG["prototypes"]["EVASIVE"]
KNOWLEDGE_GAP_PROTOTYPES = CFG["prototypes"]["KNOWLEDGE_GAP"]


def max_similarity_to_prototypes(
    text: str,
    prototypes: List[str],
    embedder: SentenceTransformer
) -> float:
    text_embedding = embedder.encode([text], normalize_embeddings=True)[0]
    prototype_embeddings = embedder.encode(prototypes, normalize_embeddings=True)

    similarities = [
        float(np.dot(text_embedding, prototype_embedding))
        for prototype_embedding in prototype_embeddings
    ]

    return float(max(similarities)) if similarities else 0.0


def rewrite_question(question: str, reason: Reason) -> Tuple[str, str]:
    cleaned_question = question.strip()

    if len(cleaned_question) < 3:
        return cleaned_question, "too_short"

    def ensure_sentence_ending(text: str) -> str:
        text = text.strip()
        return text if text.endswith(("?", "!", ".")) else text + "?"

    rule = CFG["rewrite_rules"].get(reason.value)

    if not rule:
        return ensure_sentence_ending(cleaned_question), "no_rule_matched"

    return (
        ensure_sentence_ending(rule["template"].format(q=cleaned_question)),
        rule["note"]
    )


class MisunderstandingDiagnoser:
    def __init__(self, models_dir: str, softmax_alpha: Optional[float] = None):
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

        self.mnli_path = os.path.join(models_dir, "roberta-large-mnli")
        self.embed_path = os.path.join(models_dir, "all-MiniLM-L6-v2")

        default_alpha = float(CFG["thresholds"]["SOFTMAX_ALPHA"])
        self.softmax_alpha = float(default_alpha if softmax_alpha is None else softmax_alpha)

        if not os.path.isdir(self.mnli_path):
            raise FileNotFoundError(f"MNLI model folder not found: {self.mnli_path}")

        if not os.path.isdir(self.embed_path):
            raise FileNotFoundError(f"Embedder model folder not found: {self.embed_path}")

        self.tokenizer = AutoTokenizer.from_pretrained(self.mnli_path)
        self.model = AutoModelForSequenceClassification.from_pretrained(self.mnli_path)

        self.model.eval()
        self.model.to(self.device)

        self.entailment_id = 2

        if hasattr(self.model.config, "label2id") and isinstance(self.model.config.label2id, dict):
            for key, value in self.model.config.label2id.items():
                if str(key).lower().startswith("entail"):
                    self.entailment_id = int(value)

        self.embedder = SentenceTransformer(self.embed_path)

    @torch.inference_mode()
    def _entailment(self, premise: str, hypothesis: str) -> float:
        inputs = self.tokenizer(
            premise,
            hypothesis,
            truncation=True,
            padding=True,
            return_tensors="pt",
            max_length=512
        ).to(self.device)

        logits = self.model(**inputs).logits[0]
        probabilities = torch.softmax(logits, dim=-1)

        return float(probabilities[self.entailment_id].detach().cpu().item())

    def diagnose(self, question: str, answer: str) -> Diagnosis:
        premise = CFG.get(
            "premise_template",
            "Question: {q}\nAnswer: {a}"
        ).format(q=question, a=answer)

        scored = []
        entailment_map = {}

        for reason, hypothesis in HYPOTHESES.items():
            score = self._entailment(premise, hypothesis)
            scored.append((reason, float(score)))
            entailment_map[reason.value] = float(score)

        scored.sort(key=lambda item: item[1], reverse=True)

        best_reason, best_entailment = scored[0]

        sim_qa = qa_similarity(question, answer, self.embedder)
        kg_sim = max_similarity_to_prototypes(answer, KNOWLEDGE_GAP_PROTOTYPES, self.embedder)
        evasive_sim = max_similarity_to_prototypes(answer, EVASIVE_PROTOTYPES, self.embedder)

        answer_lower = answer.lower().strip()
        question_lower = question.lower().strip()

        no_example_lex = re.search(CFG["regex"]["NO_EXAMPLE"], answer_lower) is not None

        kg_strong, kg_soft, has_recovery_content, _ = detect_kg_flags(answer_lower)
        kg_lex = kg_strong or kg_soft

        looks_definitional = looks_like_definition(answer_lower)
        asks_steps = any(phrase in question_lower for phrase in CFG["asks_steps_phrases"])

        forced_reason = None

        hard_offtopic_t = float(CFG["thresholds"]["HARD_OFFTOPIC_T"])
        soft_offtopic_t = float(CFG["thresholds"]["SOFT_OFFTOPIC_T"])
        weak_nli_t = float(CFG["thresholds"]["WEAK_NLI_T"])
        knowledge_gap_t = float(CFG["thresholds"]["KNOWLEDGE_GAP_T"])
        evasive_t = float(CFG["thresholds"]["EVASIVE_T"])
        tie_margin_t = float(CFG["thresholds"]["TIE_MARGIN_T"])
        tie_conf_t = float(CFG["thresholds"]["TIE_CONF_T"])

        if kg_strong and not has_recovery_content:
            forced_reason = Reason.knowledge_gap

        if forced_reason is None and sim_qa < hard_offtopic_t:
            forced_reason = Reason.topic_drift

        if forced_reason is None and sim_qa < soft_offtopic_t and scored[0][1] < weak_nli_t:
            forced_reason = Reason.topic_drift

        if forced_reason is None and asks_steps and looks_definitional and sim_qa >= soft_offtopic_t:
            forced_reason = Reason.task_mismatch

        if (
            forced_reason is None
            and sim_qa >= soft_offtopic_t
            and kg_sim >= knowledge_gap_t
            and scored[0][1] < weak_nli_t
        ):
            forced_reason = Reason.knowledge_gap

        if forced_reason is None and no_example_lex:
            forced_reason = Reason.no_example

        if (
            forced_reason is None
            and sim_qa >= soft_offtopic_t
            and evasive_sim >= evasive_t
            and scored[0][1] < weak_nli_t
        ):
            forced_reason = Reason.evasive_nonanswer

        if forced_reason is None and best_reason == Reason.topic_drift and sim_qa >= 0.35 and len(scored) > 1:
            best_reason, best_entailment = scored[1]

        probabilities = softmax([score for _, score in scored], alpha=self.softmax_alpha)

        softmax_map = {
            scored[index][0].value: float(probabilities[index])
            for index in range(len(scored))
        }

        confidence_softmax = float(softmax_map.get(best_reason.value, 0.0))

        if forced_reason is not None:
            best_reason = forced_reason

        if forced_reason == Reason.topic_drift:
            confidence_softmax = float(min(0.99, max(0.60, 1.0 - sim_qa)))
        elif forced_reason == Reason.evasive_nonanswer:
            confidence_softmax = float(min(0.95, max(0.60, evasive_sim)))
        elif forced_reason == Reason.knowledge_gap:
            confidence_softmax = 0.95 if kg_lex else float(min(0.95, max(0.60, kg_sim)))
        elif forced_reason == Reason.task_mismatch:
            confidence_softmax = float(min(0.95, max(0.70, sim_qa)))
        elif forced_reason == Reason.no_example:
            confidence_softmax = float(entailment_map.get(Reason.no_example.value, 0.0))

        if forced_reason is None and len(scored) > 1:
            top1 = scored[0][1]
            top2 = scored[1][1]
            margin = float(top1 - top2)

            if margin < 0.03:
                confidence_softmax = max(0.0, confidence_softmax - 0.10)

        if forced_reason is not None:
            selected = [best_reason]
            tie_group_size = 1
        else:
            top_score = float(scored[0][1])

            tie_group = [
                (reason, float(score))
                for reason, score in scored
                if (top_score - float(score)) <= tie_margin_t
            ]

            tie_group_size = len(tie_group)

            def rank_key(item: Tuple[Reason, float]):
                reason, score = item
                return -score, REASON_PRIORITY.get(reason, 10**9)

            tie_group_sorted = sorted(tie_group, key=rank_key)
            max_returned = int(CFG.get("tie", {}).get("max_reasons_returned", 2))

            selected = [
                item[0] for item in tie_group_sorted[:max_returned]
            ] if tie_group_sorted else [scored[0][0]]

            best_reason = selected[0]

        best_entailment_final = float(entailment_map.get(best_reason.value, 0.0))

        if len(selected) >= 2:
            runner_up_reason_final = selected[1]
            runner_up_entailment_final = float(entailment_map.get(runner_up_reason_final.value, 0.0))
        else:
            runner_up_reason_final = scored[1][0] if len(scored) > 1 else best_reason
            runner_up_entailment_final = float(entailment_map.get(runner_up_reason_final.value, 0.0))

        margin_final = float(best_entailment_final - runner_up_entailment_final)

        is_tie = (
            len(selected) == 2
            or margin_final < tie_margin_t
            or confidence_softmax < tie_conf_t
        )

        none_match_conf_t = float(CFG["thresholds"]["NONE_MATCH_CONF_T"])
        none_match_entail_t = float(CFG["thresholds"]["NONE_MATCH_ENTAIL_T"])

        if confidence_softmax < none_match_conf_t and best_entailment_final < none_match_entail_t:
            best_reason = Reason.none_match
            selected = [Reason.none_match]
            best_entailment_final = float(
                entailment_map.get(Reason.none_match.value, best_entailment_final)
            )
            runner_up_reason_final = best_reason
            runner_up_entailment_final = best_entailment_final
            margin_final = 0.0
            is_tie = False
            tie_group_size = 1

        if forced_reason is not None:
            runner = None

            for reason, _ in scored:
                if reason != best_reason:
                    runner = reason
                    break

            if runner is None:
                runner = best_reason

            runner_up_reason_final = runner
            runner_up_entailment_final = float(entailment_map.get(runner.value, 0.0))
            margin_final = float(best_entailment_final - runner_up_entailment_final)
            is_tie = False

        top2_debug = [
            {
                "reason": best_reason.value,
                "entailment": float(best_entailment_final)
            }
        ]

        top2_debug.append({
            "reason": runner_up_reason_final.value if runner_up_reason_final else None,
            "entailment": float(runner_up_entailment_final)
        })

        evidence_min_sim = float(CFG["thresholds"]["EVIDENCE_MIN_SIM"])
        evidence_config = CFG.get("evidence", {})

        evidence_k = (
            int(evidence_config.get("topk_incomplete_coverage", 3))
            if best_reason == Reason.incomplete_coverage
            else int(evidence_config.get("topk_default", 2))
        )

        max_chars = int(evidence_config.get("max_chars", 220))

        evidence_answer, evidence_similarities = pick_topk_evidence(
            answer=answer,
            question=question,
            embedder=self.embedder,
            k=evidence_k,
            min_sim=evidence_min_sim,
            max_chars=max_chars
        )

        evidence_question = question[:max_chars]
        why = self._render_why(best_reason)

        rewritten_question, rewrite_note = rewrite_question(question, best_reason)

        debug_meta = {
            "forced_reason": forced_reason.value if forced_reason else None,
            "sim_qa": sim_qa,
            "kg_sim": kg_sim,
            "evasive_sim": evasive_sim,
            "kg_lex": bool(kg_lex),
            "looks_definitional": bool(looks_definitional),
            "asks_steps": bool(asks_steps),
            "is_tie": bool(is_tie),
            "tie_group_size": int(tie_group_size),
            "selected_reasons": [reason.value for reason in selected],
            "top2_final": top2_debug,
            "margin_final": float(margin_final),
            "best_entail_final": float(best_entailment_final),
            "confidence_softmax": float(confidence_softmax)
        }

        return Diagnosis(
            reason=best_reason,
            reasons=selected,
            rewritten_question=rewritten_question,
            rewrite_note=rewrite_note,
            entailment_top1=float(best_entailment_final),
            confidence_softmax=float(confidence_softmax),
            margin_top1_top2=float(margin_final),
            why=why,
            evidence_q=evidence_question,
            evidence_a=evidence_answer,
            evidence_a_sims=evidence_similarities,
            debug_entailment=entailment_map,
            debug_softmax=softmax_map,
            debug_meta=debug_meta
        )

    @staticmethod
    def _render_why(reason: Reason) -> str:
        return CFG["why_templates"].get(
            reason.value,
            "No explanation template found for this reason."
        )


def get_models_dir() -> str:
    current_dir = os.path.dirname(os.path.abspath(__file__))

    return os.getenv(
        "MODELS_DIR",
        os.path.abspath(os.path.join(current_dir, "..", "..", "models"))
    )


def print_json_result(result: dict) -> None:
    print("DIAGNOSIS_JSON:" + json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print_json_result({
            "status": "error",
            "message": "Expected arguments: question answer"
        })
        sys.exit(1)

    question_input = sys.argv[1]
    answer_input = sys.argv[2]

    if not answer_input.strip():
        print_json_result({
            "status": "error",
            "message": "Answer is empty"
        })
        sys.exit(1)

    try:
        models_dir = get_models_dir()
        diagnoser = MisunderstandingDiagnoser(models_dir)
        diagnosis = diagnoser.diagnose(question_input, answer_input)

        result = {
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

        print_json_result(result)

    except Exception as error:
        print_json_result({
            "status": "error",
            "message": str(error)
        })
        sys.exit(1)