// A stand-in for lilToon, named exactly "lilToon" so material.shader.name
// matches ShaderPropertyMap's allowlist and the supported-shader capture/apply
// path actually executes under CI.
//
// This lives in TestProject, NOT in the package: shipping a shader that claims
// the name "lilToon" would collide with the real one in a user's project.
//
// Only the shape matters here, not the shading -- ShaderPropertyMap reads the
// declared Color/Float/Range properties off the Shader object, so a handful of
// each is enough to exercise capture, diff, duplication and re-apply.
Shader "lilToon"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainColor ("Alternate Main Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Metallic ("Metallic", Float) = 0.0
        // Texture properties. lilToon's second/third layers are the ones
        // users notice going missing, so mirror that shape here.
        _MainTex ("Main Texture", 2D) = "white" {}
        _Main2ndTex ("2nd Layer Texture", 2D) = "white" {}
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
