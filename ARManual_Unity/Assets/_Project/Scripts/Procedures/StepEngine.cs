using System;
using System.Collections.Generic;
using UnityEngine;
using Vuforia;

/// <summary>
/// Works through a procedure (JSON) step by step:
/// switches the Model Target, highlights parts, points at markers, starts animations.
///
/// It does not draw any UI. Buttons (test UI now, layout A later), voice commands
/// and the AI assistant all call the same public methods:
///   StartProcedure(id), Next(), Back(), Repeat(), GoToStep(n),
///   HighlightPart(name), PointAt(marker), ShowXray(on).
/// </summary>
public class StepEngine : MonoBehaviour
{
    [Serializable]
    public struct TargetEntry
    {
        [Tooltip("Key used in the procedure file, e.g. FaceUp or FaceDown.")]
        public string key;
        public ObserverBehaviour target;
    }

    [Header("Content")]
    [Tooltip("Procedure JSON files from Assets/_Project/Data/Procedures.")]
    [SerializeField] TextAsset[] procedureFiles;

    [Tooltip("The ONE Thermometer_Ghost instance. It is moved under whichever Model Target is active.")]
    [SerializeField] Transform content;

    [Header("Vuforia")]
    [SerializeField] TargetEntry[] targets;

    [Tooltip("Target that is active when no procedure is running.")]
    [SerializeField] string defaultTarget = "FaceUp";

    [Header("Look")]
    [Tooltip("Optional: applied to every part at start (e.g. M_Ghost). Leave empty to keep the prefab's materials.")]
    [SerializeField] Material baseMaterial;

    [SerializeField] Material highlightMaterial;

    [Tooltip("Optional: object placed on a marker by PointAt. If empty, a small sphere is used.")]
    [SerializeField] GameObject pointerPrefab;

    [Tooltip("Diameter of the fallback sphere in metres.")]
    [SerializeField] float pointerSize = 0.006f;

    [Header("Testing")]
    [Tooltip("Show the model even without tracking (for testing in the editor). Switch off for phone builds.")]
    [SerializeField] bool alwaysShowContent = false;

    /// <summary>Step changed. The int is the 0-based step index.</summary>
    public event Action<Procedure, int> StepChanged;
    public event Action<Procedure> ProcedureFinished;
    public event Action<bool> XrayChanged;
    /// <summary>Tracking status of the active target changed (target key, status).</summary>
    public event Action<string, TargetStatus> TrackingChanged;

    public IReadOnlyList<Procedure> Procedures => procedures;
    public Procedure Current { get; private set; }
    public int StepIndex { get; private set; } = -1;
    public ProcedureStep CurrentStep => Current != null && StepIndex >= 0 ? Current.steps[StepIndex] : null;
    public string ActiveTargetKey { get; private set; }
    public bool XrayOn { get; private set; }

    public bool IsTracked =>
        active != null &&
        (active.TargetStatus.Status == Status.TRACKED ||
         active.TargetStatus.Status == Status.EXTENDED_TRACKED);

    readonly List<Procedure> procedures = new List<Procedure>();
    readonly Dictionary<string, Transform> parts = new Dictionary<string, Transform>();
    readonly Dictionary<string, StepAnimation> animations = new Dictionary<string, StepAnimation>();
    readonly Dictionary<Renderer, Material[]> highlighted = new Dictionary<Renderer, Material[]>();
    readonly List<GameObject> pointers = new List<GameObject>();

    ObserverBehaviour active;
    StepAnimation playing;

    // ------------------------------------------------------------------ setup

    void Awake()
    {
        LoadProcedures();
        IndexContent();
        foreach (var p in procedures) Validate(p);

        foreach (var t in targets)
            if (t.target != null) t.target.OnTargetStatusChanged += OnTargetStatusChanged;
    }

    void Start()
    {
        ActivateTarget(defaultTarget);
    }

    void OnDestroy()
    {
        foreach (var t in targets)
            if (t.target != null) t.target.OnTargetStatusChanged -= OnTargetStatusChanged;
    }

    void LoadProcedures()
    {
        foreach (var file in procedureFiles)
        {
            if (file == null) continue;

            Procedure p;
            try { p = JsonUtility.FromJson<Procedure>(file.text); }
            catch (Exception e)
            {
                Debug.LogError($"StepEngine: could not read '{file.name}': {e.Message}", this);
                continue;
            }

            if (p == null || p.steps == null || p.steps.Length == 0)
            {
                Debug.LogError($"StepEngine: '{file.name}' has no steps.", this);
                continue;
            }
            if (string.IsNullOrEmpty(p.id)) p.id = file.name;
            if (string.IsNullOrEmpty(p.title)) p.title = p.id;
            procedures.Add(p);
        }
    }

