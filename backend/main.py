"""Start the voice assistant backend:  python main.py"""
import asyncio
import sys

import config
from server import serve

if __name__ == "__main__":
    sys.stdout.reconfigure(errors="replace")   # Windows consoles: never crash on °, … etc.
    if not config.OPENAI_API_KEY:
        sys.exit("No OPENAI_API_KEY. Create backend/.env with the line  OPENAI_API_KEY=sk-...")
    try:
        asyncio.run(serve(typing=True))
    except KeyboardInterrupt:
        print("Stopped.")
