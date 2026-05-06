using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Camera))]
public class Ink : MonoBehaviour
{
    // ---------- Shader & Texture Resources ----------
    public Shader inkShader;
    public Texture paperTexture;
    public Texture inkTexture;
 public Texture blueNoise;

    // Edge detection operator selection (mapped to shader pass index 1~4)
    public enum EdgeDetector
    {
contrast = 1,
        sobelFeldman = 2,
        prewitt = 3,
        dog = 4  // Difference of Gaussians edge detection
    }
    public EdgeDetector edgeDetector = EdgeDetector.sobelFeldman;

    // Threshold used by the contrast edge detector
    [Range(0.01f, 1.0f)]
    public float contrastThreshold = 0.5f;

    [Header("DoG")]
    // Base Gaussian sigma
    [Range(0.3f, 3.0f)] public float dogSigma = 1.0f;
    // Sigma multiplier between the two Gaussians
    [Range(1.1f, 3.0f)] public float dogK = 1.6f;
    // Output gain applied to the DoG response
    [Range(1.0f, 100.0f)] public float dogGain = 20.0f;

    // Global luminance contrast / correction (applied in luminance pass)
    [Range(0.01f, 5.0f)]
    public float luminanceContrast = 1.0f;

    [Range(1.0f, 10.0f)]
    public float luminanceCorrection = 1.0f;

    // Stipple dot size in screen-space
    [Range(0.01f, 1.0f)]
    public float stippleSize = 1.0f;

    [Header("Ink Bleed")]
    [Range(0.0f, 3.0f)] public float bleedAmount = 1.2f;
    [Range(0.0f, 30.0f)] public float bleedRadius = 10.0f;
    [Range(0.0f, 1.0f)] public float bleedIrregularity = 0.8f;
    [Range(1, 3)] public int bleedIterations = 2;
    [Range(0.5f, 3.0f)] public float bleedDensity = 1.5f;

    [Header("Temporal Coherence (World-Space Anchored Noise)")]
    [Tooltip("Stipple tiling density in world space")]
    [Range(0.1f, 20.0f)] public float stippleWorldScale = 4.0f;
    [Tooltip("Bleed noise tiling density in world space")]
 [Range(0.1f, 20.0f)] public float bleedWorldScale = 1.5f;

    [Header("Ink Bleed - Dark Edge (Diffuse Dark Edges Only)")]
 [Tooltip("If enabled, only dark edges bleed; otherwise all edges bleed")]
    public bool bleedDarkOnly = true;
    [Tooltip("Pixels darker than this luminance participate in bleeding. 1 = all bleed, 0 = none")]
[Range(0.0f, 1.0f)] public float bleedDarkThreshold = 0.45f;
    [Tooltip("Width of the dark mask transition band; larger = softer falloff")]
    [Range(0.01f, 0.5f)] public float bleedDarkSoftness = 0.15f;

 [Tooltip("Partial-selection threshold. 0 ~= every contour bleeds, 1 ~= none; creates a broken/irregular look")]
    [Range(0.0f, 1.0f)] public float bleedPartialThreshold = 0.35f;

    [Header("Ink Bleed - Fade (Opacity Compositing)")]
    [Tooltip("Curve treating bleed as ink opacity. 1 = linear, >1 = strong edges fading quickly to transparent center, <1 = softer transition")]
    [Range(0.2f, 5.0f)] public float bleedFadeGamma = 1.5f;
    [Tooltip("Debug: output the raw bleed grayscale to inspect the diffusion range")]
    public bool bleedDebug = false;

    // If true, save a screenshot every frame
    public bool capturing = false;

[Tooltip("Master switch for the post-processing effect. Toggle at runtime with Space.")]
    public bool postProcessingEnabled = true;

    [Header("Debug — Single-Pass Preview")]
    [Tooltip("Final = full pipeline; other options blit the corresponding intermediate RT directly to the screen")]
    public DebugStage debugStage = DebugStage.Final;

 // Intermediate render targets that can be previewed for debugging
    public enum DebugStage
    {
        Final = 0,// Normal output
        Luminance,  // Pass 0 output
    Edge,              // Pass 1~4 output (depends on edgeDetector)
      InkBleed,       // Pass 6 output (after N bleed iterations on edge)
        Stipple,           // Pass 5 output
   CombineNoBleed,  // Pass 7 input: edge without bleed + stipple
    }

    private Material inkMaterial;
    private int frameCount = 0;

    // ---------- Shader pass indices ----------
    const int PASS_LUMINANCE = 0;
    const int PASS_STIPPLE = 5;
    // Pass order: Ink Bleed = 6, Combine + Final = 7
    const int PASS_INK_BLEED = 6;
  const int PASS_FINAL = 7;

