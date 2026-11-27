# AR Smart Manual

An Android AR app that replaces the paper manual of a small digital thermo-hygrometer. Point the phone at the real device and each step appears as a 3D animation directly on top of it, while a voice assistant answers questions about the device.

▶ **Demo video and photos:** [nataliayakushkina.github.io/project.html?p=ar-smart-manual](https://nataliayakushkina.github.io/project.html?p=ar-smart-manual)

## What it does

- **Recognizes the physical device** from its CAD shape with Vuforia Model Targets, face-up and face-down.
- **Guides through two procedures:** changing the battery and switching between °C and °F.
- **Shows only what the current step is about.** The part to move is drawn in orange and animated; every other part is an invisible occluder, so the user still sees the real device. An x-ray view shows the battery and its polarity inside the closed compartment.
- **Voice assistant (push-to-talk).** Ask in your own words, e.g. "which way does the battery go?" or "what does the smiley mean?". It answers out loud and as subtitles, highlights parts and moves through the steps. It answers only from the manual's content and says so when something isn't covered.

## How it works

```
Fusion 360 (1:1 CAD model, named parts) ──FBX──► Unity
            │
            └──► Vuforia Model Target Generator ──► Model Targets (FaceUp / FaceDown)

Unity app (Android)  ⇄  WebSocket  ⇄  Python backend  ⇄  OpenAI Realtime (speech-to-speech, tool calling)
```

- **Procedures are data, not code.** Each step is described in JSON ([`Assets/_Project/Data/Procedures/`](ARManual_Unity/Assets/_Project/Data/Procedures)): which part to highlight, show or hide, which animation to play, which Model Target to use and what text to display. The `StepEngine` runs them.
- **One source of truth for the assistant.** The backend builds the model's instructions from the same procedure files and [`device_facts.md`](ARManual_Unity/Assets/_Project/Data/device_facts.md), and controls the app through the same `StepEngine` functions via tool calls (`start_procedure`, `next_step`, `highlight_part`, `point_at`, `show_xray`, …). It cannot invent steps.
- **Custom URP shaders:** `Occluder` (depth only, hides virtual content behind the real housing) and `Battery` (wraps the battery label around the CAD mesh without UVs).
- **UI Toolkit** (UXML/USS) for the step card, subtitles and buttons.
- **The OpenAI API key stays on the PC** running the backend, never on the phone.

## Tech stack

Unity 6.3 (6000.3.28f1, URP) · C# · Vuforia Engine 11.4.4 (Model Targets) · Autodesk Fusion 360 · Python (`websockets`) · OpenAI Realtime API · Android (ARM64)

## Repository structure

```
ARManual_Unity/   Unity project; own code and content in Assets/_Project/
backend/          Python voice-assistant server (OpenAI Realtime)
CAD/              Fusion 360 model, FBX export, Model Target sources
docs/             Design document and UI mockups
```

## Run it yourself

The Model Targets are made for one specific thermo-hygrometer, so tracking only works with that device in front of the camera.

**Unity app**

1. Open `ARManual_Unity` with Unity **6000.3.28f1** (with Android Build Support).
2. Add your own Vuforia license key; the key is not part of this repository. Create a free Basic key at [developer.vuforia.com](https://developer.vuforia.com) → License Manager, then in Unity select the **ARCamera** → **Open Vuforia Engine configuration** → **App License Key**.
3. Open `Assets/_Project/Scenes/SampleScene.unity` and set the server URL on the `AssistantClient` component: `ws://localhost:8765` in the Editor, `ws://<your PC's IP>:8765` on the phone (phone and PC in the same Wi-Fi).
4. Build for Android (ARM64).

**Voice assistant backend**

```bash
cd backend
pip install -r requirements.txt
copy .env.example .env        # then put your OpenAI API key into .env
python main.py
```

More details in [backend/README.md](backend/README.md).

## Limitations

- Android only, English only.
- Vuforia Basic plan: the camera view shows a Vuforia watermark.
- The user confirms each step ("Done" button or by voice); the app does not check with the camera that a step was really done.

## Author

**Natalia Yakushkina**: CAD model, AR app and AI assistant. Portfolio: [nataliayakushkina.github.io](https://nataliayakushkina.github.io)
