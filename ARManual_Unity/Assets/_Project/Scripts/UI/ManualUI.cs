using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Screen layout A (docs/mockups/app-flow.html): step card on top, subtitles and
/// Repeat · mic · Done at the bottom, yellow banner while the device is not found.
///
/// Everything goes through the StepEngine, the same way voice and the AI assistant will later.
/// The assistant uses ShowSubtitle(), SetListening() and the MicPressed event.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ManualUI : MonoBehaviour
{
    [SerializeField] StepEngine engine;

    [Tooltip("How long the 'Finished' message stays, in seconds.")]
    [SerializeField] float messageSeconds = 4f;

    [Tooltip("Most subtitles shown at once; older ones are removed.")]
    [SerializeField] int maxSubtitles = 2;

    /// <summary>The mic button was tapped (the voice assistant subscribes to this).</summary>
    public event Action MicPressed;

    VisualElement root, banner, progress, progressFill, subtitles, menu;
    Label bannerText, cardLabel, cardText;
    Button back, exit, repeat, done, mic;

    bool lastVisible;
    Procedure lastProcedure;
    Rect lastSafeArea;
    IVisualElementScheduledItem clearMessage;

    // ------------------------------------------------------------- setup

    void OnEnable()
    {
        root = GetComponent<UIDocument>().rootVisualElement;

        banner = root.Q("banner");
        bannerText = root.Q<Label>("banner-text");
        cardLabel = root.Q<Label>("card-label");
        cardText = root.Q<Label>("card-text");
        progress = root.Q("progress");
        progressFill = root.Q("progress-fill");
        subtitles = root.Q("subtitles");
        menu = root.Q("menu");

        back = root.Q<Button>("back");
        exit = root.Q<Button>("exit");
        repeat = root.Q<Button>("repeat");
        done = root.Q<Button>("done");
        mic = root.Q<Button>("mic");

        back.clicked += OnBack;
        exit.clicked += OnExit;
        repeat.clicked += OnRepeat;
        done.clicked += OnDone;
        mic.clicked += OnMic;

        root.RegisterCallback<GeometryChangedEvent>(_ => ApplySafeArea());

        if (engine != null)
        {
            engine.StepChanged += OnStepChanged;
            engine.ProcedureFinished += OnFinished;
        }
    }

    void OnDisable()
    {
        if (back != null) back.clicked -= OnBack;
        if (exit != null) exit.clicked -= OnExit;
        if (repeat != null) repeat.clicked -= OnRepeat;
        if (done != null) done.clicked -= OnDone;
        if (mic != null) mic.clicked -= OnMic;

        if (engine != null)
        {
            engine.StepChanged -= OnStepChanged;
            engine.ProcedureFinished -= OnFinished;
        }
    }

    void Start()
    {
        // The engine loads the procedures in Awake, so the menu is built here.
        BuildMenu();
        Refresh();
    }

    void Update()
    {
        if (engine == null) return;

        // Tracking found / lost, or a guide started / stopped by the assistant, changes the screen.
        if (engine.IsContentVisible != lastVisible || engine.Current != lastProcedure) Refresh();

        if (Screen.safeArea != lastSafeArea) ApplySafeArea();
    }

    void BuildMenu()
    {
        menu.Clear();
        if (engine == null) return;

        foreach (var p in engine.Procedures)
        {
            string id = p.id;
            var button = new Button(() => OnChoose(id)) { text = p.title };
            button.AddToClassList("menu-button");
            menu.Add(button);
        }
    }

    // ------------------------------------------------------------ public

    /// <summary>Adds a subtitle line. fromUser = the user's own words (yellow), otherwise the assistant (white).</summary>
    public void ShowSubtitle(string text, bool fromUser = false)
    {
        var line = new Label((fromUser ? "You: " : "Assistant: ") + text);
        line.AddToClassList("sub");
        if (fromUser) line.AddToClassList("sub--you");
        line.pickingMode = PickingMode.Ignore;
        subtitles.Add(line);

        while (subtitles.childCount > Mathf.Max(1, maxSubtitles))
            subtitles.RemoveAt(0);
    }

    public void ClearSubtitles() => subtitles.Clear();

    /// <summary>Mic turns red while the assistant is listening.</summary>
    public void SetListening(bool listening) => mic.EnableInClassList("mic--listening", listening);

    // ----------------------------------------------------------- buttons

    void OnChoose(string id)
    {
        clearMessage?.Pause();
        ClearSubtitles();
        engine.StartProcedure(id);
    }

    void OnBack() => engine.Back();

    void OnExit()
    {
        engine.StopProcedure();
        Refresh();
    }

    void OnRepeat() => engine.Repeat();

    void OnDone() => engine.Next();

    void OnMic()
    {
        if (MicPressed != null) MicPressed.Invoke();
        else ShowSubtitle("The voice assistant is not connected yet.");
    }

    // ------------------------------------------------------ engine events

    void OnStepChanged(Procedure p, int index) => Refresh();

    void OnFinished(Procedure p)
    {
        Refresh();
        ShowSubtitle($"Finished: {p.title}.");

        clearMessage?.Pause();
        clearMessage = root.schedule.Execute(ClearSubtitles).StartingIn((long)(messageSeconds * 1000));
    }

    // ------------------------------------------------------------ drawing

    void Refresh()
    {
        if (engine == null) return;

        bool visible = engine.IsContentVisible;
        lastVisible = visible;
        lastProcedure = engine.Current;

        var p = engine.Current;
        if (p == null)
        {
            // Home: find the device, then choose a guide.
            cardLabel.text = visible ? "WHAT DO YOU NEED HELP WITH?" : "FIND YOUR DEVICE";
            cardText.text = visible
                ? "Choose a guide below."
                : "Point the camera at the thermometer and match the outline.";

            Show(progress, false);
            Show(back, false);
            Show(exit, false);
            Show(banner, false);
            Show(menu, true);
            SetInvisible(repeat, true);
            SetInvisible(done, true);
            return;
        }

        // Step screen.
        int n = engine.StepIndex + 1, total = p.steps.Length;
        var step = engine.CurrentStep;

        cardLabel.text = $"STEP {n} OF {total}";
        cardText.text = step.text;
        progressFill.style.width = Length.Percent(100f * n / total);

        Show(progress, true);
        Show(back, n > 1);
        Show(exit, true);
        Show(menu, false);

        SetInvisible(repeat, string.IsNullOrEmpty(step.animation));
        SetInvisible(done, false);
        done.text = n == total ? "Finish" : "Done";

        Show(banner, !visible);
        bannerText.text = SearchHint(step.target);
    }

    static string SearchHint(string target)
    {
        switch (target)
        {
            case "FaceDown": return "Looking for the device. Lay it face-down and point the camera at it.";
            case "FaceUp": return "Looking for the device. Lay it face-up and point the camera at it.";
            default: return "Looking for the device. Point the camera at it.";
        }
    }

    static void Show(VisualElement e, bool show) => e.EnableInClassList("hidden", !show);

    // Invisible but still takes its space, so the mic stays in the middle.
    static void SetInvisible(VisualElement e, bool invisible) => e.EnableInClassList("invisible", invisible);

    /// <summary>Keeps the UI away from the notch and the rounded corners of the phone.</summary>
    void ApplySafeArea()
    {
        if (root?.panel == null) return;

        Rect sa = Screen.safeArea;
        lastSafeArea = sa;

        Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(root.panel,
            new Vector2(sa.xMin, Screen.height - sa.yMax));
        Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(root.panel,
            new Vector2(Screen.width - sa.xMax, sa.yMin));

        root.style.paddingTop = topLeft.y;
        root.style.paddingLeft = topLeft.x;
        root.style.paddingRight = bottomRight.x;
        root.style.paddingBottom = bottomRight.y;
    }
}
