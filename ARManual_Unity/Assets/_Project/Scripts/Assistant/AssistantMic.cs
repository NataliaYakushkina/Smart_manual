using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Push-to-talk on the mic button: tap to start, tap to stop.
///
/// Start: the assistant stops talking, the mic turns red and records.
/// While recording: audio goes to the backend in ~0.1 s chunks (PCM, 16-bit, mono, 24 kHz).
/// Stop: the rest is sent, then "mic_stop"; the model hears the whole question and answers.
/// </summary>
public class AssistantMic : MonoBehaviour
{
    const int TargetRate = 24000;   // what OpenAI Realtime expects

    [SerializeField] AssistantClient client;
    [SerializeField] AssistantVoice voice;
    [SerializeField] ManualUI ui;

    [Tooltip("Recording stops by itself after this many seconds.")]
    [SerializeField] float maxSeconds = 15f;

    [Tooltip("Audio is sent in chunks of about this length, in seconds.")]
    [SerializeField] float chunkSeconds = 0.1f;

    public bool IsRecording { get; private set; }

    string device;          // null = default microphone
    AudioClip clip;
    int readPos;
    float startTime;

    void Awake()
    {
        // Usually on the same object as the AssistantClient / AssistantVoice: then the fields can stay empty.
        if (client == null) client = GetComponent<AssistantClient>();
        if (voice == null) voice = GetComponent<AssistantVoice>();
    }

    void OnEnable()
    {
        if (ui != null) ui.MicPressed += Toggle;
        if (client != null) client.StatusChanged += OnStatus;
    }

    void OnDisable()
    {
        if (ui != null) ui.MicPressed -= Toggle;
        if (client != null) client.StatusChanged -= OnStatus;
        if (IsRecording) StopRecording();
    }

    public void Toggle()
    {
        if (IsRecording) StopRecording();
        else StartRecording();
    }

    void StartRecording()
    {
        if (client == null || !client.IsConnected)
        {
            ui?.ShowSubtitle("I'm offline right now. Please check that the assistant server is running.");
            return;
        }
        if (Microphone.devices.Length == 0)
        {
            ui?.ShowSubtitle("No microphone found.");
            return;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);   // tap the mic again after allowing
            return;
        }
#endif
        voice?.Stop();                    // interrupt the assistant
        _ = client.SendMicStart();

        // Loop buffer a bit longer than the maximum, so it never overwrites unread audio.
        clip = Microphone.Start(device, true, Mathf.CeilToInt(maxSeconds) + 2, TargetRate);
        readPos = 0;
        startTime = Time.unscaledTime;
        IsRecording = true;
        ui?.SetListening(true);
    }

    void StopRecording()
    {
        if (!IsRecording) return;
        SendNewAudio(force: true);
        Microphone.End(device);
        IsRecording = false;
        ui?.SetListening(false);
        _ = client.SendMicStop();
    }

    void Update()
    {
        if (!IsRecording) return;
        SendNewAudio(force: false);
        if (Time.unscaledTime - startTime > maxSeconds) StopRecording();
    }

    void OnStatus(string state)
    {
        if (state == "too_short") ui?.ShowSubtitle("I didn't catch that. Tap the mic, speak, then tap again.");
        if (state == "offline" && IsRecording) StopRecording();
    }

    /// <summary>Sends the audio recorded since the last call (only once a full chunk is there, unless forced).</summary>
    void SendNewAudio(bool force)
    {
        if (clip == null) return;
        int pos = Microphone.GetPosition(device);
        if (pos < 0) return;

        int count = (pos - readPos + clip.samples) % clip.samples;
        if (count == 0 || (!force && count < chunkSeconds * clip.frequency)) return;

        var samples = new float[count * clip.channels];
        clip.GetData(samples, readPos);              // wraps around the loop buffer by itself
        readPos = pos;

        if (clip.channels > 1) samples = FirstChannel(samples, clip.channels);
        if (clip.frequency != TargetRate) samples = Resample(samples, clip.frequency, TargetRate);

        _ = client.SendAudio(ToPcm16Base64(samples));
    }

    static float[] FirstChannel(float[] interleaved, int channels)
    {
        var mono = new float[interleaved.Length / channels];
        for (int i = 0; i < mono.Length; i++) mono[i] = interleaved[i * channels];
        return mono;
    }

    // Some phones only record at 44.1 / 48 kHz: convert to 24 kHz (linear, good enough for speech).
    static float[] Resample(float[] input, int fromRate, int toRate)
    {
        int outLength = (int)((long)input.Length * toRate / fromRate);
        var output = new float[outLength];
        float step = (float)fromRate / toRate;
        for (int i = 0; i < outLength; i++)
        {
            float x = i * step;
            int a = Mathf.Min((int)x, input.Length - 1);
            int b = Mathf.Min(a + 1, input.Length - 1);
            output[i] = Mathf.Lerp(input[a], input[b], x - a);
        }
        return output;
    }

    static string ToPcm16Base64(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * 32767f), short.MinValue, short.MaxValue);
            bytes[2 * i] = (byte)(s & 0xff);              // little-endian
            bytes[2 * i + 1] = (byte)((s >> 8) & 0xff);
        }
        return Convert.ToBase64String(bytes);
    }
}
