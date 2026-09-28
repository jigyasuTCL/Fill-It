Shader "Custom/SpinWheelShader"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Wheel Texture", 2D) = "white" {}
        _SegmentCount ("Segment Count", Range(2, 24)) = 6
        _LineColor ("Divider Line Color", Color) = (1, 1, 1, 1)
        _LineWidth ("Divider Line Width (radians)", Range(0.0005, 0.1)) = 0.01
        _Rotation ("Rotation Offset (Degrees)", Range(0, 360)) = 0
        [Toggle(TILE_PER_SEGMENT)] _TilePerSegment ("Tile Texture Per Segment (off = one image across whole wheel)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature TILE_PER_SEGMENT
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            int _SegmentCount;
            fixed4 _LineColor;
            float _LineWidth;
            float _Rotation;

            #define PI 3.14159265359
            #define TWO_PI 6.28318530718

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; // raw 0..1 UV, kept separate from _MainTex_ST so tiling math below stays predictable
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ---- Polar coordinates from UV, centered on the wheel ----
                float2 centered = i.uv - 0.5;
                float radius = length(centered) * 2.0; // 0 at center, 1 at the rim

                // Outside the circle (the square UV's corners) -> fully transparent,
                // with a 1-pixel-ish soft edge so the rim isn't jagged.
                float edgeSoftness = fwidth(radius);
                float circleMask = 1.0 - smoothstep(1.0 - edgeSoftness, 1.0 + edgeSoftness, radius);
                if (circleMask <= 0.0)
                    return fixed4(0, 0, 0, 0);

                float angle = atan2(centered.y, centered.x) + PI;      // 0 .. 2*PI
                angle += radians(_Rotation);                           // optional spin offset driven from script
                angle = fmod(angle, TWO_PI);
                if (angle < 0) angle += TWO_PI;

                // ---- THIS is where the circle gets divided into segments ----
                float segmentAngle = TWO_PI / _SegmentCount;
                float angleInSegment = fmod(angle, segmentAngle);
                int segmentIndex = (int)floor(angle / segmentAngle);

                // ---- Texture sampling ----
                #if TILE_PER_SEGMENT
                    // Map each slice's local angle/radius to its own 0..1 UV,
                    // so one small texture repeats identically in every segment.
                    float2 segmentUV = float2(angleInSegment / segmentAngle, radius);
                    fixed4 texColor = tex2D(_MainTex, segmentUV);
                #else
                    // One image stretched across the whole wheel (typical for
                    // a pre-baked wheel sprite) — sampled with plain UVs.
                    fixed4 texColor = tex2D(_MainTex, i.uv);
                #endif

                // ---- Divider line color ----
                // Distance (in radians) from this pixel's angle to the nearest
                // segment boundary; anything closer than _LineWidth gets painted
                // with _LineColor instead of the texture.
                float distToBoundary = min(angleInSegment, segmentAngle - angleInSegment);
                float lineSoftness = fwidth(distToBoundary);
                float lineMask = 1.0 - smoothstep(_LineWidth - lineSoftness, _LineWidth + lineSoftness, distToBoundary);

                fixed4 finalColor = lerp(texColor, _LineColor, lineMask);
                finalColor.a *= circleMask;

                return finalColor;
            }
            ENDCG
        }
    }

    FallBack "Transparent/Diffuse"
}
