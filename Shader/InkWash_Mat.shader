Shader "NPR/IW_mat"
{
    Properties
    {
        [Header(Brush Stroke)]
        _InkColor       ("Ink Color", Color)              = (0.05, 0.05, 0.05, 1)
        _BrushNoise     ("Brush Noise (Greyscale)", 2D)   = "white" {}
        _StrokeWidth    ("Stroke Width", Range(0, 0.05))  = 0.012
        _StrokeJitter   ("Stroke Jitter", Range(0, 1))    = 0.6
        _StrokeZPush    ("Stroke Z Push", Range(0, 1))    = 0.15

        [Header(Wet Stroke (Pass 0) Flying White)]
        _WetNoiseScale  ("Wet Noise Scale", Range(0.1, 20)) = 2.0
        _WetCutoff      ("Wet Cutoff", Range(0, 1))         = 0.15
        _WetFeather     ("Wet Feather", Range(0, 0.5))      = 0.05

        [Header(Dry Stroke (Pass 1) Flying White)]
        _DryWidth       ("Dry Width Mul", Range(1, 2.5))    = 1.4
        _DryNoiseScale  ("Dry Noise Scale", Range(0.1, 20)) = 5.0
        _DryCutoff      ("Dry Cutoff", Range(0, 1))         = 0.55
        _DryFeather     ("Dry Feather", Range(0, 0.5))      = 0.08

        [Header(Ink Wash)]
        _InkRamp        ("Ink Ramp (1D)", 2D)                   = "white" {}
        _BrushTex       ("Brush Detail", 2D)                    = "white" {}
        _WashNoise      ("Wash Noise", 2D)                      = "white" {}
        _WashDistort    ("Wash UV Distortion", Range(0, 0.5))   = 0.15
        _BrushPower     ("Brush Detail Power", Range(0.1, 3))   = 0.6
        _BrushBlend     ("Brush Detail Blend", Range(0, 1))     = 0.6

        [Header(Smoothing)]
        [KeywordEnum(Off, Box, Anisotropic)] _Smooth ("Smoothing Mode", Float) = 1
        _SmoothRadius   ("Smooth Radius", Range(0, 0.05))       = 0.012

        [Header(Rim)]
        [Toggle(_RIM_ON)] _UseRim ("Enable Rim", Float) = 0
        _RimColor       ("Rim Color", Color)            = (1, 1, 1, 1)
        _RimPower       ("Rim Power", Range(0.1, 10))   = 3.0
        _RimGain        ("Rim Gain", Range(0, 2))       = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Shared stroke helpers (used by Pass 0 & 1)
        CGINCLUDE
        #include "UnityCG.cginc"

        float4    _InkColor;
        sampler2D _BrushNoise;
        float4    _BrushNoise_ST;
        float     _StrokeWidth;
        float     _StrokeJitter;
        float     _StrokeZPush;

        float     _WetNoiseScale;
        float     _WetCutoff;
        float     _WetFeather;

        float     _DryWidth;
        float     _DryNoiseScale;
        float     _DryCutoff;
        float     _DryFeather;

        struct StrokeIn
        {
            float4 pos    : POSITION;
            float3 normal : NORMAL;
            float2 uv     : TEXCOORD0;
        };

        struct StrokeOut
        {
            float4 cs       : SV_POSITION;
            float2 uvObject : TEXCOORD0;    // model UV (for jitter)
            float2 uvWorld  : TEXCOORD1;    // world-space triplanar UV (for discard)
        };

        // Build a stroke vertex in view space:
        StrokeOut BuildStroke(StrokeIn v, float widthMul, float noiseScale)
        {
            StrokeOut o;

            // --- Per-vertex jitter (object-space UV based) -------------
            float2 nUV   = v.uv * _BrushNoise_ST.xy + _BrushNoise_ST.zw;
            float4 brush = tex2Dlod(_BrushNoise, float4(nUV, 0, 0));
            float  jit   = lerp(1.0 - _StrokeJitter, 1.0 + _StrokeJitter, brush.r);

            // --- Stroke geometry in view space -------------------------
            float3 nWS = UnityObjectToWorldNormal(v.normal);
            float3 nVS = mul((float3x3) UNITY_MATRIX_V, nWS);
            float4 pVS = mul(UNITY_MATRIX_MV, v.pos);

            float dist = max(0.001, -pVS.z);
            float w    = _StrokeWidth * widthMul * jit * dist;

            pVS.xy += nVS.xy * w;
            pVS.z  -= _StrokeZPush * w;

            o.cs = mul(UNITY_MATRIX_P, pVS);

            //   Triplanar world-space UV for noise discard
            float3 wp = mul(unity_ObjectToWorld, v.pos).xyz;
            o.uvWorld = (wp.xy + wp.yz + wp.xz) * noiseScale;

            o.uvObject = v.uv;
            return o;
        }
        ENDCG


        // Pass 0 - Solid Outline
        Pass
        {
            Name "WET_STROKE"
            // Process Rules by ShaderLab
            Cull Front
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            StrokeOut vert(StrokeIn v)
            {
                return BuildStroke(v, 1.0, _WetNoiseScale);
            }

            fixed4 frag(StrokeOut i) : SV_Target
            {
                // Sparse discard: low cutoff means most pixels survive,only a few small breaks appear in the wet stroke.
                float n = tex2D(_BrushNoise, i.uvWorld).r;
                clip(n - _WetCutoff);
                return _InkColor;
            }
            ENDCG
        }

        // Pass 1: Flying white simulate
        Pass
        {
            Name "DRY_STROKE"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Offset 20, 0
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            StrokeOut vert(StrokeIn v)
            {
                return BuildStroke(v, _DryWidth, _DryNoiseScale);
            }

            fixed4 frag(StrokeOut i) : SV_Target
            {
                float n = tex2D(_BrushNoise, i.uvWorld).g;

                float a = smoothstep(_DryCutoff - _DryFeather,
                                     _DryCutoff + _DryFeather, n);

                clip(a - 0.01);
                return fixed4(_InkColor.rgb, a);
            }
            ENDCG
        }

        // Pass 2 - Ink Wash Surface
        Pass
        {
            Name "INK_WASH"
            Tags { "LightMode" = "ForwardBase" }
            Cull Back

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase
            #pragma multi_compile _SMOOTH_OFF _SMOOTH_BOX _SMOOTH_ANISOTROPIC
            #pragma multi_compile _ _RIM_ON

            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            sampler2D _InkRamp;   float4 _InkRamp_ST;
            sampler2D _BrushTex;  float4 _BrushTex_ST;
            sampler2D _WashNoise;
            float     _WashDistort;
            float     _BrushPower;
            float     _BrushBlend;
            float     _SmoothRadius;
            float4    _RimColor;
            float     _RimPower;
            float     _RimGain;

            struct WashIn
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct WashOut
            {
                float4 pos : SV_POSITION;
                float2 uvR : TEXCOORD0;
                float2 uvB : TEXCOORD1;
                float3 nWS : TEXCOORD2;
                float3 pWS : TEXCOORD3;
                SHADOW_COORDS(4)
            };

            WashOut vert(WashIn v)
            {
                WashOut o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvR = TRANSFORM_TEX(v.uv, _InkRamp);
                o.uvB = TRANSFORM_TEX(v.uv, _BrushTex);
                o.nWS = UnityObjectToWorldNormal(v.normal);
                o.pWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                TRANSFER_SHADOW(o);
                return o;
            }

            // 5-tap cross blur centred on uv
            float3 SampleRampBox(float2 uv)
            {
                float r  = _SmoothRadius;
                float3 c = tex2D(_InkRamp, uv).rgb * 0.40;
                c += tex2D(_InkRamp, uv + float2( r, 0)).rgb * 0.15;
                c += tex2D(_InkRamp, uv + float2(-r, 0)).rgb * 0.15;
                c += tex2D(_InkRamp, uv + float2( 0, r)).rgb * 0.15;
                c += tex2D(_InkRamp, uv + float2( 0,-r)).rgb * 0.15;
                return c;
            }

            // Mimicking ink bleed
            float3 SampleRampAniso(float2 uv)
            {
                float r  = _SmoothRadius;
                float3 c = tex2D(_InkRamp, uv).rgb;
                c += tex2D(_InkRamp, uv + float2( r * 0.5, 0)).rgb;
                c += tex2D(_InkRamp, uv + float2(-r * 0.5, 0)).rgb;
                c += tex2D(_InkRamp, uv + float2( r,       0)).rgb;
                c += tex2D(_InkRamp, uv + float2(-r,       0)).rgb;
                return c / 5.0;
            }

            float3 SampleRamp(float2 uv)
            {
                #if defined(_SMOOTH_BOX)
                    return SampleRampBox(uv);
                #elif defined(_SMOOTH_ANISOTROPIC)
                    return SampleRampAniso(uv);
                #else
                    return tex2D(_InkRamp, uv).rgb;
                #endif
            }

            float4 frag(WashOut i) : SV_Target
            {
                float3 nWS = normalize(i.nWS);
                float3 lWS = normalize(UnityWorldSpaceLightDir(i.pWS));

                // Half-Lambert
                float NdL = dot(nWS, lWS) * 0.5 + 0.5;

                // Ramp UV with stroke-noise distortion 
                // Distort the ramp lookup with brush + wash noise so the tonal boundaries break up like real ink on paper.
                float2 brushXY = tex2D(_BrushTex,  i.uvB).xy - 0.5;
                float2 washXY  = tex2D(_WashNoise, i.uvR).xy - 0.5;
                float2 cuv     = float2(NdL, NdL)
                               + (brushXY + washXY) * _WashDistort;
                cuv = clamp(cuv, 0.01, 0.99);

                float3 ink = SampleRamp(cuv);

                //Brush detail
                float brush = pow(saturate(tex2D(_BrushTex, i.uvB).r),
                                  _BrushPower);
                ink *= lerp(1.0, brush, _BrushBlend);

                // Rim Light
                #ifdef _RIM_ON
                    float3 vWS = normalize(UnityWorldSpaceViewDir(i.pWS));
                    float  rim = pow(1.0 - saturate(dot(vWS, nWS)),
                                     _RimPower) * _RimGain;
                    ink = lerp(ink, _RimColor.rgb, saturate(rim));
                #endif

                return float4(ink, 1.0);
            }
            ENDCG
        }
    }

    FallBack "Diffuse"
}
