"""One OpenAI Realtime session (speech-to-speech) per connected app.

Push-to-talk: automatic turn detection is off. The app sends audio while the mic is on,
then "commit": the model hears the whole question and answers with voice. Tool calls are
handed to the app (Unity) and their results go back to the model, like in the thesis backend.
"""
import asyncio
import json
from typing import Awaitable, Callable, Protocol

import websockets

import config


class Listener(Protocol):
    """What the session reports back (implemented by the app connection in server.py)."""
    async def on_audio(self, pcm_b64: str) -> None: ...
    async def on_audio_done(self) -> None: ...
    async def on_subtitle(self, who: str, text: str, final: bool) -> None: ...
    async def on_tool_call(self, name: str, args: dict, call_id: str) -> dict: ...
    async def on_error(self, message: str) -> None: ...


class RealtimeSession:
    def __init__(self, instructions: str, tools: list, listener: Listener):
        self.instructions = instructions
        self.tools = tools
        self.listener = listener
        self.ws = None
        self.reader: asyncio.Task | None = None
        self.buffered_bytes = 0
        self.responding = False

    # ------------------------------------------------------------- lifecycle

    async def connect(self) -> None:
        url = f"{config.REALTIME_URL}?model={config.REALTIME_MODEL}"
        self.ws = await websockets.connect(
            url,
            additional_headers={"Authorization": f"Bearer {config.OPENAI_API_KEY}"},
            max_size=None,
        )
        await self.send({
            "type": "session.update",
            "session": {
                "type": "realtime",
                "output_modalities": ["audio"],
                "instructions": self.instructions,
                "tools": self.tools,
                "tool_choice": "auto",
                "audio": {
                    "input": {
                        "format": {"type": "audio/pcm", "rate": config.SAMPLE_RATE},
                        "transcription": {"model": config.TRANSCRIBE_MODEL},
                        "turn_detection": None,          # push-to-talk: the app decides when a turn ends
                    },
                    "output": {
                        "format": {"type": "audio/pcm", "rate": config.SAMPLE_RATE},
                        "voice": config.VOICE,
                    },
                },
            },
        })
        self.reader = asyncio.create_task(self._read_events())
        print(f"[Realtime] connected ({config.REALTIME_MODEL}, voice {config.VOICE})")

    async def close(self) -> None:
        if self.reader:
            self.reader.cancel()
        if self.ws:
            await self.ws.close()
            self.ws = None

    async def send(self, event: dict) -> None:
        await self.ws.send(json.dumps(event))

    # ------------------------------------------------------------ user input

    async def start_talking(self) -> None:
        """Mic on: stop the assistant if it is still speaking and start a fresh question."""
        if self.responding:
            await self.send({"type": "response.cancel"})
        await self.send({"type": "input_audio_buffer.clear"})
        self.buffered_bytes = 0

    async def append_audio(self, pcm_b64: str) -> None:
        self.buffered_bytes += len(pcm_b64) * 3 // 4
        await self.send({"type": "input_audio_buffer.append", "audio": pcm_b64})

    async def stop_talking(self) -> bool:
        """Mic off: send the question. Returns False if it was too short to count."""
        if self.buffered_bytes < config.MIN_SPEECH_BYTES:
            await self.send({"type": "input_audio_buffer.clear"})
            return False
        await self.send({"type": "input_audio_buffer.commit"})
        await self.send({"type": "response.create"})
        self.buffered_bytes = 0
        return True

    async def send_text(self, text: str) -> None:
        """Development only (test console): a typed question instead of speech."""
        await self.send({
            "type": "conversation.item.create",
            "item": {"type": "message", "role": "user", "content": [{"type": "input_text", "text": text}]},
        })
        await self.send({"type": "response.create"})

    async def add_context(self, note: str) -> None:
        """A small state update (e.g. the current step) the model should know; no answer is triggered."""
        await self.send({
            "type": "conversation.item.create",
            "item": {"type": "message", "role": "system", "content": [{"type": "input_text", "text": note}]},
        })

    # ---------------------------------------------------------- model output

    async def _read_events(self) -> None:
        try:
            async for raw in self.ws:
                await self._handle(json.loads(raw))
        except asyncio.CancelledError:
            pass
        except websockets.ConnectionClosed as e:
            await self.listener.on_error(f"Realtime connection closed: {e}")

    async def _handle(self, event: dict) -> None:
        kind = event.get("type", "")

        if kind == "response.created":
            self.responding = True
        elif kind == "response.output_audio.delta":
            await self.listener.on_audio(event["delta"])
        elif kind == "response.output_audio.done":
            await self.listener.on_audio_done()
        elif kind == "response.output_audio_transcript.delta":
            await self.listener.on_subtitle("assistant", event.get("delta", ""), False)
        elif kind == "response.output_audio_transcript.done":
            await self.listener.on_subtitle("assistant", event.get("transcript", ""), True)
        elif kind == "conversation.item.input_audio_transcription.completed":
            await self.listener.on_subtitle("user", event.get("transcript", "").strip(), True)
        elif kind == "response.done":
            self.responding = False
            # Tool calls are handled in a task so this reader keeps receiving events meanwhile.
            asyncio.create_task(self._run_tool_calls(event.get("response", {})))
        elif kind == "error":
            err = event.get("error", {})
            await self.listener.on_error(f"{err.get('code')}: {err.get('message')}")
        elif kind == "session.updated":
            print("[Realtime] session configured")

    async def _run_tool_calls(self, response: dict) -> None:
        calls = [o for o in response.get("output", []) if o.get("type") == "function_call"]
        if not calls:
            return
        for call in calls:
            try:
                args = json.loads(call.get("arguments") or "{}")
            except json.JSONDecodeError:
                args = {}
            print(f"  [Tool] {call['name']}({args})")
            result = await self.listener.on_tool_call(call["name"], args, call["call_id"])
            await self.send({
                "type": "conversation.item.create",
                "item": {"type": "function_call_output", "call_id": call["call_id"], "output": json.dumps(result)},
            })
        # One new response after all results, so the model can say what it did.
        await self.send({"type": "response.create"})
