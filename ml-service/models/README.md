# Model Weights

This directory is reserved for local AI/NLP model files used by the Python FastAPI model server.

The actual trained model weights are not included in this repository because of file size limitations.

To run the full AI pipeline locally, place the required model files under this directory according to the structure below.

```text
models/
├── my-roberta/
│   ├── vocab.json
│   ├── merges.txt
│   ├── layers.py
│   ├── roberta_model.py
│   └── Tokenization.py
├── roberta-base/
├── relevance-check/
│   └── relevance_model_v5_logic.pt
├── personality-traits/
│   └── ocean_model_gpu_e5.pt
├── practical-ability/
│   └── abilities_base_model_final.pt
├── thinking-quality/
│   └── thinking_quality_v1_classifier.pt
├── experience/
│   └── experience_custom_model.pt
├── routing-classifier/
│   └── router_model.pkl
├── job-question-matcher/
│   └── job_question_matcher_v2.pt
├── roberta-large-mnli/
├── all-MiniLM-L6-v2/
└── whisper-small/
```

## Notes

The `my-roberta` folder contains the custom RoBERTa implementation used by several diagnostic models.

Large model weights and pretrained model folders should be stored locally and should not be committed to GitHub.

You can also override model locations using environment variables, for example:

```bash
MODELS_DIR=/path/to/models
MY_ROBERTA_CODE=/path/to/my-roberta
RELEVANCE_MODEL_PATH=/path/to/relevance_model_v5_logic.pt
PERSONALITY_MODEL_PATH=/path/to/ocean_model_gpu_e5.pt
PRACTICAL_ABILITY_MODEL_PATH=/path/to/abilities_base_model_final.pt
THINKING_QUALITY_MODEL_PATH=/path/to/thinking_quality_v1_classifier.pt
EXPERIENCE_MODEL_PATH=/path/to/experience_custom_model.pt
ROUTING_MODEL_PATH=/path/to/router_model.pkl
JOB_QUESTION_MATCHER_MODEL_PATH=/path/to/job_question_matcher_v2.pt
WHISPER_MODEL_PATH=/path/to/whisper-small
FFMPEG_PATH=/path/to/ffmpeg/bin
```
