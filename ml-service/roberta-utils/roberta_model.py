import torch
import torch.nn as nn
from transformers import RobertaForMaskedLM as HFRoberta

from layers import EncoderLayer, PositionalEncoding


class Transformer(nn.Module):
    def __init__(
        self,
        vocab_size=50265,
        d_model=768,
        num_heads=12,
        num_layers=12,
        d_ff=3072,
        max_len=514,
        dropout=0.1
    ):
        super().__init__()

        self.embedding = nn.Embedding(vocab_size, d_model)
        self.pos = PositionalEncoding(d_model, max_len)
        self.emb_norm = nn.LayerNorm(d_model)

        self.layers = nn.ModuleList([
            EncoderLayer(d_model, num_heads, d_ff, dropout)
            for _ in range(num_layers)
        ])

    def generate_mask(self, src):
        return (src != 1).unsqueeze(1).unsqueeze(2)

    def forward(self, src):
        mask = self.generate_mask(src)

        x = self.embedding(src)
        x = self.pos(x)
        x = self.emb_norm(x)

        for layer in self.layers:
            x = layer(x, mask)

        return x


class RobertaLMHead(nn.Module):
    def __init__(self, d_model, vocab_size):
        super().__init__()

        self.dense = nn.Linear(d_model, d_model)
        self.gelu = nn.GELU()
        self.layer_norm = nn.LayerNorm(d_model, eps=1e-5)
        self.decoder = nn.Linear(d_model, vocab_size, bias=False)
        self.bias = nn.Parameter(torch.zeros(vocab_size))

    def forward(self, x):
        x = self.dense(x)
        x = self.gelu(x)
        x = self.layer_norm(x)

        return self.decoder(x) + self.bias


class RobertaForMaskedLM(nn.Module):
    def __init__(self, transformer_model, vocab_size, d_model):
        super().__init__()

        self.transformer = transformer_model
        self.lm_head = RobertaLMHead(d_model, vocab_size)

    def forward(self, src):
        hidden_states = self.transformer(src)
        logits = self.lm_head(hidden_states)

        return logits


def load_roberta_weights(my_model, path):
    hf_model = HFRoberta.from_pretrained(path)

    hf_state_dict = hf_model.state_dict()
    my_state_dict = my_model.state_dict()

    is_wrapper = hasattr(my_model, "transformer")
    prefix = "transformer." if is_wrapper else ""

    mapping = {
        "roberta.embeddings.word_embeddings.weight": f"{prefix}embedding.weight",
        "roberta.embeddings.position_embeddings.weight": f"{prefix}pos.pe.weight",
        "roberta.embeddings.LayerNorm.weight": f"{prefix}emb_norm.weight",
        "roberta.embeddings.LayerNorm.bias": f"{prefix}emb_norm.bias",
    }

    for index in range(12):
        hf_prefix = f"roberta.encoder.layer.{index}"
        my_prefix = f"{prefix}layers.{index}"

        mapping.update({
            f"{hf_prefix}.attention.self.query.weight": f"{my_prefix}.self_attn.W_q.weight",
            f"{hf_prefix}.attention.self.query.bias": f"{my_prefix}.self_attn.W_q.bias",
            f"{hf_prefix}.attention.self.key.weight": f"{my_prefix}.self_attn.W_k.weight",
            f"{hf_prefix}.attention.self.key.bias": f"{my_prefix}.self_attn.W_k.bias",
            f"{hf_prefix}.attention.self.value.weight": f"{my_prefix}.self_attn.W_v.weight",
            f"{hf_prefix}.attention.self.value.bias": f"{my_prefix}.self_attn.W_v.bias",
            f"{hf_prefix}.attention.output.dense.weight": f"{my_prefix}.self_attn.W_o.weight",
            f"{hf_prefix}.attention.output.dense.bias": f"{my_prefix}.self_attn.W_o.bias",
            f"{hf_prefix}.intermediate.dense.weight": f"{my_prefix}.feed_forward.fc1.weight",
            f"{hf_prefix}.intermediate.dense.bias": f"{my_prefix}.feed_forward.fc1.bias",
            f"{hf_prefix}.output.dense.weight": f"{my_prefix}.feed_forward.fc2.weight",
            f"{hf_prefix}.output.dense.bias": f"{my_prefix}.feed_forward.fc2.bias",
            f"{hf_prefix}.attention.output.LayerNorm.weight": f"{my_prefix}.norm1.weight",
            f"{hf_prefix}.attention.output.LayerNorm.bias": f"{my_prefix}.norm1.bias",
            f"{hf_prefix}.output.LayerNorm.weight": f"{my_prefix}.norm2.weight",
            f"{hf_prefix}.output.LayerNorm.bias": f"{my_prefix}.norm2.bias",
        })

    if is_wrapper:
        mapping.update({
            "lm_head.dense.weight": "lm_head.dense.weight",
            "lm_head.dense.bias": "lm_head.dense.bias",
            "lm_head.layer_norm.weight": "lm_head.layer_norm.weight",
            "lm_head.layer_norm.bias": "lm_head.layer_norm.bias",
            "lm_head.bias": "lm_head.bias"
        })

    for hf_name, my_name in mapping.items():
        if hf_name in hf_state_dict and my_name in my_state_dict:
            my_state_dict[my_name].copy_(hf_state_dict[hf_name])

    my_model.load_state_dict(my_state_dict, strict=False)

    if is_wrapper:
        my_model.lm_head.decoder.weight = my_model.transformer.embedding.weight