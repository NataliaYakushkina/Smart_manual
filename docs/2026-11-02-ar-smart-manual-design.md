# AR Smart Manual: Design

*Date: 2026-11-02 · Status: draft, waiting for Natalia's approval*

An Android AR app that replaces the paper instructions of a thermometer (thermo-hygrometer). The user points the phone at the real device and gets the steps as 3D animations sitting directly on top of it, plus a voice AI assistant that answers questions and controls the scene. The first and only procedure for now is **changing the batteries**.

Mockups: [mockups/app-flow.html](mockups/app-flow.html) (open in a browser).

---

## 1. Intent and the why

- **Portfolio project for the job search.** It should show employers in industrial XR / digital twins and human-AI interaction (e.g. Miele's AR assembly-assistant internship in Bielefeld) that Natalia can take a real product **from CAD to a working AR experience**.
- It deliberately builds on her existing experience:
  - **Fusion CAD:** she modelled the thermometer herself, so she owns the whole pipeline (CAD → export → Unity → AR).
  - **Unity / C# / AR Foundation:** her main stack.
  - **Bachelor thesis:** an LLM assistant with a semantic scene graph and tool calling, connected to Unity via a Python backend over WebSocket. The same approach is reused here, with the device's parts as the "scene".
- **A demo video** of the app is a main deliverable. It must be understandable **without sound**, which is why everything the assistant and the user say appears as subtitles.

## 2. Goals

1. **CAD → AR pipeline:** the 1:1 Fusion model of the thermometer, split into named components, imported into Unity.
2. **Overlay on the real device:** the app recognises the physical thermometer from its CAD shape and draws highlights, arrows and "ghost" animations exactly on top of it.
3. **Procedures as data:** steps are described in a data file (which part to highlight, which animation, what text), not hard-coded. Battery change is the first procedure; more (wall mounting, °C/°F switch) can be added later without new code.
4. **Voice AI assistant:** the user asks in their own words; the assistant answers out loud and as a subtitle, and highlights parts / jumps to steps via tool calls.
5. **Demo video** recorded with Android's screen recorder.

## 3. Non-goals (for now)

- Live sensor data / MQTT / digital-twin link (possible later extension).
- User study (possible later extension, see §12).
- iPhone, AR glasses or VR headsets. Android phone only.
- Languages other than English.
- Automatic camera check that a step was really done. The user confirms with "Done" (button or voice).
- Other devices than this thermometer.

## 4. Anti-goals (would be a failure even if it technically works)

- **The assistant invents instructions.** It must answer only from the manual's content (parts, steps, battery type). If it doesn't know, it says so.
- **The overlay floats visibly next to the device** or jumps around so much that it confuses more than a paper manual.
- **The video can't be understood with the sound off.**

## 5. Constraints

| Area | Decision |
|---|---|
| Device to track | Natalia's physical thermometer (she has it) |
| CAD | Autodesk Fusion model by Natalia; she can change or extend it |
| Engine | Unity (C#) |
| Platform | Android phone |
| Tracking | **Vuforia Engine – Model Targets** (AR Foundation/ARCore can't track 3D objects on Android). Use Vuforia's free development plan; check current licence terms when signing up |
| Language | English (speech and subtitles) |

## 6. Decisions made in the conversation

Marked by who made the call.

- **Scope = ideas 1–3** (CAD pipeline, procedures as data, AI assistant). *Natalia.*
- **Animations on top of the real device, with a virtual copy as backup** when tracking is lost. *Claude's recommendation; Natalia continued on that basis.*
- **Battery steps are done with the device lying face-down on the table**, because a still object tracks much more reliably than one in the hand. *Claude's recommendation.*
- **The user talks to the assistant; everything is also shown as subtitles** (for the soundless video). *Natalia.*
- **English.** *Natalia.*
- **Screen layout A: "Full camera view"**: small step card on top, subtitles and buttons floating at the bottom. *Natalia.*
- **Simple animations are made in Unity**, not Blender (straight-line moves: lid slides, batteries lift/drop). *Agreed.*
- **Natalia builds the app herself** (with Claude assisting), since the portfolio claim is that *she* can take a product from CAD to AR. The plan should therefore have clear checkpoints and include learning Vuforia. *Claude's call.*
- **The thesis backend code (public repo `Spatial_awareness_code`) is the starting point** for the Python backend: its WebSocket + tool-calling parts are reused and trimmed, not rewritten. *Claude's call.*
- **"Go" criteria for the tracking test:** with the device face-down on the table, the outline stays on the real device for 30 s while the phone moves slowly, with no visible drift beyond ~3–5 mm; it re-locks within ~2 s after the device leaves and re-enters the view. *Claude's call.*
- **Hybrid tracking is allowed** as an intermediate option: Model Target for the start and the final check, placemat for the face-down steps. *Claude's call.*

## 7. CAD model requirements

**Size:** 1:1 with the real device. The **outer shape** (outline, thickness, display window, edges) should match within about **1 mm**, because tracking depends on it. Inside details only need to look right.

**Separate, clearly named components** (names carry over into Unity and are used by the assistant):

| Component | Needed for | Notes |
|---|---|---|
| `Housing` | tracking, occlusion | battery compartment modelled as a real **hollow cavity** |
| `BatteryLid` | battery change | include the snap/latch tab if the user must press it |
| `Battery_1`, `Battery_2` | battery change | one component each, visible **+** end |
| `BatterySpring` / contacts | polarity hint | optional; marks the − side |
| `Display` | later procedures, AI questions | |
| `UnitButton` (°F/°C) | later procedure | |
| Wall-mount keyhole | later procedure | can stay part of `Housing`; marker point added in Unity |

Export from Fusion as FBX (or OBJ). Check scale in Unity (Fusion = mm, Unity = m). Pivots matter only for rotating parts.

## 8. How the AR content looks

- **Highlight:** a glowing, semi-transparent copy of a CAD part drawn exactly over the real part, so the real part appears to light up (pulse).
- **Ghost animation:** a transparent, cyan-edged (fresnel) copy of a part shows the motion, e.g. the lid sliding off, looping 2–3×. The user then copies it with the real part.
- **Arrows** for the direction of motion.
- **Occluder:** an invisible copy of the housing hides virtual content behind the real device, so the overlay looks attached.
- **Cavity + x-ray:** a dark virtual cavity where the lid is, with ghost batteries inside and big **+ / −** labels, shown before and after opening.
- **Backup virtual copy:** when tracking is lost, a full 3D copy of the device is shown with a yellow banner ("Lost the device. Showing a 3D copy…") and keeps playing the current step.

Materials in URP Shader Graph (ghost/fresnel, depth-only occluder, dark cavity).

## 9. The battery-change procedure (draft)

*Steps are a first draft; Natalia should check them against the real device (latch? battery type and count?).*

1. Place the thermometer face-down on the table.
2. Slide the battery lid off (lid glows, ghost lid slides, arrow).
3. Remove the old batteries (ghost batteries lift out).
4. Insert new batteries with the right polarity (ghost batteries drop in, + / − labels, spring highlighted).
5. Slide the lid back on until it clicks.
6. Turn the device over and press the °F/°C button: the display should change. *(An e-paper display keeps its last image without power, so just looking at it proves nothing. Claude's call.)*

The user moves on by tapping **Done ✓** or saying "done" / "next". **↺ Repeat** replays the step.

## 10. The AI assistant

**Interaction**
- Big mic button at the bottom centre (tap to talk). Short voice commands "next", "done", "repeat" also work.
- The user's words appear immediately as a subtitle (**yellow**, "You: …").
- The answer is spoken (text-to-speech) **and** shown as a subtitle (**white**, "Assistant: …"), high contrast, long enough to read.

**What the assistant knows and does**
- Context: the device's part list and the procedure steps from the data file, plus a short **device facts file** written by Natalia (battery type and count, display icons such as the low-battery symbol, °F/°C button, wall mounting). If a manufacturer paper manual exists, it is the source for that file. *Claude's call.*
- A small set of test questions checks that it refuses what isn't in the manual (e.g. "can I use rechargeable batteries?" when the facts file doesn't say).
- Tools it can call (first set): `highlight_part(name)`, `go_to_step(n)`, `replay_animation()`, `show_xray()`.
- It answers **only** from this content (see anti-goals).

**Architecture (reused from the thesis)**
```
Unity app (Android) ⇄ WebSocket ⇄ Python backend ⇄ LLM API
                                       ├─ speech-to-text
                                       └─ text-to-speech
```
The backend keeps the API key off the phone.

## 11. Screens (layout A)

See [mockups/app-flow.html](mockups/app-flow.html).

1. **Find your device:** the CAD outline (Vuforia guide view) shows how to aim the phone. The assistant greets the user.
2. **Step screen:** step card on top ("Step 2 of 6", one line of text, progress bar). Device overlay in the middle. Subtitles above the bottom bar. Bottom bar: ↺ Repeat · 🎤 · Done ✓.
3. **Voice question:** mic turns red while listening; "You:" and "Assistant:" subtitles; the relevant parts are highlighted.
4. **Tracking lost:** yellow banner + virtual copy continues the step.

## 12. Risks and plan B

- **Tracking a small, smooth, box-like object is the hardest case for Model Targets.** It may work well or not. **Build step 1 is therefore a tracking test**: CAD → Model Target Generator → test app showing only an outline → check face-up and face-down.
- **Plan B:** a printed **placemat** with the device's outline and a pattern. The user places the thermometer in the outline and the app tracks the mat as an image target. To keep one SDK, the mat can be a Vuforia Image Target. Less impressive, but very stable, and it fits the face-down step.
- **Later extensions** (not in scope now): live sensor data over MQTT (battery low → app offers the battery guide), user study comparing paper vs. AR vs. AR + AI (task time, errors, SUS).

## 13. Suggested build order

1. Tracking test (go / plan B decision).
2. CAD prepared and exported; parts imported and checked in Unity.
3. Procedure data file + step engine + battery-change animations and materials.
4. UI layout A (step card, buttons, subtitles).
5. Backup virtual copy when tracking is lost.
6. Python backend + LLM with tool calls (text only first).
7. Voice: speech-to-text, text-to-speech, subtitles.
8. Polish and record the demo video.

## 14. Open questions

- Does the CAD model match the real device within ~1 mm on the outside? (needed for tracking)
- Battery type and count, and how the lid opens (straight slide? latch to press?).
- Which Android phone model? (check Vuforia/ARCore support)
- LLM provider: keep the provider from the thesis code, or switch.
- Speech-to-text and text-to-speech: cloud services or Android's built-in ones.

## 15. Left to the builder

Unity project structure, the format of the procedure data file, shader details, exact subtitle timing, choice of animation method (Animation clips vs. code tweens), backend message format.
