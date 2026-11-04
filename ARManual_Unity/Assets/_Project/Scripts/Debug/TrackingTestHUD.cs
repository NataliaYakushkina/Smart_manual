using UnityEngine;
using Vuforia;

/// <summary>
/// Debug overlay for the tracking test.
/// - Keeps exactly ONE Model Target active at a time (standard Model Targets
///   with guide views should not run in parallel).
/// - Shows the active target's tracking status (TRACKED / EXTENDED_TRACKED / ...).
/// - A big button at the bottom switches to the next target (e.g. after flipping the device).
/// Test tool only; the real step engine will do the switching later.
/// </summary>
public class TrackingTestHUD : MonoBehaviour
{
    [Tooltip("Model Targets to switch between, e.g. ModelTarget_Face_up and ModelTarget_Face_down.")]
    [SerializeField] ObserverBehaviour[] targets;

    [Tooltip("Index of the target that is active at start.")]
    [SerializeField] int startIndex = 0;

    int active;
    GUIStyle labelStyle, buttonStyle;

    void Start()
    {
        if (targets == null || targets.Length == 0)
        {
            Debug.LogError("TrackingTestHUD: no targets assigned.", this);
            enabled = false;
            return;
        }
        Activate(Mathf.Clamp(startIndex, 0, targets.Length - 1));
    }

    void Activate(int index)
    {
        active = index;
        for (int i = 0; i < targets.Length; i++)
            targets[i].gameObject.SetActive(i == active);
    }

    void OnGUI()
    {
        if (labelStyle == null)
        {
            int size = Mathf.RoundToInt(Screen.height / 45f);
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold };
            labelStyle.normal.textColor = Color.yellow;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = size };
        }

        float w = Screen.width, h = Screen.height;
        ObserverBehaviour target = targets[active];
        TargetStatus status = target.TargetStatus;

        GUI.Label(new Rect(30, h * 0.05f, w - 60, h * 0.12f),
                  $"{target.name}\n{status.Status} / {status.StatusInfo}", labelStyle);

        int next = (active + 1) % targets.Length;
        if (targets.Length > 1 &&
            GUI.Button(new Rect(30, h * 0.86f, w - 60, h * 0.08f), $"Switch to {targets[next].name}", buttonStyle))
        {
            Activate(next);
        }
    }
}
