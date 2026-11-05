using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Vuforia;

/// <summary>
/// Works through a procedure (JSON) step by step:
/// switches the Model Target, highlights parts, points at markers, starts animations.
///
/// It does not draw any UI. Buttons (test UI now, layout A later), voice commands
/// and the AI assistant all call the same public methods:
///   StartProcedure(id), Next(), Back(), Repeat(), GoToStep(n),
///   HighlightPart(name), OutlinePart(name), PointAt(marker), HidePart(name), ShowXray(on).
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

    [Serializable]
    public struct XrayPart
    {
        [Tooltip("Part name, e.g. Battery or BatteryCavity.")]
        public string part;
        [Tooltip("Material in x-ray mode. Empty = the prefab's own material.")]
        public Material material;
    }

    [Header("Look")]
    [Tooltip("M_Occluder: invisible material on every part that is not shown. The user sees the real device, " +
             "and 3D content behind it stays hidden, like behind the real housing. Empty = keep the prefab's materials.")]
    [FormerlySerializedAs("baseMaterial")]
    [SerializeField] Material occluderMaterial;

    [SerializeField] Material highlightMaterial;

    [Tooltip("Parts that are only drawn in x-ray mode (\"xray\": true in the step), e.g. Battery and BatteryCavity. " +
             "Outside x-ray mode they are switched off, unless they are highlighted.")]
    [SerializeField] XrayPart[] xrayParts;

    [Tooltip("Optional: object placed on a marker by PointAt. If empty, a small sphere is used.")]
    [SerializeField] GameObject pointerPrefab;

    [Tooltip("Diameter of the fallback sphere in metres.")]
    [SerializeField] float pointerSize = 0.006f;

    [Tooltip("Outline ring: gap between the part's edge and the ring, in metres.")]
    [SerializeField] float ringGap = 0.002f;

    [Tooltip("Outline ring: width of the ring, in metres.")]
    [SerializeField] float ringWidth = 0.0015f;

    [Tooltip("Outline ring: how far it floats above the part's surface, in metres.")]
    [SerializeField] float ringLift = 0.0003f;

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
    readonly List<GameObject> pointers = new List<GameObject>();
    readonly List<GameObject> outlines = new List<GameObject>();
    Bounds deviceBounds;    // whole model, in content space; tells which side of a part faces outwards

    // Look of every part. ApplyLooks() decides each renderer from these sets.
    readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
    readonly Dictionary<Renderer, Material> xrayLook = new Dictionary<Renderer, Material>();
    readonly HashSet<Renderer> highlighted = new HashSet<Renderer>();
    readonly HashSet<Renderer> hidden = new HashSet<Renderer>();

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
            // Trim: a pasted name easily carries an invisible space or tab.
            string key = a.animationName?.Trim();
            if (string.IsNullOrEmpty(key)) continue;
            if (animations.ContainsKey(key))
                Debug.LogWarning($"StepEngine: two animations named '{key}'.", a);
            else
                animations.Add(key, a);
        }

        foreach (var r in content.GetComponentsInChildren<Renderer>(true))
            originals[r] = r.sharedMaterials;

        LocalBounds(content, out deviceBounds);

        foreach (var x in xrayParts ?? Array.Empty<XrayPart>())
        {
            if (!parts.TryGetValue(x.part?.Trim() ?? "", out var part))
            {
                Debug.LogWarning($"StepEngine: x-ray part '{x.part}' is not in the model.", this);
                continue;
            }
            foreach (var r in part.GetComponentsInChildren<Renderer>(true))
                xrayLook[r] = x.material;
        }

        ApplyLooks();
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

            foreach (var n in s.outline ?? Array.Empty<string>())
                if (!parts.ContainsKey(n))
                    Debug.LogWarning($"StepEngine: {where}: no part named '{n}' to outline.", this);

            foreach (var n in s.pointAt ?? Array.Empty<string>())
                if (!parts.ContainsKey(n))
                    Debug.LogWarning($"StepEngine: {where}: no marker named '{n}' in the model.", this);

            foreach (var n in s.hide ?? Array.Empty<string>())
                if (!parts.ContainsKey(n))
                    Debug.LogWarning($"StepEngine: {where}: no part named '{n}' to hide.", this);

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
            highlighted.Add(r);
        ApplyLooks();
        return true;
    }

    public void ClearHighlights()
    {
        highlighted.Clear();
        ApplyLooks();
    }

    /// <summary>
    /// Draws an orange ring around a part and leaves the part itself real, e.g. the UnitButton.
    /// The ring lies on the part's outer face: the flattest direction of the part,
    /// on the side that faces away from the middle of the device.
    /// </summary>
    public bool OutlinePart(string partName)
    {
        if (!parts.TryGetValue(partName, out var part))
        {
            Debug.LogWarning($"StepEngine: no part named '{partName}'.", this);
            return false;
        }
        if (highlightMaterial == null || !LocalBounds(part, out var b))
        {
            Debug.LogWarning($"StepEngine: cannot outline '{partName}' (no mesh or no highlight material).", this);
            return false;
        }

        Vector3 size = b.size;
        int axis = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);
        float side = b.center[axis] >= deviceBounds.center[axis] ? 1f : -1f;

        Vector3 normal = Vector3.zero;
        normal[axis] = side;

        Vector3 position = b.center;
        position[axis] = side > 0 ? b.max[axis] : b.min[axis];
        position += normal * ringLift;

        float radius = 0.5f * Mathf.Max(size[(axis + 1) % 3], size[(axis + 2) % 3]) + ringGap;

        var ring = new GameObject("Outline_" + partName, typeof(MeshFilter), typeof(MeshRenderer));
        ring.transform.SetParent(content, false);
        ring.transform.localPosition = position;
        ring.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, normal);
        ring.GetComponent<MeshFilter>().sharedMesh = BuildRing(radius, radius + ringWidth);
        ring.GetComponent<MeshRenderer>().sharedMaterial = highlightMaterial;

        outlines.Add(ring);
        return true;
    }

    public void ClearOutlines()
    {
        foreach (var ring in outlines)
        {
            if (ring == null) continue;
            Destroy(ring.GetComponent<MeshFilter>().sharedMesh);
            Destroy(ring);
        }
        outlines.Clear();
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

    /// <summary>Hides a part that is already removed in reality, e.g. the lid while the battery is changed.</summary>
    public bool HidePart(string partName)
    {
        if (!parts.TryGetValue(partName, out var part))
        {
            Debug.LogWarning($"StepEngine: no part named '{partName}'.", this);
            return false;
        }

        // Only the renderers are switched off, so markers and animations inside the part keep working.
        foreach (var r in part.GetComponentsInChildren<Renderer>(true))
            hidden.Add(r);
        ApplyLooks();
        return true;
    }

    public void ShowAllParts()
    {
        hidden.Clear();
        ApplyLooks();
    }

    /// <summary>X-ray mode: draws the x-ray parts (battery, cavity) as if you could see inside the device.</summary>
    public void ShowXray(bool on)
    {
        if (on == XrayOn) return;
        XrayOn = on;
        ApplyLooks();
        XrayChanged?.Invoke(on);
    }

    /// <summary>
    /// Sets the look of every part. First rule that fits wins:
    ///   hidden      -> not drawn (removed in reality, e.g. the lid in steps 3-4)
    ///   highlighted -> orange
    ///   x-ray part  -> its x-ray material while x-ray is on, otherwise not drawn
    ///   everything else -> occluder: invisible, the real device shows through
    /// </summary>
    void ApplyLooks()
    {
        foreach (var kv in originals)
        {
            var r = kv.Key;
            if (r == null) continue;

            if (hidden.Contains(r))
            {
                r.enabled = false;
            }
            else if (highlighted.Contains(r) && highlightMaterial != null)
            {
                SetLook(r, highlightMaterial);
            }
            else if (xrayLook.TryGetValue(r, out var xray))
            {
                r.enabled = XrayOn;
                if (XrayOn) r.sharedMaterials = xray != null ? Fill(xray, kv.Value.Length) : kv.Value;
            }
            else if (occluderMaterial != null)
            {
                SetLook(r, occluderMaterial);
            }
            else
            {
                r.enabled = true;
                r.sharedMaterials = kv.Value;
            }
        }
    }

    void SetLook(Renderer r, Material m)
    {
        r.enabled = true;
        r.sharedMaterials = Fill(m, originals[r].Length);
    }

    // ---------------------------------------------------------------- steps

    void GoToIndex(int index)
    {
        ClearStepVisuals();
        StepIndex = index;
        var s = Current.steps[index];

        ActivateTarget(s.target);
        foreach (var n in s.highlight ?? Array.Empty<string>()) HighlightPart(n);
        foreach (var n in s.outline ?? Array.Empty<string>()) OutlinePart(n);
        foreach (var n in s.pointAt ?? Array.Empty<string>()) PointAt(n);
        foreach (var n in s.hide ?? Array.Empty<string>()) HidePart(n);
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
        ClearOutlines();
        ShowAllParts();
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

    /// <summary>Bounding box of all meshes inside a part, in the content's axes (metres, +Y = top of the device).</summary>
    bool LocalBounds(Transform part, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? mb.min.x : mb.max.x,
                    (i & 2) == 0 ? mb.min.y : mb.max.y,
                    (i & 4) == 0 ? mb.min.z : mb.max.z);
                Vector3 p = content.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return any;
    }

    /// <summary>Flat ring in the XY plane, visible from both sides.</summary>
    static Mesh BuildRing(float inner, float outer, int segments = 48)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        for (int face = 0; face < 2; face++)
        {
            int start = vertices.Count;
            Vector3 n = face == 0 ? Vector3.forward : Vector3.back;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                vertices.Add(dir * inner);
                vertices.Add(dir * outer);
                normals.Add(n);
                normals.Add(n);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + 2 * i, b = a + 1;
                int c = start + 2 * ((i + 1) % segments), d = c + 1;
                if (face == 0) triangles.AddRange(new[] { a, b, c, b, d, c });
                else triangles.AddRange(new[] { a, c, b, b, c, d });
            }
        }

        var mesh = new Mesh { name = "OutlineRing" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Material[] Fill(Material m, int count)
    {
        var result = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < result.Length; i++) result[i] = m;
        return result;
    }
}
