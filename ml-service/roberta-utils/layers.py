import math

import torch
import torch.nn as nn


class MultiHeadAttention(nn.Module):
    def __init__(self, d_model, num_heads):
        super().__init__()

        if d_model % num_heads != 0:
            raise ValueError("d_model must be divisible by num_heads")

        self.d_model = d_model
        self.num_heads = num_heads
        self.d_k = d_model // num_heads

        self.W_q = nn.Linear(d_model, d_model)
        self.W_k = nn.Linear(d_model, d_model)
        self.W_v = nn.Linear(d_model, d_model)
        self.W_o = nn.Linear(d_model, d_model)

    def scaled_dot_product_attention(self, q, k, v, mask=None):
        attention_scores = torch.matmul(q, k.transpose(-1, -2)) / math.sqrt(self.d_k)

        if mask is not None:
            attention_scores = attention_scores.masked_fill(mask == 0, -1e9)

        attention_probs = torch.softmax(attention_scores, dim=-1)
        output = torch.matmul(attention_probs, v)

        return output

    def split_heads(self, x):
        batch_size, sequence_length, _ = x.size()

        return (
            x.view(batch_size, sequence_length, self.num_heads, self.d_k)
            .transpose(1, 2)
        )

    def combine_heads(self, x):
        batch_size, _, sequence_length, _ = x.size()

        return (
            x.transpose(1, 2)
            .contiguous()
            .view(batch_size, sequence_length, self.d_model)
        )

    def forward(self, q, k, v, mask=None):
        q = self.split_heads(self.W_q(q))
        k = self.split_heads(self.W_k(k))
        v = self.split_heads(self.W_v(v))

        attention_output = self.scaled_dot_product_attention(q, k, v, mask)
        output = self.combine_heads(attention_output)

        return self.W_o(output)


class PositionWiseFeedForward(nn.Module):
    def __init__(self, d_model, d_ff):
        super().__init__()

        self.fc1 = nn.Linear(d_model, d_ff)
        self.fc2 = nn.Linear(d_ff, d_model)
        self.activation = nn.GELU()

    def forward(self, x):
        return self.fc2(self.activation(self.fc1(x)))


class PositionalEncoding(nn.Module):
    def __init__(self, d_model, max_seq_length=514):
        super().__init__()

        self.pe = nn.Embedding(max_seq_length, d_model)

    def forward(self, x):
        sequence_length = x.size(1)
        positions = torch.arange(
            2,
            sequence_length + 2,
            device=x.device
        ).unsqueeze(0)

        return x + self.pe(positions)


class EncoderLayer(nn.Module):
    def __init__(self, d_model, num_heads, d_ff, dropout):
        super().__init__()

        self.self_attn = MultiHeadAttention(d_model, num_heads)
        self.feed_forward = PositionWiseFeedForward(d_model, d_ff)

        self.norm1 = nn.LayerNorm(d_model)
        self.norm2 = nn.LayerNorm(d_model)

        self.dropout = nn.Dropout(dropout)

    def forward(self, x, mask):
        attention_output = self.self_attn(x, x, x, mask)
        x = self.norm1(x + self.dropout(attention_output))

        feed_forward_output = self.feed_forward(x)
        x = self.norm2(x + self.dropout(feed_forward_output))

        return x