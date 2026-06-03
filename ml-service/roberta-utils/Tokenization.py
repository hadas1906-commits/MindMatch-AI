import json
import re


class MyRobertaTokenizer:
    def __init__(self, vocab_file, merges_file):
        with open(vocab_file, "r", encoding="utf-8") as file:
            self.vocab = json.load(file)

        self.inverse_vocab = {
            value: key
            for key, value in self.vocab.items()
        }

        with open(merges_file, "r", encoding="latin-1") as file:
            lines = file.read().splitlines()
            merges = lines[1:]

        self.bpe_ranks = dict(
            zip(
                [tuple(merge.split()) for merge in merges],
                range(len(merges))
            )
        )

        self.cache = {}
        self.byte_encoder = self.bytes_to_unicode()

    def bytes_to_unicode(self):
        byte_values = (
            list(range(ord("!"), ord("~") + 1)) +
            list(range(ord("¡"), ord("¬") + 1)) +
            list(range(ord("®"), ord("ÿ") + 1))
        )

        unicode_values = byte_values[:]
        offset = 0

        for byte in range(256):
            if byte not in byte_values:
                byte_values.append(byte)
                unicode_values.append(256 + offset)
                offset += 1

        return {
            byte: chr(unicode_value)
            for byte, unicode_value in zip(byte_values, unicode_values)
        }

    def bpe(self, token):
        if token in self.cache:
            return self.cache[token]

        word = tuple(token)
        pairs = self.get_pairs(word)

        if not pairs:
            return token

        while True:
            bigram = min(
                pairs,
                key=lambda pair: self.bpe_ranks.get(pair, float("inf"))
            )

            if bigram not in self.bpe_ranks:
                break

            first, second = bigram
            new_word = []
            index = 0

            while index < len(word):
                try:
                    next_index = word.index(first, index)
                    new_word.extend(word[index:next_index])
                    index = next_index
                except ValueError:
                    new_word.extend(word[index:])
                    break

                if (
                    index < len(word) - 1
                    and word[index] == first
                    and word[index + 1] == second
                ):
                    new_word.append(first + second)
                    index += 2
                else:
                    new_word.append(word[index])
                    index += 1

            word = tuple(new_word)

            if len(word) == 1:
                break

            pairs = self.get_pairs(word)

        result = " ".join(word)
        self.cache[token] = result

        return result

    def get_pairs(self, word):
        return set(zip(word, word[1:]))

    def encode(self, text):
        token_ids = [0]

        text = " " + text.strip()
        parts = re.split(r"(<maskText>)", text)

        for part in parts:
            if part == "<maskText>":
                token_ids.append(50264)
            elif part:
                token_bytes = part.encode("utf-8")
                encoded_part = "".join(
                    self.byte_encoder[byte]
                    for byte in token_bytes
                )

                for sub_token in self.bpe(encoded_part).split():
                    if sub_token in self.vocab:
                        token_ids.append(self.vocab[sub_token])
                    else:
                        token_ids.append(3)

        token_ids.append(2)

        return token_ids