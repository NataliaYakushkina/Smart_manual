"""WebSocket server for the app (Unity), built like the thesis UnityConnection.

App -> backend
  {"type": "mic_start"}                          mic tapped on (stops the assistant if it is talking)
  {"type": "audio", "data": "<base64 PCM16>"}    mic audio, 24 kHz mono
  {"type": "mic_stop"}                           mic tapped off: the question is sent to the model
  {"type": "state", "text": "..."}               current procedure / step, sent on every change
  {"type": "tool_result", "call_id": "...", "result": {...}}
  {"type": "text", "text": "..."}                development only (test console): typed question

Backend -> app
  {"type": "audio", "data": "<base64 PCM16>"}    assistant voice, 24 kHz mono, streamed
  {"type": "audio_done"}
  {"type": "subtitle", "who": "user"|"assistant", "text": "...", "final": bool}
  {"type": "tool_call", "call_id": "...", "name": "...", "arguments": {...}}
  {"type": "status", "state": "listening"|"thinking"|"too_short"|"ready"}
  {"type": "error", "message": "..."}
"""
import asyncio
import json

import websockets

import config
from content import build_instructions, load_content
from realtime import RealtimeSession
from tools import build_tools


# Connected apps; the newest one receives questions typed into this window (development).
connections: list = []


class AppConnection:
    def __init__(self, ws):
        self.ws = ws
        self.pending: dict[str, asyncio.Future] = {}
        content = load_content()
        self.session = RealtimeSession(build_instructions(content), build_tools(content), self)

    async def run(self) -> None:
        print(f"[App] connected from {self.ws.remote_address}")
        connections.append(self)
        try:
            try:
                await self.session.connect()
            except Exception as e:   # wrong key, no internet, unknown model ...
                await self.on_error(f"Could not reach OpenAI Realtime: {e}")
                return
            await self.send({"type": "status", "state": "ready"})
            async for raw in self.ws:
                await self.handle(json.loads(raw))
        except websockets.ConnectionClosed:
            pass
        finally:
            await self.session.close()
            if self in connections:
                connections.remove(self)
            print("[App] disconnected")

    async def send(self, message: dict) -> None:
        try:
            await self.ws.send(json.dumps(message))
        except websockets.ConnectionClosed:
            pass

    async def handle(self, message: dict) -> None:
        kind = message.get("type")
        if kind == "mic_start":
            await self.session.start_talking()
            await self.send({"type": "status", "state": "listening"})
        elif kind == "audio":
            await self.session.append_audio(message["data"])
        elif kind == "mic_stop":
            sent = await self.session.stop_talking()
            await self.send({"type": "status", "state": "thinking" if sent else "too_short"})
        elif kind == "state":
            print(f"[State] {message['text']}")
            await self.session.add_context(f"[App state] {message['text']}")
        elif kind == "tool_result":
            future = self.pending.pop(message.get("call_id"), None)
            if future and not future.done():
                future.set_result(message.get("result"))
        elif kind == "text":
            print(f"[Text] {message['text']}")
            await self.session.send_text(message["text"])
            await self.send({"type": "status", "state": "thinking"})

    # ---------------------------------------------------- Realtime listener

    async def on_audio(self, pcm_b64: str) -> None:
        await self.send({"type": "audio", "data": pcm_b64})

    async def on_audio_done(self) -> None:
        await self.send({"type": "audio_done"})

    async def on_subtitle(self, who: str, text: str, final: bool) -> None:
        if final:
            print(f"[{who.capitalize()}] {text}")
        await self.send({"type": "subtitle", "who": who, "text": text, "final": final})

    async def on_tool_call(self, name: str, args: dict, call_id: str) -> dict:
        """Forward a tool call to the app and wait for the StepEngine's result."""
        future = asyncio.get_running_loop().create_future()
        self.pending[call_id] = future
        await self.send({"type": "tool_call", "call_id": call_id, "name": name, "arguments": args})
        try:
            return await asyncio.wait_for(future, timeout=config.TOOL_TIMEOUT_S)
        except asyncio.TimeoutError:
            self.pending.pop(call_id, None)
            return {"ok": False, "error": "The app did not answer."}

    async def on_error(self, message: str) -> None:
        print(f"[Error] {message}")
        await self.send({"type": "error", "message": message})


async def console_input() -> None:
    """Development: a line typed into this window is asked as a question in the newest app session
    (so the tool calls run in Unity). The app itself is voice-only."""
    while True:
        line = (await asyncio.to_thread(input)).strip()
        if not line:
            continue
        if not connections:
            print("(no app connected)")
            continue
        app = connections[-1]
        print(f"[Typed] {line}")
        await app.session.send_text(line)


async def serve(typing: bool = False) -> None:
    async with websockets.serve(lambda ws: AppConnection(ws).run(), config.HOST, config.PORT, max_size=None):
        print(f"Voice assistant backend on ws://{config.HOST}:{config.PORT}  (model {config.REALTIME_MODEL})")
        if typing:
            print("Type a question here and press Enter to ask it in the connected app (development).")
            await console_input()
        else:
            await asyncio.Future()
