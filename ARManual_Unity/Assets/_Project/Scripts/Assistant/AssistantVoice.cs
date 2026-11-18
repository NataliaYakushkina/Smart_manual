using System;
using UnityEngine;

/// <summary>
/// Plays the assistant's voice while it streams in from the backend
/// (base64 PCM, 16-bit, mono, 24 kHz, in small chunks).
///
/// The chunks go into a ring buffer; a streaming AudioClip reads from it on Unity's audio thread.
/// Playback starts once a little audio is buffered, so short network gaps don't crackle.
/// Stop() silences it at once (used when the user taps the mic to interrupt).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AssistantVoice : MonoBehaviour
{
    const int SampleRate = 24000;

    [SerializeField] AssistantClient client;

    [Tooltip("Audio buffered before the voice starts, in seconds. More = fewer gaps, a bit more delay.")]
    [SerializeField] float prebufferSeconds = 0.15f;

    [Tooltip("Longest answer the buffer can hold, in seconds.")]
    [SerializeField] int bufferSeconds = 120;

    /// <summary>True while there is voice left to play.</summary>
    public bool IsSpeaking { get { lock (sync) return buffered > 0; } }

    readonly object sync = new object();
    float[] ring;
    int readPos, writePos, buffered;
    bool started;       // prebuffer reached (or the answer is complete): drain the buffer

    AudioSource source;

    void Awake()
    {
        // Usually on the same object as the AssistantClient: then the field can stay empty.
        if (client == null) client = GetComponent<AssistantClient>();

        ring = new float[SampleRate * Mathf.Max(10, bufferSeconds)];

        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;   // plain 2D voice, not placed in the room
        // A streaming clip that never ends: OnRead fills it from the ring buffer (or with silence).
        source.clip = AudioClip.Create("AssistantVoice", SampleRate * 2, 1, SampleRate, true, OnRead);
        source.Play();
    }

    void OnEnable()
    {
        if (client == null) return;
        client.AudioReceived += OnAudio;
        client.AudioDone += OnAudioDone;
    }

    void OnDisable()
    {
        if (client == null) return;
        client.AudioReceived -= OnAudio;
        client.AudioDone -= OnAudioDone;
    }

    /// <summary>Silences the voice immediately and drops what is still buffered.</summary>
    public void Stop()
    {
        lock (sync)
        {
            readPos = writePos = buffered = 0;
            started = false;
        }
    }

    void OnAudio(string pcmBase64)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(pcmBase64); }
        catch (FormatException) { return; }

        int samples = bytes.Length / 2;
        lock (sync)
        {
            for (int i = 0; i < samples; i++)
            {
                if (buffered == ring.Length) break;              // buffer full: drop the rest
                short s = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8));   // little-endian
                ring[writePos] = s / 32768f;
                writePos = (writePos + 1) % ring.Length;
                buffered++;
            }
            if (buffered >= prebufferSeconds * SampleRate) started = true;
        }
    }

    void OnAudioDone()
    {
        lock (sync)
        {
            started = buffered > 0;    // a short answer may never reach the prebuffer: play it anyway
        }
    }

    // Audio thread: fill the clip with buffered voice, silence otherwise.
    void OnRead(float[] data)
    {
        lock (sync)
        {
            int i = 0;
            if (started)
            {
                for (; i < data.Length && buffered > 0; i++)
                {
                    data[i] = ring[readPos];
                    readPos = (readPos + 1) % ring.Length;
                    buffered--;
                }
                // Ran dry: the next audio waits for the prebuffer again (or for "audio done").
                if (buffered == 0) started = false;
            }
            for (; i < data.Length; i++) data[i] = 0f;
        }
    }
}
