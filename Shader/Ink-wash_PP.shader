Shader "NPR/IW_PP"
{
    Properties
    {
        _MainTex ("Source Texture", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    // Unity reserved / auto-bound
    sampler2D _MainTex;
    sampler2D _CameraDepthTexture;
    float4 _MainTex_TexelSize;

    // Custom textures
    sampler2D _TexPaper;
    sampler2D _TexNoise;
    sampler2D _TexStipple;
    sampler2D _TexInk;
    sampler2D _TexLuminance;

    // General controls
    float _EdgeThreshold;
    float _LuminanceContrast;
    float _LuminanceCorrection;

    // Stippling
    float _StippleSize;
    float _StippleWorldScale;

    // Ink bleed
    float _BleedRadius;
    float _BleedStrength;
    float _BleedIrregularity;
    float _BleedDensity;
    float _BleedWorldScale;

    float _BleedPartialThreshold;
    float _BleedDarkThreshold;
    float _BleedDarkSoftness;
    float _BleedDarkOnly;
    float _BleedFadeGamma;
    float _BleedDebug;

    // DoG
    float _DoGSigma;
    float _DoGK;
    float _DoGGain;

    // Matrices
    float4x4 _InvViewProj;

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv     : TEXCOORD0;
    };

    struct v2f
    {
        float2 uv     : TEXCOORD0;
        float4 vertex : SV_POSITION;
        float4 screen : TEXCOORD1;
    };

    v2f vert(appdata v)
    {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        o.screen = ComputeScreenPos(o.vertex);
        return o;
    }

    float3 ReconstructWorldPosition(float2 uv)
    {
        float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
        float4 ndc = float4(uv * 2.0 - 1.0, rawDepth, 1.0);

        #if UNITY_UV_STARTS_AT_TOP
            ndc.y = -ndc.y;
        #endif

        float4 worldPos = mul(_InvViewProj, ndc);
        return worldPos.xyz / worldPos.w;
    }

    float GaussianWeight(float2 offset, float sigma)
    {
        float sigmaSquared = sigma * sigma;
        return exp(-dot(offset, offset) / (2.0 * sigmaSquared));
    }

    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // 0 - Luminance
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag(v2f i) : SV_Target
            {
                float luminance = LinearRgbToLuminance(tex2D(_MainTex, i.uv).rgb);
                return fixed4(luminance, luminance, luminance, luminance);
            }
            ENDCG
        }


        // 1 - Local Contrast Edge Detection (8-neighbour, relative contrast)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Sample the 3x3 neighbourhood and return its luminance min / max.
            void SampleMinMax3x3(float2 uv, out float lo, out float hi)
            {
                lo =  1e9;
                hi = -1e9;

                [unroll]
                for (int y = -1; y <= 1; ++y)
                {
                    [unroll]
                    for (int x = -1; x <= 1; ++x)
                    {
                        float l = tex2D(_MainTex, uv + _MainTex_TexelSize.xy * float2(x, y)).r;
                        lo = min(lo, l);
                        hi = max(hi, l);
                    }
                }
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float lo, hi;
                SampleMinMax3x3(i.uv, lo, hi);

                // Relative contrast
                float relative = (hi - lo) / (hi + lo +1e-4);

                // soft threshold (anti-alias)
                float aa = max(_EdgeThreshold *0.25,1e-4);
                float edge = smoothstep(_EdgeThreshold - aa,
                                       _EdgeThreshold + aa,
                                       relative);

                return fixed4(edge, edge, edge, edge);
            }
            ENDCG
        }

        // 2 - Sobel-Feldman Edge Detection
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag(v2f i) : SV_Target
            {
                int3x3 kernelX = { 1, 0, -1,
                                   2, 0, -2,
                                   1, 0, -1 };

                int3x3 kernelY = { 1,  2,  1,
                                   0,  0,  0,
                                  -1, -2, -1 };

                float gradientX = 0.0;
                float gradientY = 0.0;

                [unroll]
                for (int x = -1; x <= 1; ++x)
                {
                    [unroll]
                    for (int y = -1; y <= 1; ++y)
                    {
                        float2 sampleUV = i.uv + _MainTex_TexelSize.xy * float2(x, y);
                        float luminance = tex2D(_MainTex, sampleUV).r;

                        gradientX += kernelX[x + 1][y + 1] * luminance;
                        gradientY += kernelY[x + 1][y + 1] * luminance;
                    }
                }

                float magnitude = sqrt(gradientX * gradientX + gradientY * gradientY);
                float edge = magnitude > _EdgeThreshold ? 1.0 : 0.0;

                return fixed4(edge, edge, edge, edge);
            }
            ENDCG
        }


        // 3 - Compass Prewitt Edge Detection (8-direction, max response)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Convolution helper for a single 3x3 kernel against 9 luminance taps.
            float Convolve3x3(float l[9], int3x3 K)
            {
                float r = 0.0;
                r += K[0][0] * l[0]; r += K[0][1] * l[1]; r += K[0][2] * l[2];
                r += K[1][0] * l[3]; r += K[1][1] * l[4]; r += K[1][2] * l[5];
                r += K[2][0] * l[6]; r += K[2][1] * l[7]; r += K[2][2] * l[8];
                return r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Gather the 3x3 luminance window once.
                float l[9];
                [unroll]
                for (int yy = -1; yy <= 1; ++yy)
                {
                    [unroll]
                    for (int xx = -1; xx <= 1; ++xx)
                    {
                        float2 uv = i.uv + _MainTex_TexelSize.xy * float2(xx, yy);
                        l[(yy + 1) * 3 + (xx + 1)] = tex2D(_MainTex, uv).r;
                    }
                }

                // 4 unique compass orientations (N, NE, E, SE) – the other 4 are sign-flipped duplicates so abs() covers them.
                int3x3 KN  = { -1,-1,-1,
                                0, 0, 0,
                                1, 1, 1 };
                int3x3 KNE = { -1,-1, 0,
                               -1, 0, 1,
                                0, 1, 1 };
                int3x3 KE  = { -1, 0, 1,
                               -1, 0, 1,
                               -1, 0, 1 };
                int3x3 KSE = {  0, 1, 1,
                               -1, 0, 1,
                               -1,-1, 0 };

                float gN  = abs(Convolve3x3(l, KN));
                float gNE = abs(Convolve3x3(l, KNE));
                float gE  = abs(Convolve3x3(l, KE));
                float gSE = abs(Convolve3x3(l, KSE));

                // Compass operator = strongest direction.
                float magnitude = max(max(gN, gNE), max(gE, gSE));

                // Soft threshold for cleaner antialiased lines.
                float aa   = max(_EdgeThreshold * 0.25, 1e-4);
                float edge = smoothstep(_EdgeThreshold - aa,
                                       _EdgeThreshold + aa,
                                       magnitude);

                return fixed4(edge, edge, edge, edge);
            }
            ENDCG
        }

        // 4 - DoG Edge Detection
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            fixed4 frag(v2f i) : SV_Target
            {
                float sigmaSmall = max(0.3, _DoGSigma);
                float sigmaLarge = sigmaSmall * max(1.01, _DoGK);

                float blurSmall = 0.0;
                float blurLarge = 0.0;

                float weightSmallSum = 0.0;
                float weightLargeSum = 0.0;

                [unroll]
                for (int y = -3; y <= 3; ++y)
                {
                    [unroll]
                    for (int x = -3; x <= 3; ++x)
                    {
                        float2 offset = float2(x, y);
                        float2 sampleUV = i.uv + _MainTex_TexelSize.xy * offset;

                        float luminance = tex2D(_MainTex, sampleUV).r;

                        float weightSmall = GaussianWeight(offset, sigmaSmall);
                        float weightLarge = GaussianWeight(offset, sigmaLarge);

                        blurSmall += luminance * weightSmall;
                        blurLarge += luminance * weightLarge;

                        weightSmallSum += weightSmall;
                        weightLargeSum += weightLarge;
                    }
                }

                blurSmall /= weightSmallSum;
                blurLarge /= weightLargeSum;

                float response = abs(blurSmall - blurLarge) * max(1.0, _DoGGain);

                float threshold = _EdgeThreshold;
                float softness = max(threshold * 0.25, 1e-4);

                float edge = smoothstep(
                    threshold - softness,
                    threshold + softness,
                    response
                );

                return fixed4(edge, edge, edge, edge);
            }
            ENDCG
        }

        // 5 - Stippling
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                float luminance = tex2D(_MainTex, i.uv).r;

                float3 worldPos = ReconstructWorldPosition(i.uv);
                float2 noiseUV = (worldPos.xz + worldPos.yy) * _StippleWorldScale;
                noiseUV *= _StippleSize;

                float noise = tex2Dlod(_TexNoise, float4(noiseUV, 0, 0)).r;

                luminance = saturate(_LuminanceContrast * (luminance - 0.5) + 0.5);
                luminance = saturate(pow(luminance, 1.0 / _LuminanceCorrection));

                float stipple = luminance < noise ? 1.0 : 0.0;
                return float4(stipple, stipple, stipple, stipple);
            }
            ENDCG
        }

        // 6 - Ink Bleed
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 12-sample Poisson disk pattern.
            static const float2 PoissonDisk12[12] =
            {
                float2(-0.326, -0.406),
                float2(-0.840, -0.074),
                float2(-0.696,  0.457),
                float2(-0.203,  0.621),
                float2( 0.962, -0.195),
                float2( 0.473, -0.480),
                float2( 0.519,  0.767),
                float2( 0.185, -0.893),
                float2( 0.507,  0.064),
                float2( 0.896,  0.412),
                float2(-0.322, -0.933),
                float2(-0.792, -0.598)
            };

            float4 frag(v2f i) : SV_Target
            {
                float center = tex2D(_MainTex, i.uv).r;

                // Reconstruct world position so that the bleed noise is anchored in world space.
                float3 worldPos = ReconstructWorldPosition(i.uv);
                float2 worldUV = (worldPos.xz + worldPos.yy) * _BleedWorldScale;

                float macroNoise = tex2D(_TexNoise, worldUV * 0.6).r;
                float microNoise = tex2D(_TexNoise, worldUV * 7.3 + float2(0.37, 0.11)).r;

                float luminance = tex2D(_TexLuminance, i.uv).r;

                // Dark-region mask: optionally restrict bleeding to darker areas.
                float darkThreshold = saturate(_BleedDarkThreshold);
                float darkSoftness = max(0.01, _BleedDarkSoftness);

                float darkMask = 1.0 - smoothstep(
                    darkThreshold - darkSoftness,
                    darkThreshold + darkSoftness,
                    luminance
                );

                // If _BleedDarkOnly is disabled, the mask becomes 1.0 everywhere.
                darkMask = lerp(1.0, darkMask, step(0.5, _BleedDarkOnly));

                float noiseLow  = tex2D(_TexNoise, worldUV * 1.7 + float2(0.19, 0.83)).r;
                float noiseMid  = tex2D(_TexNoise, worldUV * 4.3 + float2(0.71, 0.29)).r;
                float noiseHigh = tex2D(_TexNoise, worldUV * 9.1 + float2(0.43, 0.57)).r;

                float partialNoise = noiseLow * 0.5 + noiseMid * 0.35 + noiseHigh * 0.15;

                float partialMask = smoothstep(
                    _BleedPartialThreshold,
                    saturate(_BleedPartialThreshold + 0.25),
                    partialNoise
                );

                float bleedMask = saturate(darkMask * partialMask);

                if (bleedMask < 0.01)
                {
                    return float4(center, center, center, center);
                }
                // Bleed radius is affected by strength, irregularity, and micro noise.
                float bleedStrength = max(0.0, _BleedStrength) * bleedMask;
                float normalizedStrength = saturate(bleedStrength / 3.0);

                float radius = _BleedRadius
                    * lerp(1.0 - _BleedIrregularity, 1.0 + _BleedIrregularity, microNoise)
                    * lerp(0.2, 0.9, normalizedStrength);

                float falloffPower = lerp(4.0, 1.6, normalizedStrength);

                float accumulatedBleed = center;
                // Diffusion sampling.
                [unroll]
                for (int k = 0; k < 12; ++k)
                {
                    float2 offset = PoissonDisk12[k];

                    float jitter = tex2D(_TexNoise, worldUV * 13.1 + offset * 0.27).r;
                    float sampleRadius = radius * (0.35 + jitter);

                    float sampledEdge = tex2D(
                        _MainTex,
                        i.uv + _MainTex_TexelSize.xy * offset * sampleRadius
                    ).r;

                    float falloff = pow(saturate(1.0 - length(offset)), falloffPower);
                    accumulatedBleed = max(accumulatedBleed, sampledEdge * falloff);
                }

                float bleed = pow(
                    saturate(accumulatedBleed * lerp(0.75, 1.15, macroNoise)),
                    1.0 / max(0.01, _BleedDensity)
                );

                bleed *= normalizedStrength;
                bleed = saturate(bleed + max(0.0, bleedStrength - 1.0) * bleed * 0.2);

                float opacity = pow(saturate(bleed), max(0.05, _BleedFadeGamma));
                opacity = saturate(opacity * lerp(0.9, 1.1, microNoise));

                float targetInkValue = 1.0;
                float target = lerp(center, targetInkValue, opacity);

                float result = lerp(center, target, bleedMask);
                // Edge fuzz adds small high-frequency variation to avoid clean digital edges.
                float edgeFuzz = tex2D(_TexNoise, worldUV * 25.0).r;

                if (result > 0.05 && result < 0.95)
                {
                    result = saturate(result * lerp(0.85, 1.1, edgeFuzz));
                }
                // Debug view for inspecting the raw bleed map.
                if (_BleedDebug > 0.5)
                {
                    return float4(bleed, bleed, bleed, 1.0);
                }

                return float4(result, result, result, result);
            }
            ENDCG
        }

        // 7 - Combine + Final Composition
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 frag(v2f i) : SV_Target
            {
                float edge = saturate(tex2D(_MainTex,    i.uv).r);
                float stip = saturate(tex2D(_TexStipple, i.uv).r);

                float depth = saturate(1.0 - Linear01Depth(
                    tex2D(_CameraDepthTexture, i.uv).r));

                // Skip sky/far clip: avoid stipple painting on empty geometry
                if (depth < 0.0001)
                {
                    stip = 0.0;
                }

                // Treat edge / stipple as two layers of ink opacity, perform "white paper - ink" multiplication
                float ink2 = 1.0 - (1.0 - edge) * (1.0 - stip);
                float mask = saturate(1.0 - ink2);    // 1=paper, 0=ink

                // Anti-aliasing strip
                float4 ink   = tex2D(_TexInk,   i.uv);
                float4 paper = tex2D(_TexPaper, i.uv);

                float t = smoothstep(0.85, 1.0, mask);
                return lerp(ink, paper, t);
            }
            ENDCG
        }
    }
}