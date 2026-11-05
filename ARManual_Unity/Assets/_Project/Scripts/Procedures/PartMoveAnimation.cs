using System.Collections;
using UnityEngine;

/// <summary>
/// Moves one part in a straight line, e.g. the battery lid sliding down and off.
/// One script covers all the moves in the procedures:
///   LidSlideOff    direction (0,-1,0)                 lid slides down and off
///   LidSlideOn     direction (0,-1,0), Reverse on     lid comes back from below
///   BatteryLiftOut direction (0,0,-1)                 battery lifts out of the back
///   BatteryDropIn  direction (0,0,-1), Reverse on     battery goes back in
///   ButtonPress    direction (0,0,1),  Press And Release on
///
/// The direction is measured in the axes of "Space". If Space is empty, the axes of the
/// object this script sits on are used. Put the script on the Thermometer_Ghost root:
/// there +Y is the top of the device, +Z the display side, and the units are metres.
///
/// The part always returns to where it started when the step changes (Stop).
/// </summary>
public class PartMoveAnimation : StepAnimation
{
    [Tooltip("The part that moves, e.g. BatteryLid.")]
    [SerializeField] Transform part;

    [Tooltip("Axes the direction is measured in. Empty = the object this script is on.")]
    [SerializeField] Transform space;

    [SerializeField] Vector3 direction = Vector3.down;

    [Tooltip("How far the part moves, in metres (0.025 = 25 mm).")]
    [SerializeField] float distance = 0.025f;

    [Tooltip("Play the move backwards: the part starts away and comes back to its place (lid on, battery in).")]
    [SerializeField] bool reverse;

    [Tooltip("Move there and back again, e.g. a button press.")]
    [SerializeField] bool pressAndRelease;

    [Header("Timing (seconds)")]
    [SerializeField] float duration = 1.2f;
    [SerializeField] float holdAtEnd = 0.8f;
    [SerializeField] float pauseBeforeRepeat = 0.5f;

    [Tooltip("Repeat until the step changes. Off = play once and stay at the end position.")]
    [SerializeField] bool loop = true;

    Vector3 home;
    bool hasHome;
    Coroutine routine;

    public override void Play()
    {
        if (part == null)
        {
            Debug.LogWarning($"PartMoveAnimation '{animationName}': no part assigned.", this);
            return;
        }

        // Remember where the part belongs before it moves for the first time.
        if (!hasHome)
        {
            home = part.localPosition;
            hasHome = true;
        }

        Stop();

        // The model is hidden while the device is not found. The StepEngine plays the
        // animation again as soon as the model shows up.
        if (!isActiveAndEnabled) return;

        routine = StartCoroutine(Run());
    }

    public override void Stop()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        ResetPart();
    }

    void OnDisable()
    {
        // Coroutines stop by themselves when the object is hidden; put the part back too.
        routine = null;
        ResetPart();
    }

    void ResetPart()
    {
        if (hasHome && part != null) part.localPosition = home;
    }

    IEnumerator Run()
    {
        Vector3 offset = LocalOffset();
        Vector3 from = reverse ? home + offset : home;
        Vector3 to = reverse ? home : home + offset;

        do
        {
            part.localPosition = from;
            yield return Move(from, to);
            yield return new WaitForSeconds(holdAtEnd);

            if (pressAndRelease)
                yield return Move(to, from);

            if (loop)
                yield return new WaitForSeconds(pauseBeforeRepeat);
        }
        while (loop);

        routine = null;
    }

    IEnumerator Move(Vector3 a, Vector3 b)
    {
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            part.localPosition = Vector3.Lerp(a, b, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        part.localPosition = b;
    }

    /// <summary>The move, converted from Space's axes into the part's parent axes.</summary>
    Vector3 LocalOffset()
    {
        Vector3 world = WorldOffset();
        return part.parent != null ? part.parent.InverseTransformVector(world) : world;
    }

    Vector3 WorldOffset()
    {
        Transform s = space != null ? space : transform;
        return s.TransformVector(direction.normalized * distance);
    }

    // Shows the move as a yellow line in the Scene view when this object is selected.
    void OnDrawGizmosSelected()
    {
        if (part == null || direction == Vector3.zero) return;

        // Start at the centre of the visible part: CAD pivots often sit at the model origin.
        Vector3 start = part.position;
        var renderers = part.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            start = bounds.center;
        }
        Vector3 end = start + WorldOffset();
        if (reverse) (start, end) = (end, start);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(end, distance * 0.08f);
    }
}
