// What the shaders of image fades share (see ImageFadeTransition): the vertices of FadeGraphic, sampling both images, and the clipping and masking of UI shaders.
// A fade shader includes this, computes the color in Blend (like ImageFadeTransition.Blend in Core) and returns it through FinishFade, see EdgeReveal.shader.
#ifndef UICONSTRAINTS_FADE_INCLUDED
#define UICONSTRAINTS_FADE_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"

sampler2D _MainTex; // the new image
sampler2D _OldTex;
sampler2D _OldDistance;
sampler2D _NewDistance;
sampler2D _Noise;

// where each image is drawn: the vertices go from 0 to 1 across both images, (xy - offset) / size is 0 to 1 across one of them
float4 _OldMap;
float4 _NewMap;
// the part of the textures each image shows (offset, size), and the part their distance fields cover
float4 _OldUV;
float4 _NewUV;
float4 _OldArea;
float4 _NewArea;

float _Progress;

fixed4 _TextureSampleAdd;
float4 _ClipRect;
float _UIMaskSoftnessX;
float _UIMaskSoftnessY;
int _UIVertexColorAlwaysGammaSpace;

struct FadeVertex {
	float4 vertex : POSITION;
	fixed4 color : COLOR;
	float2 texcoord : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FadeVaryings {
	float4 vertex : SV_POSITION;
	fixed4 color : COLOR;
	float2 uv : TEXCOORD0;
	float4 mask : TEXCOORD1;
	UNITY_VERTEX_OUTPUT_STEREO
};

// what Blend gets, like FadePoint in Core
struct FadePoint {
	fixed4 oldColor;
	fixed4 newColor;
	float oldDistance;
	float newDistance;
	float noise;
};

FadeVaryings FadeVert(FadeVertex v) {
	FadeVaryings o;
	UNITY_SETUP_INSTANCE_ID(v);
	UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
	float4 position = UnityObjectToClipPos(v.vertex);
	o.vertex = position;
	o.uv = v.texcoord;

	if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace()) v.color.rgb = UIGammaToLinear(v.color.rgb);
	o.color = v.color;

	float2 pixelSize = position.w;
	pixelSize /= abs(float2(_ScreenParams.x * UNITY_MATRIX_P[0][0], _ScreenParams.y * UNITY_MATRIX_P[1][1]));
	float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
	o.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + pixelSize.xy));
	return o;
}

// one image at a point: its color (transparent outside of it) and the distance field
void SampleImage(sampler2D image, sampler2D distanceField, float2 uv, float4 map, float4 textureUV, float4 area, out fixed4 color, out float distance) {
	float2 position = (uv - map.xy) / map.zw;
	float2 textureCoordinates = textureUV.xy + position * textureUV.zw;
	color = tex2D(image, textureCoordinates) + _TextureSampleAdd;
	color.a *= all(position >= 0) && all(position <= 1) ? 1 : 0;
	distance = tex2D(distanceField, (textureCoordinates - area.xy) / area.zw).r;
}

FadePoint SampleFade(FadeVaryings i) {
	FadePoint point_;
	SampleImage(_OldTex, _OldDistance, i.uv, _OldMap, _OldUV, _OldArea, point_.oldColor, point_.oldDistance);
	SampleImage(_MainTex, _NewDistance, i.uv, _NewMap, _NewUV, _NewArea, point_.newColor, point_.newDistance);
	point_.noise = tex2D(_Noise, i.uv).r;
	return point_;
}

// the colors mixed by their weights (which already include their alpha), with the given alpha (FadeColor.Mix in Core)
fixed4 FadeMix(fixed4 first, float firstWeight, fixed4 second, float secondWeight, float alpha) {
	float total = firstWeight + secondWeight;
	if (total <= 0) return 0;
	return fixed4((first.rgb * firstWeight + second.rgb * secondWeight) / total, alpha);
}

// the tint of the image, and the clipping of masks
fixed4 FinishFade(fixed4 color, FadeVaryings i) {
	color *= i.color;

	#ifdef UNITY_UI_CLIP_RECT
	half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
	color.a *= m.x * m.y;
	#endif

	#ifdef UNITY_UI_ALPHACLIP
	clip(color.a - 0.001);
	#endif

	return color;
}

#endif
