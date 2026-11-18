using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Connection to the voice assistant backend (backend/server.py, protocol in its docstring).
///
/// - Runs the assistant's tool calls on the StepEngine (start_procedure, next_step, point_at, ...)
///   and answers with { ok, state, error }, so the model always knows where the user is.
/// - Sends the app state whenever the procedure or step changes (also via the Done/Back buttons).
/// - Shows the final subtitles ("You: ..." / "Assistant: ...") in the ManualUI.
/// - Audio in/out (mic, assistant voice) is passed on through the methods and events below.
///
/// Editor: ws://localhost:8765. Phone: ws://&lt;PC IP address&gt;:8765 (same Wi-Fi).
/// </summary>
public class AssistantClient : MonoBehaviour
{
    [SerializeField] StepEngine engine;
    [SerializeField] ManualUI ui;

    [Tooltip("Editor: ws://localhost:8765. Phone: ws://<PC IP address>:8765 (PC and phone in the same Wi-Fi).")]
    [SerializeField] string serverUrl = "ws://localhost:8765";

    [Tooltip("Seconds between connection attempts while the backend is not reachable.")]
    [SerializeField] float reconnectSeconds = 3f;

    /// <summary>Assistant voice: base64 PCM, 16-bit, mono, 24 kHz.</summary>
    public event Action<string> AudioReceived;
    public event Action AudioDone;
    /// <summary>"ready", "listening", "thinking", "too_short", "offline".</summary>
    public event Action<string> StatusChanged;

    public bool IsConnected => ws != null && ws.State == WebSocketState.Open;

    ClientWebSocket ws;
    CancellationTokenSource cts;
    readonly Queue<byte[]> outbox = new Queue<byte[]>();   // sent strictly in order (mic chunks, then mic_stop)
    bool pumping;

    bool runningTool;
    Procedure sentProcedure;
    int sentStep = -2;

    // ------------------------------------------------------------- messages

    [Serializable]
    class Incoming
    {
        public string type, call_id, name, data, text, who, state, message;
        public bool final;
        public ToolArgs arguments;
    }

    [Serializable]
    class ToolArgs
    {
        public string procedure_id, part, marker;
        public int step;
        public bool on;
    }

    [Serializable]
    class ToolResult
    {
        public bool ok;
        public string state;
        public string error;
    }

    [Serializable] class ToolResultMessage { public string type = "tool_result"; public string call_id; public ToolResult result; }
    [Serializable] class TextMessage { public string type; public string text; }
    [Serializable] class AudioMessage { public string type = "audio"; public string data; }
    [Serializable] class TypeMessage { public string type; }

    // ----------------------------------------------------------- lifecycle

    void OnEnable()
    {
        cts = new CancellationTokenSource();
        _ = ConnectLoop(cts.Token);
    }

    void OnDisable()
    {
        cts?.Cancel();
        ws?.Abort();
    }

    void Update()
    {
        // Report every procedure / step change (Done, Back, Exit, or the assistant itself).
        if (engine == null || runningTool) return;
        if (engine.Current != sentProcedure || engine.StepIndex != sentStep)
            _ = SendState();
    }

