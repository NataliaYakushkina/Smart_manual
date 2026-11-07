using UnityEngine;

/// <summary>
/// Tells the ARManual/Battery shader where the battery's + and - ends are.
/// Put it on the battery object that has the Mesh Renderer (Battery_body).
///
/// The long side of the mesh is the battery axis. The + end (yellow band) is the end
/// nearer to the Plus Marker (Polarity_Plus), so the look always matches the procedure.
/// Works in the editor too, so you see the result without pressing Play.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(Renderer))]
public class BatteryLook : MonoBehaviour
{
    [Tooltip("Polarity_Plus marker. The yellow band goes to the end nearer to it.")]
    [SerializeField] Transform plusMarker;

    [Tooltip("Swap the ends by hand (used when no Plus Marker is set).")]
    [SerializeField] bool flip;

    static readonly int AxisStart = Shader.PropertyToID("_AxisStart");
    static readonly int AxisEnd = Shader.PropertyToID("_AxisEnd");

    void OnEnable() => Apply();
    void OnValidate() => Apply();

    public void Apply()
    {
        var mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null) return;

        // Long side of the mesh = battery axis (object space).
        Bounds b = mesh.bounds;
        Vector3 size = b.size;
        Vector3 dir = size.x >= size.y && size.x >= size.z ? Vector3.right
                    : size.y >= size.z ? Vector3.up : Vector3.forward;
        float half = Vector3.Dot(size, dir) * 0.5f;

        Vector3 a = b.center - dir * half;
        Vector3 c = b.center + dir * half;

        bool swap = flip;
        if (plusMarker != null)
        {
            Vector3 plus = transform.InverseTransformPoint(plusMarker.position);
            swap = (plus - c).sqrMagnitude < (plus - a).sqrMagnitude;
        }
        if (swap) (a, c) = (c, a);

        // A property block survives the StepEngine's material swaps (highlight, x-ray).
        var renderer = GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetVector(AxisStart, a);
        block.SetVector(AxisEnd, c);
        renderer.SetPropertyBlock(block);
    }
}
