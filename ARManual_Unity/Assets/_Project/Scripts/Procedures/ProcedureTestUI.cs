using UnityEngine;

/// <summary>
/// Temporary test UI for the StepEngine (replaced by layout A in build step 4).
/// No procedure running: one button per procedure.
/// Procedure running: step card at the top, Back/Exit · Repeat · Done at the bottom.
/// </summary>
public class ProcedureTestUI : MonoBehaviour
{
    [SerializeField] StepEngine engine;

    GUIStyle labelStyle, buttonStyle;
    string message = "";

    void OnEnable()
    {
        if (engine != null) engine.ProcedureFinished += OnFinished;
    }

    void OnDisable()
    {
        if (engine != null) engine.ProcedureFinished -= OnFinished;
    }

    void OnFinished(Procedure p) => message = $"Finished: {p.title}";

    void OnGUI()
    {
        if (engine == null) return;
        EnsureStyles();

        float w = Screen.width, h = Screen.height, m = 30f;
        float bh = h * 0.07f;
        string tracking = $"{engine.ActiveTargetKey}: {(engine.IsTracked ? "found" : "searching...")}";

        // ---- menu
        if (engine.Current == null)
        {
            string top = string.IsNullOrEmpty(message) ? tracking : $"{message}\n{tracking}";
            GUI.Label(new Rect(m, h * 0.05f, w - 2 * m, h * 0.1f), top, labelStyle);

            float y = h * 0.6f;
            foreach (var p in engine.Procedures)
            {
                if (GUI.Button(new Rect(m, y, w - 2 * m, bh), p.title, buttonStyle))
                {
                    message = "";
                    engine.StartProcedure(p.id);
                }
                y += bh + 20f;
            }
            return;
        }

        // ---- step card
        var step = engine.CurrentStep;
        int n = engine.StepIndex + 1, total = engine.Current.steps.Length;
        GUI.Label(new Rect(m, h * 0.05f, w - 2 * m, h * 0.2f),
                  $"{engine.Current.title}  -  Step {n} of {total}\n{step.text}\n{tracking}", labelStyle);

        // ---- buttons
        float bw = (w - 4 * m) / 3f, by = h - bh - h * 0.05f;
        bool first = engine.StepIndex == 0, last = n == total;

        if (GUI.Button(new Rect(m, by, bw, bh), first ? "Exit" : "Back", buttonStyle))
        {
            if (first) engine.StopProcedure(); else engine.Back();
        }
        if (GUI.Button(new Rect(2 * m + bw, by, bw, bh), "Repeat", buttonStyle))
            engine.Repeat();
        if (GUI.Button(new Rect(3 * m + 2 * bw, by, bw, bh), last ? "Finish" : "Done", buttonStyle))
            engine.Next();
    }

    void EnsureStyles()
    {
        if (labelStyle != null) return;

        int size = Mathf.RoundToInt(Screen.height / 45f);

        var background = new Texture2D(1, 1);
        background.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
        background.Apply();

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            wordWrap = true,
            padding = new RectOffset(20, 20, 15, 15)
        };
        labelStyle.normal.textColor = Color.white;
        labelStyle.normal.background = background;

        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = size };
    }
}
