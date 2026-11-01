// Shrinks the old image from its edges, grows the new one from its center (EdgeRevealTransition in Core). Drawn by FadeGraphic, see UIConstraintsFade.cginc
Shader "UIConstraints/Fade/EdgeReveal" {
	Properties {
		[PerRendererData] _MainTex ("New Image", 2D) = "white" {}
		_OldTex ("Old Image", 2D) = "black" {}
		_Progress ("Progress", Range(0, 1)) = 0
		_OldDistance ("Old Distance Field", 2D) = "black" {}
		_NewDistance ("New Distance Field", 2D) = "black" {}
		_OldProgress ("Old Progress", Range(0, 1)) = 0
		_NewProgress ("New Progress", Range(0, 1)) = 0
		_Softness ("Softness", Float) = 0.03

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

			float _OldProgress;
			float _NewProgress;
			float _Softness;

			fixed4 Blend(FadePoint p) {
				// the depth from which on the images are visible: rises through the old image, falls through the new one
				float oldThreshold = _OldProgress * (1 + 2 * _Softness) - _Softness;
				float newThreshold = (1 - _NewProgress) * (1 + 2 * _Softness) - _Softness;
				float oldWeight = p.oldColor.a * smoothstep(oldThreshold - _Softness, oldThreshold + _Softness, p.oldDistance);
				float newWeight = p.newColor.a * smoothstep(newThreshold - _Softness, newThreshold + _Softness, p.newDistance);
				return FadeMix(p.oldColor, oldWeight, p.newColor, newWeight, max(oldWeight, newWeight));
			}

			fixed4 Fragment(FadeVaryings i) : SV_Target {
				return FinishFade(Blend(SampleFade(i)), i);
			}
			ENDCG
		}
	}
}
