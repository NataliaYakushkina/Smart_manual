"""Settings for the voice assistant backend. The API key comes from backend/.env (never committed)."""
import os
from pathlib import Path

BACKEND_DIR = Path(__file__).resolve().parent
REPO_DIR = BACKEND_DIR.parent


def _load_env_file(path: Path) -> None:
    """Minimal .env reader: KEY=value lines, # comments. Existing environment variables win."""
    if not path.exists():
        return
    for line in path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


_load_env_file(BACKEND_DIR / ".env")

# --- Connection to the phone / Unity editor ---
# 0.0.0.0 = reachable from the phone over Wi-Fi (use the PC's IP address in the app).
HOST = os.getenv("HOST", "0.0.0.0")
PORT = int(os.getenv("PORT", "8765"))

# --- OpenAI Realtime (speech-to-speech) ---
OPENAI_API_KEY = os.getenv("OPENAI_API_KEY")
REALTIME_URL = "wss://api.openai.com/v1/realtime"
REALTIME_MODEL = os.getenv("REALTIME_MODEL", "gpt-realtime-2.1-mini")
VOICE = os.getenv("VOICE", "marin")                         # marin / cedar recommended by OpenAI
TRANSCRIBE_MODEL = "gpt-4o-mini-transcribe"                 # only for the subtitles
LANGUAGE = "English"

# Audio: raw PCM, 16-bit signed little-endian, mono, 24 kHz (the only PCM rate Realtime accepts).
SAMPLE_RATE = 24000
MIN_SPEECH_BYTES = SAMPLE_RATE * 2 // 10                    # 100 ms; less than that is ignored

TOOL_TIMEOUT_S = 10.0

# --- Content: the same files the app uses (single source of truth) ---
DATA_DIR = REPO_DIR / "ARManual_Unity" / "Assets" / "_Project" / "Data"
PROCEDURES_DIR = DATA_DIR / "Procedures"
DEVICE_FACTS = DATA_DIR / "device_facts.md"
