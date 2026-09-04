// A stand-in for one of lilToon's VARIANT shaders. Real avatars almost never
// use the plain "lilToon" name that the sibling stand-in provides: an outfit
// on lilToon with an outline reports "Hidden/lilToonOutline", a transparent
// face "Hidden/lilToonTransparent", and so on across 63 other names. The
// allowlist matched "lilToon" exactly, so all of them were silently skipped
// and whole avatars recorded no shader settings at all.
//
// This lives in TestProject, NOT in the package: shipping shaders that claim
// lilToon's registered names would collide with the real ones.
//
// Only the shape matters here, not the shading -- ShaderPropertyMap reads the
// declared Color/Float/Range properties off the Shader object, so a handful of
// each is enough to exercise capture, diff, duplication and re-apply.
Shader "Hidden/lilToonOutline"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainColor ("Alternate Main Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Metallic ("Metallic", Float) = 0.0
        // A texture property: deliberately NOT captured (asset references need
        // GUID handling), so its presence pins that exclusion.
        _MainTex ("Main Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            float4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
