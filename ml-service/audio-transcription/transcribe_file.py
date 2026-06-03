import os
import sys
import io
import json
from faster_whisper import WhisperModel

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

ffmpeg_path = os.getenv("FFMPEG_PATH", "")
if ffmpeg_path:
    os.environ["PATH"] += os.pathsep + ffmpeg_path

model_path = os.getenv("WHISPER_MODEL_PATH", "models/whisper-small")
model = WhisperModel(
    model_path,
    device="cpu",
    compute_type="int8",
    local_files_only=True
)

def transcribe_file(audio_path):
    segments, _ = model.transcribe(
        audio_path,
        beam_size=5,
        language="en",
        vad_filter=True,
        no_speech_threshold=0.6,
        condition_on_previous_text=False
    )

    text_parts = []

    for segment in segments:
        if segment.text and segment.text.strip():
            text_parts.append(segment.text.strip())

    return " ".join(text_parts).strip()

if __name__ == "__main__":
    try:
        if len(sys.argv) < 2:
            print(json.dumps({
                "status": "error",
                "message": "Missing audio file path."
            }, ensure_ascii=False))
            sys.exit(1)

        audio_path = sys.argv[1]

        if not os.path.exists(audio_path):
            print(json.dumps({
                "status": "error",
                "message": "Audio file was not found."
            }, ensure_ascii=False))
            sys.exit(1)

        text = transcribe_file(audio_path)

        print(json.dumps({
            "status": "success",
            "text": text
        }, ensure_ascii=False))

    except Exception as e:
        print(json.dumps({
            "status": "error",
            "message": str(e)
        }, ensure_ascii=False))
        sys.exit(1)