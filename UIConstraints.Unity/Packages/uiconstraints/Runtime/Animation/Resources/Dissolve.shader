// Switches point by point in the order of the noise (DissolveTransition in Core). Drawn by FadeGraphic, see UIConstraintsFade.cginc
Shader "UIConstraints/Fade/Dissolve" {
	Properties {
		[PerRendererData] _MainTex ("New Image", 2D) = "white" {}
		_OldTex ("Old Image", 2D) = "black" {}
		_Progress ("Progress", Range(0, 1)) = 0
		_Noise ("Noise", 2D) = "black" {}
		_Softness ("Softness", Float) = 0.1

		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255
		_ColorMask ("Color Mask", Float) = 15
		[Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
	}

	SubShader {
		Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }

		Stencil {
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		Blend SrcAlpha OneMinusSrcAlpha
		ColorMask [_ColorMask]

		Pass {
			CGPROGRAM
			#pragma vertex FadeVert
			#pragma fragment Fragment
			#pragma target 2.0
			#pragma multi_compile_local _ UNITY_UI_CLIP_RECT
			#pragma multi_compile_local _ UNITY_UI_ALPHACLIP

			#include "UIConstraintsFade.cginc"

			float _Softness;

			fixed4 Blend(FadePoint p) {
				float switched = saturate((_Progress * (1 + _Softness) - p.noise) / _Softness);
				float oldWeight = p.oldColor.a * (1 - switched);
				float newWeight = p.newColor.a * switched;
				return FadeMix(p.oldColor, oldWeight, p.newColor, newWeight, oldWeight + newWeight);
			}

			fixed4 Fragment(FadeVaryings i) : SV_Target {
				return FinishFade(Blend(SampleFade(i)), i);
			}
			ENDCG
		}
	}
}