    /// <summary>Builds a name -> object lookup of everything inside the content (parts and markers).</summary>
    void IndexContent()
    {
        if (content == null)
        {
            Debug.LogError("StepEngine: no content assigned.", this);
            return;
        }

        // Parents come first, so for duplicate names the topmost object wins.
        foreach (var t in content.GetComponentsInChildren<Transform>(true))
            if (!parts.ContainsKey(t.name)) parts.Add(t.name, t);

        foreach (var a in content.GetComponentsInChildren<StepAnimation>(true))
        {
            if (string.IsNullOrEmpty(a.animationName)) continue;
            if (animations.ContainsKey(a.animationName))
                Debug.LogWarning($"StepEngine: two animations named '{a.animationName}'.", a);
            else
                animations.Add(a.animationName, a);
        }

        if (baseMaterial != null)
            foreach (var r in content.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Fill(baseMaterial, r.sharedMaterials.Length);
    }

    /// <summary>Warns about names in the JSON that don't exist in the model (typos, renamed CAD parts).</summary>
    void Validate(Procedure p)
    {
        for (int i = 0; i < p.steps.Length; i++)
        {
            var s = p.steps[i];
            string where = $"'{p.id}' step {i + 1}";

            if (FindTarget(s.target) == null)
                Debug.LogWarning($"StepEngine: {where}: unknown target '{s.target}'.", this);

            foreach (var n in s.highlight ?? Array.Empty<string>())
                if (!parts.ContainsKey(n))
                    Debug.LogWarning($"StepEngine: {where}: no part named '{n}' in the model.", this);

            foreach (var n in s.pointAt ?? Array.Empty<string>())
                if (!parts.ContainsKey(n))
                    Debug.LogWarning($"StepEngine: {where}: no marker named '{n}' in the model.", this);

            if (!string.IsNullOrEmpty(s.animation) && !animations.ContainsKey(s.animation))
                Debug.Log($"StepEngine: {where}: animation '{s.animation}' is not built yet.", this);
        }
    }

    // ------------------------------------------------------------- public API

    public bool StartProcedure(string id)
    {
        var p = procedures.Find(x => x.id == id);
        if (p == null)
        {
            Debug.LogWarning($"StepEngine: no procedure '{id}'.", this);
            return false;
        }
        Current = p;
        GoToIndex(0);
        return true;
    }

    /// <summary>"Done": next step, or finish after the last one.</summary>
    public void Next()
    {
        if (Current == null) return;
        if (StepIndex < Current.steps.Length - 1) GoToIndex(StepIndex + 1);
        else Finish();
    }

    public void Back()
    {
        if (Current != null && StepIndex > 0) GoToIndex(StepIndex - 1);
    }

    /// <summary>n is 1-based, the way the user and the assistant count.</summary>
    public bool GoToStep(int n)
    {
        if (Current == null || n < 1 || n > Current.steps.Length) return false;
        GoToIndex(n - 1);
        return true;
    }

    /// <summary>"Repeat": replays the current step's animation.</summary>
    public void Repeat()
    {
        if (CurrentStep != null) PlayAnimation(CurrentStep.animation);
    }

    /// <summary>Aborts the running procedure and returns to the default target.</summary>
    public void StopProcedure()
    {
        ClearStepVisuals();
        Current = null;
        StepIndex = -1;
        ActivateTarget(defaultTarget);
    }

    public bool HighlightPart(string partName)
    {
        if (!parts.TryGetValue(partName, out var part))
        {
            Debug.LogWarning($"StepEngine: no part named '{partName}'.", this);
            return false;
        }
        if (highlightMaterial == null)
        {
            Debug.LogWarning("StepEngine: no highlight material assigned.", this);
            return false;
        }

        foreach (var r in part.GetComponentsInChildren<Renderer>(true))
        {
            if (highlighted.ContainsKey(r)) continue;
            highlighted[r] = r.sharedMaterials;
            r.sharedMaterials = Fill(highlightMaterial, r.sharedMaterials.Length);
        }
        return true;
    }

    public void ClearHighlights()
    {
        foreach (var kv in highlighted)
            if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
        highlighted.Clear();
    }

    /// <summary>Puts a pointer on a marker, e.g. "Display_Temperature".</summary>
    public bool PointAt(string markerName)
    {
        if (!parts.TryGetValue(markerName, out var marker))
        {
            Debug.LogWarning($"StepEngine: no marker named '{markerName}'.", this);
            return false;
        }

        GameObject pointer;
        if (pointerPrefab != null)
        {
            pointer = Instantiate(pointerPrefab, marker, false);
        }
        else
        {
            pointer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(pointer.GetComponent<Collider>());
            pointer.transform.SetParent(marker, false);
            float parentScale = Mathf.Max(marker.lossyScale.x, 1e-6f);
            pointer.transform.localScale = Vector3.one * (pointerSize / parentScale);
            if (highlightMaterial != null)
                pointer.GetComponent<Renderer>().sharedMaterial = highlightMaterial;
        }
        pointer.name = "Pointer_" + markerName;
        pointers.Add(pointer);
        return true;
    }

    public void ClearPointers()
    {
        foreach (var p in pointers)
            if (p != null) Destroy(p);
        pointers.Clear();
    }

    /// <summary>X-ray mode. The visual comes with the materials step; for now it only raises XrayChanged.</summary>
    public void ShowXray(bool on)
    {
        if (on == XrayOn) return;
        XrayOn = on;
        Debug.Log($"StepEngine: x-ray {(on ? "on" : "off")}.", this);
        XrayChanged?.Invoke(on);
    }

    // ---------------------------------------------------------------- steps

    void GoToIndex(int index)
    {
        ClearStepVisuals();
        StepIndex = index;
        var s = Current.steps[index];

        ActivateTarget(s.target);
        foreach (var n in s.highlight ?? Array.Empty<string>()) HighlightPart(n);
        foreach (var n in s.pointAt ?? Array.Empty<string>()) PointAt(n);
        ShowXray(s.xray);
        PlayAnimation(s.animation);

        StepChanged?.Invoke(Current, index);
    }

    void Finish()
    {
        var finished = Current;
        StopProcedure();
        ProcedureFinished?.Invoke(finished);
    }

    void ClearStepVisuals()
    {
        StopAnimation();
        ClearHighlights();
        ClearPointers();
        ShowXray(false);
    }

    void PlayAnimation(string animationName)
    {
        StopAnimation();
        if (string.IsNullOrEmpty(animationName)) return;

        if (animations.TryGetValue(animationName, out var a))
        {
            playing = a;
            a.Play();
        }
        else
        {
            Debug.Log($"StepEngine: would play '{animationName}' (not built yet).", this);
        }
    }

    void StopAnimation()
    {
        if (playing != null) playing.Stop();
        playing = null;
    }

    // -------------------------------------------------------------- tracking

    ObserverBehaviour FindTarget(string key)
    {
        foreach (var t in targets)
            if (t.key == key) return t.target;
        return null;
    }

    /// <summary>Activates exactly one Model Target and moves the content under it.</summary>
    void ActivateTarget(string key)
    {
        var next = FindTarget(key);
        if (next == null)
        {
            Debug.LogWarning($"StepEngine: unknown target '{key}'.", this);
            return;
        }

        if (next != active && content != null)
        {
            content.SetParent(next.transform, false);
            content.localPosition = Vector3.zero;
            content.localRotation = Quaternion.identity;
            content.localScale = Vector3.one;
        }

        active = next;
        ActiveTargetKey = key;

        foreach (var t in targets)
            if (t.target != null) t.target.gameObject.SetActive(t.target == next);

        UpdateContentVisibility();
    }

    void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus status)
    {
        if (behaviour != active) return;
        UpdateContentVisibility();
        TrackingChanged?.Invoke(ActiveTargetKey, status);
    }

    /// <summary>The model is only visible while the device is found (or always, when testing).</summary>
    void UpdateContentVisibility()
    {
        if (content == null) return;

        bool visible = alwaysShowContent || IsTracked;
        if (content.gameObject.activeSelf == visible) return;

        content.gameObject.SetActive(visible);

        // Animations stop when the content is hidden; restart the step's animation once the device is found again.
        if (visible && CurrentStep != null) PlayAnimation(CurrentStep.animation);
    }

    static Material[] Fill(Material m, int count)
    {
        var result = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < result.Length; i++) result[i] = m;
        return result;
    }
}
