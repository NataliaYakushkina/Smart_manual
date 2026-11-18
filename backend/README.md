# Voice assistant backend

Python server between the AR app and **OpenAI Realtime** (speech-to-speech). The user taps the mic, speaks,
taps again, and the assistant answers with voice. It can drive the app through tool calls
(`start_procedure`, `next_step`, `highlight_part`, `point_at`, `show_xray`, …), which the app runs on its
`StepEngine`. The API key stays on the PC.

Based on the bachelor-thesis backend (`python_connector`): same WebSocket server pattern and tool-call loop,
with the chat + TTS model replaced by one Realtime session.

```
App (Unity)  ⇄  ws://<PC>:8765  ⇄  this backend  ⇄  wss://api.openai.com/v1/realtime (gpt-realtime-2.1-mini)
```

## What the assistant knows

Built at connection time from the Unity project, so the app and the assistant always match:

- `ARManual_Unity/Assets/_Project/Data/Procedures/*.json`: the guides and their steps
- `ARManual_Unity/Assets/_Project/Data/device_facts.md`: everything it may say about the device

It answers only from these files and says so when something isn't covered.

## Run

```bash
cd backend
pip install -r requirements.txt
copy .env.example .env        # then put your OpenAI key into .env
python main.py
```

## Test without the app

In a second terminal, while `main.py` runs:

```bash
python test_console.py
```

Type questions; answers are printed as text, tool calls are printed and answered with a simulated "ok".
`/state Change the battery, step 2 of 6: Slide the battery lid straight down and off.` tells it where the user is.

## Files

| File | Purpose |
|---|---|
| `main.py` | start the server |
| `server.py` | connection to the app; message protocol (see the docstring) |
| `realtime.py` | one OpenAI Realtime session: push-to-talk audio, subtitles, tool calls |
| `content.py` | loads procedures + device facts, builds the instructions |
| `tools.py` | tool definitions (one per StepEngine function) |
| `config.py` | model, voice, port, paths; reads `.env` |
| `test_console.py` | development only: pretend to be the app |