    async Task ConnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(serverUrl), ct);
                Debug.Log($"Assistant: connected to {serverUrl}", this);
                sentStep = -2;                        // send the state again on the next frame
                await ReceiveLoop(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Debug.Log($"Assistant: backend not reachable ({e.Message})", this); }
            finally
            {
                ws.Dispose();
                ws = null;
            }

            if (ct.IsCancellationRequested) break;
            StatusChanged?.Invoke("offline");
            try { await Task.Delay(TimeSpan.FromSeconds(reconnectSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (r.MessageType == WebSocketMessageType.Close) break;

            message.Write(buffer, 0, r.Count);
            if (!r.EndOfMessage) continue;

            string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            Handle(json);
        }
    }

    void Handle(string json)
    {
        Incoming msg;
        try { msg = JsonUtility.FromJson<Incoming>(json); }
        catch (Exception e)
        {
            Debug.LogWarning($"Assistant: bad message ({e.Message})", this);
            return;
        }

        switch (msg.type)
        {
            case "tool_call":
                var result = RunTool(msg.name, msg.arguments ?? new ToolArgs());
                Debug.Log($"Assistant tool: {msg.name} -> {(result.ok ? "ok" : result.error)}", this);
                _ = Send(new ToolResultMessage { call_id = msg.call_id, result = result });
                break;
            case "audio":
                AudioReceived?.Invoke(msg.data);
                break;
            case "audio_done":
                AudioDone?.Invoke();
                break;
            case "subtitle":
                if (msg.final && ui != null && !string.IsNullOrWhiteSpace(msg.text))
                    ui.ShowSubtitle(msg.text, msg.who == "user");
                break;
            case "status":
                StatusChanged?.Invoke(msg.state);
                break;
            case "error":
                Debug.LogWarning($"Assistant: {msg.message}", this);
                break;
        }
    }

    // --------------------------------------------------------------- tools

    ToolResult RunTool(string name, ToolArgs a)
    {
        if (engine == null) return Fail("The app has no step engine.");

        runningTool = true;
        try
        {
            bool running = engine.Current != null;
            switch (name)
            {
                case "start_procedure":
                    return engine.StartProcedure(a.procedure_id) ? Ok() : Fail($"Unknown procedure '{a.procedure_id}'.");

                case "next_step":
                    if (!running) return Fail("No guide is running.");
                    string title = engine.Current.title;
                    engine.Next();
                    return engine.Current == null
                        ? new ToolResult { ok = true, state = $"Guide \"{title}\" finished. No guide is running." }
                        : Ok();

                case "previous_step":
                    if (!running) return Fail("No guide is running.");
                    if (engine.StepIndex == 0) return Fail("Already at step 1.");
                    engine.Back();
                    return Ok();

                case "go_to_step":
                    if (!running) return Fail("No guide is running.");
                    return engine.GoToStep(a.step) ? Ok() : Fail($"There is no step {a.step}.");

                case "repeat_step":
                    if (!running) return Fail("No guide is running.");
                    engine.Repeat();
                    return Ok();

                case "stop_procedure":
                    engine.StopProcedure();
                    return Ok();

                case "highlight_part":
                    return engine.HighlightPart(a.part) ? Ok() : Fail($"No part '{a.part}'.");

                case "point_at":
                    return engine.PointAt(a.marker) ? Ok() : Fail($"No marker '{a.marker}'.");

                case "show_xray":
                    engine.ShowXray(a.on);
                    return Ok();

                case "clear_highlights":
                    engine.ClearHighlights();
                    engine.ClearPointers();
                    return Ok();

                default:
                    return Fail($"Unknown tool '{name}'.");
            }
        }
        finally
        {
            runningTool = false;
            // The tool result already carries the new state; don't send it again as a note.
            sentProcedure = engine.Current;
            sentStep = engine.StepIndex;
        }
    }

    ToolResult Ok() => new ToolResult { ok = true, state = DescribeState() };
    ToolResult Fail(string error) => new ToolResult { ok = false, error = error, state = DescribeState() };

    /// <summary>Same wording as the backend's test console, e.g. "Change the battery", step 2 of 6: ...</summary>
    string DescribeState()
    {
        var p = engine.Current;
        if (p == null) return "No guide is running.";
        return $"\"{p.title}\", step {engine.StepIndex + 1} of {p.steps.Length}: {engine.CurrentStep.text}";
    }

    async Task SendState()
    {
        sentProcedure = engine.Current;
        sentStep = engine.StepIndex;
        if (!IsConnected) return;
        await Send(new TextMessage { type = "state", text = DescribeState() });
    }

    // ---------------------------------------------------- audio (parts 3-4)

    /// <summary>Mic tapped on: the backend stops the assistant if it is still talking.</summary>
    public Task SendMicStart() => Send(new TypeMessage { type = "mic_start" });

    /// <summary>A chunk of mic audio: base64 PCM, 16-bit, mono, 24 kHz.</summary>
    public Task SendAudio(string pcmBase64) => Send(new AudioMessage { data = pcmBase64 });

    /// <summary>Mic tapped off: the question goes to the model.</summary>
    public Task SendMicStop() => Send(new TypeMessage { type = "mic_stop" });

    // ---------------------------------------------------------------- send

    /// <summary>Queues a message; the queue goes out strictly in order, one message at a time.</summary>
    Task Send(object message)
    {
        if (!IsConnected) return Task.CompletedTask;
        outbox.Enqueue(Encoding.UTF8.GetBytes(JsonUtility.ToJson(message)));
        if (!pumping) _ = Pump();
        return Task.CompletedTask;
    }

    // Runs on the main thread (Unity's synchronization context), so the queue needs no lock.
    async Task Pump()
    {
        pumping = true;
        try
        {
            while (outbox.Count > 0 && IsConnected)
            {
                var bytes = outbox.Dequeue();
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.Log($"Assistant: send failed ({e.Message})", this);
        }
        finally
        {
            pumping = false;
            if (!IsConnected) outbox.Clear();
        }
    }
}