    void OnEnable()
    {
        // Lazily build the runtime material from the assigned shader
        if (inkMaterial == null)
        {
            inkMaterial = new Material(inkShader);
            inkMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    void OnDisable()
    {
        inkMaterial = null;
    }

    void Start()
    {
      // The ink shader samples the depth texture, so make sure the camera generates it
      Camera cam = GetComponent<Camera>();
     cam.depthTextureMode = cam.depthTextureMode | DepthTextureMode.Depth;
    }

    void Update()
    {
   ++frameCount;

        // Switch edge detector with number keys 1/2/3/4
        HandleEdgeDetectorHotkeys();

        // Toggle post-processing on/off with Space
        if (IsTogglePPKeyPressed())
   postProcessingEnabled = !postProcessingEnabled;
    }

    // Hotkey handler: 1=contrast, 2=Sobel-Feldman, 3=Prewitt, 4=DoG
    private void HandleEdgeDetectorHotkeys()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
    if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame) edgeDetector = EdgeDetector.contrast;
        else if (kb.digit2Key.wasPressedThisFrame) edgeDetector = EdgeDetector.sobelFeldman;
        else if (kb.digit3Key.wasPressedThisFrame) edgeDetector = EdgeDetector.prewitt;
        else if (kb.digit4Key.wasPressedThisFrame) edgeDetector = EdgeDetector.dog;
#else
        if (Input.GetKeyDown(KeyCode.Alpha1)) edgeDetector = EdgeDetector.contrast;
   else if (Input.GetKeyDown(KeyCode.Alpha2)) edgeDetector = EdgeDetector.sobelFeldman;
   else if (Input.GetKeyDown(KeyCode.Alpha3)) edgeDetector = EdgeDetector.prewitt;
  else if (Input.GetKeyDown(KeyCode.Alpha4)) edgeDetector = EdgeDetector.dog;
#endif
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
   // Master switch: when disabled, just copy source to destination unchanged
        if (!postProcessingEnabled || inkMaterial == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        // ---------- General shader uniforms ----------
        inkMaterial.SetFloat("_EdgeThreshold", contrastThreshold);
        inkMaterial.SetFloat("_LuminanceContrast", luminanceContrast);
        inkMaterial.SetFloat("_LuminanceCorrection", luminanceCorrection);

   // ---------- Textures ----------
        inkMaterial.SetTexture("_TexNoise", blueNoise);

        // ---------- Stipple parameters ----------
        inkMaterial.SetFloat("_StippleSize", stippleSize);
        inkMaterial.SetFloat("_StippleWorldScale", stippleWorldScale);

        // ---------- Bleed parameters ----------
 // Clamp to safe values to avoid divide-by-zero / no-op iterations in shader
        float safeDensity = Mathf.Max(0.5f, bleedDensity);
        int safeIter = Mathf.Max(1, bleedIterations);

        inkMaterial.SetFloat("_BleedStrength", bleedAmount);
        inkMaterial.SetFloat("_BleedRadius", bleedRadius);
  inkMaterial.SetFloat("_BleedIrregularity", bleedIrregularity);
      inkMaterial.SetFloat("_BleedDensity", safeDensity);
        inkMaterial.SetFloat("_BleedWorldScale", bleedWorldScale);
    inkMaterial.SetFloat("_BleedDarkOnly", bleedDarkOnly ? 1f : 0f);
        inkMaterial.SetFloat("_BleedDarkThreshold", bleedDarkThreshold);
        inkMaterial.SetFloat("_BleedDarkSoftness", bleedDarkSoftness);
        inkMaterial.SetFloat("_BleedPartialThreshold", bleedPartialThreshold);
        inkMaterial.SetFloat("_BleedFadeGamma", bleedFadeGamma);
     inkMaterial.SetFloat("_BleedDebug", bleedDebug ? 1f : 0f);

        // ---------- DoG parameters ----------
        inkMaterial.SetFloat("_DoGSigma", dogSigma);
inkMaterial.SetFloat("_DoGK", dogK);
        inkMaterial.SetFloat("_DoGGain", dogGain);

        // ---------- Camera matrices (used to reconstruct world position from depth) ----------
     Camera cam = GetComponent<Camera>();
   Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
 Matrix4x4 viewProj = gpuProj * cam.worldToCameraMatrix;
   inkMaterial.SetMatrix("_InvViewProj", viewProj.inverse);

int width = source.width;
        int height = source.height;
        RenderTextureFormat fmt = source.format;

   // --- Pass 0: Luminance ---
      // Convert source color into a contrast-corrected grayscale buffer
        RenderTexture luminanceRT = RenderTexture.GetTemporary(width, height, 0, fmt);
        Graphics.Blit(source, luminanceRT, inkMaterial, PASS_LUMINANCE);

   if (debugStage == DebugStage.Luminance)
        {
   Graphics.Blit(luminanceRT, destination);
            RenderTexture.ReleaseTemporary(luminanceRT);
         return;
        }

        // --- Pass 1~4: Edge Detection (operator selected by edgeDetector) ---
        RenderTexture edgeRT = RenderTexture.GetTemporary(width, height, 0, fmt);
        Graphics.Blit(luminanceRT, edgeRT, inkMaterial, (int)edgeDetector);

        if (debugStage == DebugStage.Edge)
  {
            Graphics.Blit(edgeRT, destination);
     RenderTexture.ReleaseTemporary(luminanceRT);
  RenderTexture.ReleaseTemporary(edgeRT);
      return;
      }

   // Ink bleed needs the original luminance image as a dark-area gate
   inkMaterial.SetTexture("_TexLuminance", luminanceRT);

      // --- Pass 5: Stipple (computed early so CombineNoBleed debug can reuse it) ---
RenderTexture stippleRT = RenderTexture.GetTemporary(width, height, 0, fmt);
        Graphics.Blit(luminanceRT, stippleRT, inkMaterial, PASS_STIPPLE);

        if (debugStage == DebugStage.Stipple)
        {
            Graphics.Blit(stippleRT, destination);
RenderTexture.ReleaseTemporary(luminanceRT);
            RenderTexture.ReleaseTemporary(edgeRT);
            RenderTexture.ReleaseTemporary(stippleRT);
            return;
        }

        // --- Debug: run the final pass without applying ink bleed ---
        if (debugStage == DebugStage.CombineNoBleed)
        {
inkMaterial.SetTexture("_TexStipple", stippleRT);
   inkMaterial.SetTexture("_TexInk", inkTexture);
     inkMaterial.SetTexture("_TexPaper", paperTexture);
     Graphics.Blit(edgeRT, destination, inkMaterial, PASS_FINAL);
         RenderTexture.ReleaseTemporary(luminanceRT);
      RenderTexture.ReleaseTemporary(edgeRT);
      RenderTexture.ReleaseTemporary(stippleRT);
            return;
   }

     // --- Pass 6: Ink Bleed (ping-pong over multiple iterations) ---
        if (bleedAmount > 0.001f && bleedRadius > 0.001f && blueNoise != null)
        {
            RenderTexture src = edgeRT;
       for (int it = 0; it < safeIter; ++it)
    {
         RenderTexture dst = RenderTexture.GetTemporary(width, height, 0, fmt);
          Graphics.Blit(src, dst, inkMaterial, PASS_INK_BLEED);
  // Release previous intermediate, but keep the original edgeRT until the loop ends
     if (src != edgeRT) RenderTexture.ReleaseTemporary(src);
           src = dst;
            }
          RenderTexture.ReleaseTemporary(edgeRT);
  edgeRT = src;
   }

        if (debugStage == DebugStage.InkBleed)
   {
            Graphics.Blit(edgeRT, destination);
        RenderTexture.ReleaseTemporary(luminanceRT);
   RenderTexture.ReleaseTemporary(edgeRT);
            RenderTexture.ReleaseTemporary(stippleRT);
         return;
        }

      // Luminance buffer is no longer needed past this point
     RenderTexture.ReleaseTemporary(luminanceRT);

   inkMaterial.SetTexture("_TexStipple", stippleRT);

        // --- Pass 7: Final composite (Combine + Color in a single pass) ---
     inkMaterial.SetTexture("_TexInk", inkTexture);
        inkMaterial.SetTexture("_TexPaper", paperTexture);
        Graphics.Blit(edgeRT, destination, inkMaterial, PASS_FINAL);

        RenderTexture.ReleaseTemporary(edgeRT);
        RenderTexture.ReleaseTemporary(stippleRT);
    }

    // Sanitize values when edited in the Inspector or when a scene is loaded
    private void OnValidate()
    {
        if (bleedDensity < 0.5f) bleedDensity = 1.5f;
        if (bleedIterations < 1) bleedIterations = 2;

     // Backward compatibility: remap any invalid/legacy edgeDetector value to Sobel
        int ev = (int)edgeDetector;
        if (ev != 1 && ev != 2 && ev != 3 && ev != 4)
        edgeDetector = EdgeDetector.sobelFeldman;
  }

    // Space key handler (toggles post-processing); supports both old and new input systems
    private static bool IsTogglePPKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        return kb != null && kb.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }
}
