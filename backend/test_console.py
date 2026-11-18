"""Development tool: pretends to be the app, so the backend can be tested without Unity or a mic.

    python main.py            (window 1)
    python test_console.py    (window 2)

Type a question and press Enter. Tool calls run on a small simulated StepEngine (same procedures as
the app) and are answered the way the app answers them: {"ok": ..., "state": "..."}.
  /quit           exit
"""
import asyncio
import json
import sys

import websockets

import config
from content import load_content


class FakeEngine:
    """Mimics the StepEngine's guide logic and its tool results."""

    def __init__(self):
        self.procedures = {p["id"]: p for p in load_content().procedures}
        self.current = None
        self.index = -1

    def state(self) -> str:
        if self.current is None:
            return "No guide is running."
        steps = self.current["steps"]
        return f'"{self.current["title"]}", step {self.index + 1} of {len(steps)}: {steps[self.index]["text"]}'

    def run(self, name: str, args: dict) -> dict:
        def fail(error: str) -> dict:
            return {"ok": False, "error": error, "state": self.state()}

        if name == "start_procedure":
            p = self.procedures.get(args.get("procedure_id"))
            if p is None:
                return fail("Unknown procedure.")
            self.current, self.index = p, 0
        elif name in ("next_step", "previous_step", "go_to_step", "repeat_step") and self.current is None:
            return fail("No guide is running.")
        elif name == "next_step":
            if self.index < len(self.current["steps"]) - 1:
                self.index += 1
            else:
                title = self.current["title"]
                self.current, self.index = None, -1
                return {"ok": True, "state": f'Guide "{title}" finished. No guide is running.'}
        elif name == "previous_step":
            if self.index == 0:
                return fail("Already at step 1.")
            self.index -= 1
        elif name == "go_to_step":
            n = int(args.get("step", 0))
            if not 1 <= n <= len(self.current["steps"]):
                return fail("No such step.")
            self.index = n - 1
        elif name == "stop_procedure":
            self.current, self.index = None, -1
        # repeat_step, highlight_part, point_at, show_xray, clear_highlights: nothing to simulate
        return {"ok": True, "state": self.state()}


async def main() -> None:
    url = f"ws://localhost:{config.PORT}"
    engine = FakeEngine()
    async with websockets.connect(url, max_size=None) as ws:
        print(f"Connected to {url}. Type a question (or /quit).")
        audio_bytes = 0

        async def receive() -> None:
            nonlocal audio_bytes
            async for raw in ws:
                msg = json.loads(raw)
                kind = msg.get("type")
                if kind == "audio":
                    audio_bytes += len(msg["data"]) * 3 // 4
                elif kind == "audio_done":
                    print(f"   (voice: {audio_bytes / (config.SAMPLE_RATE * 2):.1f} s of audio)")
                    audio_bytes = 0
                elif kind == "subtitle" and msg["final"]:
                    print(f"   {msg['who'].capitalize()}: {msg['text']}")
                elif kind == "tool_call":
                    result = engine.run(msg["name"], msg["arguments"])
                    print(f"   [tool] {msg['name']}({msg['arguments']})  -> {result}")
                    await ws.send(json.dumps({"type": "tool_result", "call_id": msg["call_id"], "result": result}))
                elif kind == "status" and msg["state"] == "ready":
                    print("   (assistant ready)")
                elif kind == "error":
                    print(f"   ERROR: {msg['message']}")

        reader = asyncio.create_task(receive())
        while True:
            line = (await asyncio.to_thread(input, "> ")).strip()
            if not line:
                continue
            if line == "/quit":
                break
            await ws.send(json.dumps({"type": "text", "text": line}))
        reader.cancel()


if __name__ == "__main__":
    sys.stdout.reconfigure(errors="replace")   # Windows consoles: never crash on °, … etc.
    asyncio.run(main())
