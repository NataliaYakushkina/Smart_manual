# AR Smart Manual: Design

*Date: 2026-11-02 · Updated: 2026-11-04 (device facts checked against the real device, tracking test results) · Status: draft, waiting for Natalia's approval*

An Android AR app that replaces the paper instructions of a thermometer (thermo-hygrometer). The user points the phone at the real device and gets the steps as 3D animations sitting directly on top of it, plus a voice AI assistant that answers questions and controls the scene. The first and only procedure for now is **changing the battery**.

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
3. **Procedures as data:** steps are described in a data file (which part to highlight, which animation, what text), not hard-coded. Battery change is the first procedure, the °C/°F switch the second; more (e.g. wall mounting) can be added later without new code.
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
| Engine | Unity **6000.3.28f1** (Unity 6.3, URP, C#) |
| Platform | Android phone (Android 10 / API 29 or newer, ARCore support, ARM64) |
| Tracking | **Vuforia Engine 11.4.4 – Model Targets** (AR Foundation/ARCore can't track 3D objects on Android), Model Target Generator 11.4.4. Free Basic plan: each Model Target generation uses 1 of 20 quota |
| Language | English (speech and subtitles) |

## 6. Decisions made in the conversation

Marked by who made the call.

- **Scope = ideas 1–3** (CAD pipeline, procedures as data, AI assistant). *Natalia.*
- **Animations on top of the real device, with a virtual copy as backup** when tracking is lost. *Claude's recommendation; Natalia continued on that basis.*
- **Battery steps are done with the device lying face-down on the table**, because a still object tracks much more reliably than one in the hand. *Claude's recommendation.*
- **The user talks to the assistant; everything is also shown as subtitles** (for the soundless video). *Natalia.*
- **English.** *Natalia.*
- **Screen layout A: "Full camera view"**: small step card on top, subtitles and buttons floating at the bottom. *Natalia.*
- **Simple animations are made in Unity**, not Blender (straight-line moves: lid slides, battery lifts/drops). *Agreed.*
- **Natalia builds the app herself** (with Claude assisting), since the portfolio claim is that *she* can take a product from CAD to AR. The plan should therefore have clear checkpoints and include learning Vuforia. *Claude's call.*
- **The thesis backend code (public repo `Spatial_awareness_code`) is the starting point** for the Python backend: its WebSocket + tool-calling parts are reused and trimmed, not rewritten. *Claude's call.*
- **"Go" criteria for the tracking test:** with the device face-down on the table, the outline stays on the real device for 30 s while the phone moves slowly, with no visible drift beyond ~3–5 mm; it re-locks within ~2 s after the device leaves and re-enters the view. *Claude's call.*
- **Hybrid tracking is allowed** as an intermediate option: Model Target for the start and the final check, placemat for the face-down steps. *Claude's call.*
- **Unity 6.3 (6000.3.28f1) instead of the Vuforia-certified Unity 6.0.** Vuforia 11.4.4 officially lists only Unity 6.0 LTS; the face-up tracking test on the phone worked on 6.3. *Natalia.*
- **One Model Target per resting orientation:** `Termometer_FaceUp` (up vector +Z) and `Termometer_FaceDown` (up vector −Z), each with one standard guide view. Only one target is active at a time; the step engine activates the one the current step needs (step data field `target`). *Claude's call.*
- **Part names are set in Fusion** (source of truth) and flow through FBX into Unity; parts are not renamed in Unity. *Claude's call.*
- **Procedure file + step engine + LLM on top** (not LLM-only). The procedure file is the single source of truth for the steps. The step engine is the guaranteed path (Done/Repeat buttons, works offline, same every time). The LLM reads the same file as context and drives the same engine functions via tool calls (`go_to_step`, `replay_animation`, `highlight_part`, …), so it cannot invent steps. *Natalia, on Claude's recommendation (2026-11-05).*
- **The assistant also answers general questions about the device**, e.g. what the numbers and icons on the display mean, and points at them in AR. *Natalia.*

## 7. CAD model requirements

**Size:** 1:1 with the real device. The **outer shape** (outline, thickness, display window, edges) should match within about **1 mm**, because tracking depends on it. Inside details only need to look right.

**Separate, clearly named components** (names are set in Fusion, carry over into Unity and are used by the assistant):

| Component (Fusion name) | Needed for | Notes |
|---|---|---|
| Housing = `Low_body` + `Top_cover` | tracking, occlusion | battery compartment modelled as a real **hollow cavity** |
| `Hole…` (8×) | look only | ventilation holes on the back; left as separate components (grouping into `Vents` was blocked in Fusion, purely cosmetic) |
| `BatteryLid` | battery change | **slides straight down**; latch tab to be checked |
| `Battery` | battery change | **one AAA battery**, visible **+** end |
| Polarity markers (Unity only) | polarity hint, x-ray | contacts are not modelled; empty marker objects `Polarity_Plus` / `Polarity_Minus` are added in the Unity prefab. **+ is on the left**, seen from the open compartment with the device face-down |
| `Low_metal_plate`, `Top_metal_plate` | look only | decorative metal plates on the top and the back |
| `Display` | later procedures, AI questions | |
| `UnitButton` (°F/°C) | °C/°F procedure | on the back, top right (device face-down) |
| Display markers (Unity only) | AI questions, pointers | `Display_Temperature`, `Display_TemperatureUnit`, `Display_Humidity`, `Display_ComfortIcon`: empty objects over the matching spot of the segment LCD |
| Wall-mount keyhole | later procedure | stays part of the housing; marker point added in Unity |

Export from Fusion as FBX to `CAD/export/Thermometer.fbx` (history is kept by git). The FBX is in **millimetres**; Unity converts it to metres on import. In Unity the current version is copied to a fixed name (`Assets/_Project/Models/Thermometer/Thermometer.fbx`) so prefabs and scenes keep their links. As long as the **outer shape and the origin** don't change, the Model Targets stay valid and need no new generation. Pivots matter only for rotating parts.

**Inside accuracy:** the x-ray view (§8) shows inside parts to the user, so **battery position, polarity and contacts** must match the real device, not just "look right".

## 8. How the AR content looks

- **Highlight:** a glowing, semi-transparent copy of a CAD part drawn exactly over the real part, so the real part appears to light up (pulse).
- **Ghost animation:** a transparent, cyan-edged (fresnel) copy of a part shows the motion, e.g. the lid sliding off, looping 2–3×. The user then copies it with the real part.
- **Arrows** for the direction of motion.
- **Occluder:** an invisible copy of the housing hides virtual content behind the real device, so the overlay looks attached.
- **Cavity + x-ray:** a dark virtual cavity where the lid is, with the ghost battery inside and big **+ / −** labels, shown before and after opening. X-ray is a mode that is switched on (button or the assistant's `show_xray()`), not the default view.
- **Backup virtual copy:** when tracking is lost, a full 3D copy of the device is shown with a yellow banner ("Lost the device. Showing a 3D copy…") and keeps playing the current step.

Materials in URP Shader Graph (ghost/fresnel, depth-only occluder, dark cavity).

## 9. The battery-change procedure (draft)

*Checked against the real device on 2026-11-04: one AAA battery, lid slides straight down, + contact on the left (seen from the open compartment, device face-down). Still to check: is there a latch to press?*

Expected Model Target per step: steps 1–5 **FaceDown**, step 6 **FaceUp** (see §6).

1. Place the thermometer face-down on the table.
2. Slide the battery lid straight down and off (lid glows, ghost lid slides, arrow).
3. Remove the old battery (ghost battery lifts out).
4. Insert a new AAA battery, **+ to the left** (ghost battery drops in, + / − labels, contacts highlighted).
5. Slide the lid back on until it clicks.
6. Turn the device over to see the temperature on the display. *Natalia.*

The user moves on by tapping **Done ✓** or saying "done" / "next". **↺ Repeat** replays the step.

### Second procedure: switch between °C and °F *(Natalia, 2026-11-05)*

1. Place the thermometer face-down on the table. (FaceDown)
2. Press the button at the top right (`UnitButton` glows). (FaceDown)
3. Turn the device over to see the temperature in the new unit (pointer on `Display_TemperatureUnit`). (FaceUp)

**Files:** `Assets/_Project/Data/Procedures/battery_change.json`, `unit_switch.json`; device facts in `Assets/_Project/Data/device_facts.md`. The display is a **segment LCD** (temperature with °C/°F at the top, humidity in % at the bottom, comfort face icon bottom left), so reading the temperature after the battery change does show that the device has power.

## 10. The AI assistant

**Interaction**
- Big mic button at the bottom centre (tap to talk). Short voice commands "next", "done", "repeat" also work.
- The user's words appear immediately as a subtitle (**yellow**, "You: …").
- The answer is spoken (text-to-speech) **and** shown as a subtitle (**white**, "Assistant: …"), high contrast, long enough to read.

**What the assistant knows and does**
- Context: the device's part list and the procedure steps from the data file, plus a short **device facts file** written by Natalia (battery: **1× AAA**, + on the left seen from the open compartment; display icons such as the low-battery symbol, °F/°C button, wall mounting). If a manufacturer paper manual exists, it is the source for that file. *Claude's call.*
- **Two kinds of questions:**
  - *About the current step* ("which way does the battery go?"): answered from the procedure file, often with `highlight_part` / `show_xray()`.
  - *General questions about the device* ("what do the numbers on the display mean?", "what is the small icon?"): answered from the device facts file, with `point_at` on the matching display area.
- **The device facts file must describe the display completely:** each value (e.g. temperature, humidity), its unit and position on the display, and each icon (low battery, comfort indicator, trend arrows, … whatever the real device shows). Natalia writes it from the real device. *Claude's call.*
- **Display areas as markers:** empty marker objects in the Unity prefab (e.g. `Display_Temperature`, `Display_Humidity`, one per icon), placed over the matching spot on the display, like the polarity markers. The names match the facts file.
- A small set of test questions checks that it refuses what isn't in the manual (e.g. "can I use rechargeable batteries?" when the facts file doesn't say).
- Tools it can call (first set): `highlight_part(name)` (CAD parts), `point_at(marker)` (arrow/ring on a marker, e.g. a display area), `go_to_step(n)`, `replay_animation()`, `show_xray()`.
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

1. Tracking test (go / plan B decision). *Status 2026-11-04: face-up passed the drift criterion (overlay within 3–5 mm for 30 s; extended tracking keeps it anchored in the room). Face-down target generated; switching between the two targets via a test HUD; re-lock time still to measure.*
2. CAD prepared and exported; parts imported and checked in Unity.
3. Procedure data file + step engine + battery-change animations and materials.
4. UI layout A (step card, buttons, subtitles).
5. Backup virtual copy when tracking is lost.
6. Python backend + LLM with tool calls (text only first).
7. Voice: speech-to-text, text-to-speech, subtitles.
8. Polish and record the demo video.

## 14. Open questions

- ~~Does the CAD model match the real device within ~1 mm on the outside?~~ Face-up tracking passed, so the outer shape is accurate enough.
- ~~Battery type and count, and how the lid opens.~~ 1× AAA; lid slides straight down. Open: is there a latch to press?
- Which Android phone model? (check Vuforia/ARCore support; tracking already works on Natalia's phone)
- **Vuforia watermark:** with the Basic plan, Model Target builds show "Premium Trial Feature App – Not for Distribution" and a honeycomb pattern. Accept it in the demo video, or ask Vuforia about a student/academic licence or a time-limited Premium trial before recording (build step 8)?
- LLM provider: keep the provider from the thesis code, or switch.
- Speech-to-text and text-to-speech: cloud services or Android's built-in ones.

## 15. Left to the builder

Unity project structure, the format of the procedure data file, shader details, exact subtitle timing, choice of animation method (Animation clips vs. code tweens), backend message format.
