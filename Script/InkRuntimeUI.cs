using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class InkRuntimeUI : MonoBehaviour
{
    [Tooltip("Optional explicit reference. If empty, will auto-find the Ink component in the scene.")]
    public Ink ink;

    [Tooltip("Ink-wash materials (NPR/IW_mat) to control. If empty, will auto-find them in the scene.")]
    public List<Material> inkBrushMaterials = new List<Material>();

    [Tooltip("Show the panel on start")]
    public bool visible = true;

    [Tooltip("Width of the panel in pixels")]
    public int panelWidth = 360;

    private Vector2 scroll;
    private Rect windowRect = new Rect(20, 20, 360, 600);
    private GUIStyle headerStyle;
    private bool stylesBuilt;

    // Tab selection
    private int activeTab = 0;
    private static readonly string[] kTabs = { "Post-Process", "Ink Brush Mat" };

    void Awake()
    {
        windowRect.width = panelWidth;
        TryResolveInk();
        TryResolveBrushMaterials();
    }

    void Update()
    {
        if (ink == null)
            TryResolveInk();

        if (IsToggleUIKeyPressed())
            visible = !visible;
    }

    private void TryResolveInk()
    {
        if (ink != null)
            return;

#if UNITY_2023_1_OR_NEWER
        ink = Object.FindFirstObjectByType<Ink>();
#else
        ink = Object.FindObjectOfType<Ink>();
#endif
    }

    private void TryResolveBrushMaterials()
    {
        if (inkBrushMaterials != null && inkBrushMaterials.Count > 0)
            return;

        inkBrushMaterials = new List<Material>();
        var seen = new HashSet<Material>();

#if UNITY_2023_1_OR_NEWER
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
#else
        var renderers = Object.FindObjectsOfType<Renderer>();
#endif
        foreach (var r in renderers)
        {
            // Use sharedMaterials
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; ++i)
            {
                var m = mats[i];
                if (m == null || m.shader == null) continue;
                if (m.shader.name == "NPR/IW_mat" && seen.Add(m))
                    inkBrushMaterials.Add(m);
            }
        }
    }

    void OnGUI()
    {
        if (!visible)
            return;

        BuildStylesIfNeeded();

        // Clamp panel inside screen
        windowRect.height = Mathf.Min(Screen.height - 40, 720);
        windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "Ink Settings (Tab)");
    }

    private void BuildStylesIfNeeded()
    {
        if (stylesBuilt)
            return;

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.4f) }
        };

        stylesBuilt = true;
    }

    private void DrawWindow(int id)
    {
        // Tab bar
        activeTab = GUILayout.Toolbar(activeTab, kTabs);
        GUILayout.Space(4);

        scroll = GUILayout.BeginScrollView(scroll);

        if (activeTab == 0)
            DrawPostProcessTab();
        else
            DrawInkBrushTab();

        GUILayout.EndScrollView();

        // Title bar drag
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private void DrawPostProcessTab()
    {
        if (ink == null)
        {
            GUILayout.Label("No Ink component found in scene.");
            return;
        }

        // ---- Master ----
        ink.postProcessingEnabled = GUILayout.Toggle(
            ink.postProcessingEnabled, " Post-Processing Enabled (Space)");

        GUILayout.Space(4);
        GUILayout.Label("Edge Detector (1/2/3/4)", headerStyle);
        DrawEdgeDetectorButtons();

        GUILayout.Space(4);
        GUILayout.Label("General", headerStyle);
        ink.contrastThreshold = Slider("Edge Threshold", ink.contrastThreshold, 0.01f, 1.0f);
        ink.luminanceContrast = Slider("Luminance Contrast", ink.luminanceContrast, 0.01f, 5.0f);
        ink.luminanceCorrection = Slider("Luminance Correction", ink.luminanceCorrection, 1.0f, 10.0f);

        GUILayout.Space(4);
        GUILayout.Label("DoG", headerStyle);
        ink.dogSigma = Slider("DoG Sigma", ink.dogSigma, 0.3f, 3.0f);
        ink.dogK = Slider("DoG K", ink.dogK, 1.1f, 3.0f);
        ink.dogGain = Slider("DoG Gain", ink.dogGain, 1.0f, 100.0f);

        GUILayout.Space(4);
        GUILayout.Label("Stipple", headerStyle);
        ink.stippleSize = Slider("Stipple Size", ink.stippleSize, 0.01f, 1.0f);
        ink.stippleWorldScale = Slider("Stipple World Scale", ink.stippleWorldScale, 0.1f, 20.0f);

        GUILayout.Space(4);
        GUILayout.Label("Ink Bleed", headerStyle);
        ink.bleedAmount = Slider("Amount", ink.bleedAmount, 0.0f, 3.0f);
        ink.bleedRadius = Slider("Radius", ink.bleedRadius, 0.0f, 30.0f);
        ink.bleedIrregularity = Slider("Irregularity", ink.bleedIrregularity, 0.0f, 1.0f);
        ink.bleedIterations = SliderInt("Iterations", ink.bleedIterations, 1, 3);
        ink.bleedDensity = Slider("Density", ink.bleedDensity, 0.5f, 3.0f);
        ink.bleedWorldScale = Slider("World Scale", ink.bleedWorldScale, 0.1f, 20.0f);

        GUILayout.Space(4);
        GUILayout.Label("Bleed - Dark Edge", headerStyle);
        ink.bleedDarkOnly = GUILayout.Toggle(ink.bleedDarkOnly, " Dark Only");
        ink.bleedDarkThreshold = Slider("Dark Threshold", ink.bleedDarkThreshold, 0.0f, 1.0f);
        ink.bleedDarkSoftness = Slider("Dark Softness", ink.bleedDarkSoftness, 0.01f, 0.5f);
        ink.bleedPartialThreshold = Slider("Partial Threshold", ink.bleedPartialThreshold, 0.0f, 1.0f);

        GUILayout.Space(4);
        GUILayout.Label("Bleed - Fade", headerStyle);
        ink.bleedFadeGamma = Slider("Fade Gamma", ink.bleedFadeGamma, 0.2f, 5.0f);
        ink.bleedDebug = GUILayout.Toggle(ink.bleedDebug, " Bleed Debug View");

        GUILayout.Space(4);
        GUILayout.Label("Debug Stage", headerStyle);
        DrawDebugStageButtons();
    }

    // Tab 1: Stylized/InkBrush material parameters
    private void DrawInkBrushTab()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(string.Format("Materials: {0}", inkBrushMaterials != null ? inkBrushMaterials.Count : 0));
        if (GUILayout.Button("Re-scan", GUILayout.Width(80)))
        {
            inkBrushMaterials.Clear();
            TryResolveBrushMaterials();
        }
        GUILayout.EndHorizontal();

        if (inkBrushMaterials == null || inkBrushMaterials.Count == 0)
        {
            GUILayout.Label("No 'NPR/IW_mat' materials found.");
            return;
        }

        GUILayout.Space(4);
        GUILayout.Label("Brush Stroke", headerStyle);
        MatSlider("_StrokeWidth", "Stroke Width", 0f, 0.05f);
        MatSlider("_StrokeJitter", "Stroke Jitter", 0f, 1f);
        MatSlider("_StrokeZPush", "Stroke Z Push", 0f, 1f);

        GUILayout.Space(4);
        GUILayout.Label("Wet Stroke", headerStyle);
        MatSlider("_WetNoiseScale", "Wet Noise Scale", 0.1f, 20f);
        MatSlider("_WetCutoff", "Wet Cutoff", 0f, 1f);
        MatSlider("_WetFeather", "Wet Feather", 0f, 0.5f);

        GUILayout.Space(4);
        GUILayout.Label("Dry Stroke (Flying White)", headerStyle);
        MatSlider("_DryWidth", "Dry Width Mul", 1f, 2.5f);
        MatSlider("_DryNoiseScale", "Dry Noise Scale", 0.1f, 20f);
        MatSlider("_DryCutoff", "Dry Cutoff", 0f, 1f);
        MatSlider("_DryFeather", "Dry Feather", 0f, 0.5f);

        GUILayout.Space(4);
        GUILayout.Label("Ink Wash", headerStyle);
        MatSlider("_WashDistort", "Wash UV Distort", 0f, 0.5f);
        MatSlider("_BrushPower", "Brush Detail Pow", 0.1f, 3f);
        MatSlider("_BrushBlend", "Brush Detail Blend", 0f, 1f);

        GUILayout.Space(4);
        GUILayout.Label("Smoothing", headerStyle);
        DrawSmoothModeButtons();
        MatSlider("_SmoothRadius", "Smooth Radius", 0f, 0.05f);

        GUILayout.Space(4);
        GUILayout.Label("Rim", headerStyle);
        DrawRimToggle();
        MatSlider("_RimPower", "Rim Power", 0.1f, 10f);
        MatSlider("_RimGain", "Rim Gain", 0f, 2f);
    }

    // ---------- Material helpers ----------
    private void MatSlider(string prop, string label, float min, float max)
    {
        if (inkBrushMaterials.Count == 0) return;
        Material first = FirstValidMat();
        if (first == null || !first.HasProperty(prop)) return;

        float current = first.GetFloat(prop);
        float v = Slider(label, current, min, max);
        if (!Mathf.Approximately(v, current))
        {
            foreach (var m in inkBrushMaterials)
                if (m != null && m.HasProperty(prop))
                    m.SetFloat(prop, v);
        }
    }

    private Material FirstValidMat()
    {
        foreach (var m in inkBrushMaterials)
            if (m != null) return m;
        return null;
    }

    private void DrawSmoothModeButtons()
    {
        Material first = FirstValidMat();
        if (first == null) return;

        int mode = Mathf.RoundToInt(first.HasProperty("_Smooth") ? first.GetFloat("_Smooth") : 1f);
        GUILayout.BeginHorizontal();
        ToggleButton("Off", mode == 0, () => SetSmoothMode(0));
        ToggleButton("Box", mode == 1, () => SetSmoothMode(1));
        ToggleButton("Aniso", mode == 2, () => SetSmoothMode(2));
        GUILayout.EndHorizontal();
    }

    private void SetSmoothMode(int mode)
    {
        foreach (var m in inkBrushMaterials)
        {
            if (m == null) continue;
            if (m.HasProperty("_Smooth")) m.SetFloat("_Smooth", mode);
            m.DisableKeyword("_SMOOTH_OFF");
            m.DisableKeyword("_SMOOTH_BOX");
            m.DisableKeyword("_SMOOTH_ANISOTROPIC");
            switch (mode)
            {
                case 0: m.EnableKeyword("_SMOOTH_OFF"); break;
                case 1: m.EnableKeyword("_SMOOTH_BOX"); break;
                case 2: m.EnableKeyword("_SMOOTH_ANISOTROPIC"); break;
            }
        }
    }

    private void DrawRimToggle()
    {
        Material first = FirstValidMat();
        if (first == null) return;

        bool on = first.IsKeywordEnabled("_RIM_ON");
        bool newOn = GUILayout.Toggle(on, " Enable Rim");
        if (newOn != on)
        {
            foreach (var m in inkBrushMaterials)
            {
                if (m == null) continue;
                if (m.HasProperty("_UseRim")) m.SetFloat("_UseRim", newOn ? 1f : 0f);
                if (newOn) m.EnableKeyword("_RIM_ON");
                else m.DisableKeyword("_RIM_ON");
            }
        }
    }

    // ---------- Generic UI helpers ----------
    private float Slider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
        GUILayout.Label(value.ToString("F3"), GUILayout.Width(56));
        GUILayout.EndHorizontal();
        return value;
    }

    private int SliderInt(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140));
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
        value = Mathf.RoundToInt(v);
        GUILayout.Label(value.ToString(), GUILayout.Width(56));
        GUILayout.EndHorizontal();
        return value;
    }

    private void DrawEdgeDetectorButtons()
    {
        GUILayout.BeginHorizontal();
        ToggleButton("Contrast", ink.edgeDetector == Ink.EdgeDetector.contrast,
            () => ink.edgeDetector = Ink.EdgeDetector.contrast);
        ToggleButton("Sobel", ink.edgeDetector == Ink.EdgeDetector.sobelFeldman,
            () => ink.edgeDetector = Ink.EdgeDetector.sobelFeldman);
        ToggleButton("Prewitt", ink.edgeDetector == Ink.EdgeDetector.prewitt,
            () => ink.edgeDetector = Ink.EdgeDetector.prewitt);
        ToggleButton("DoG", ink.edgeDetector == Ink.EdgeDetector.dog,
            () => ink.edgeDetector = Ink.EdgeDetector.dog);
        GUILayout.EndHorizontal();
    }

    private void DrawDebugStageButtons()
    {
        var values = System.Enum.GetValues(typeof(Ink.DebugStage));
        int perRow = 3;
        int i = 0;
        GUILayout.BeginHorizontal();

        foreach (Ink.DebugStage stage in values)
        {
            var captured = stage;
            ToggleButton(stage.ToString(), ink.debugStage == stage,
                () => ink.debugStage = captured);

            i++;
            if (i % perRow == 0)
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }
        }

        GUILayout.EndHorizontal();
    }

    private void ToggleButton(string label, bool active, System.Action onClick)
    {
        var prev = GUI.color;
        if (active)
            GUI.color = new Color(0.4f, 0.9f, 0.4f);

        if (GUILayout.Button(label))
            onClick();

        GUI.color = prev;
    }

    private static bool IsToggleUIKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null && kb.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }
}