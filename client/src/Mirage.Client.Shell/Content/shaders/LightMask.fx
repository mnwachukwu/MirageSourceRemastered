// Multiplies a light halo by its reach mask.
//
// The mask holds one texel per TILE — whether the light reaches it — and is sampled with linear filtering,
// so the step between a lit tile and a shadowed one arrives as a ramp across a tile's width instead of a
// 32px stair. The halo itself is unchanged: the gradient is still the baked sprite's.
//
// MaskScale/MaskOffset map the halo quad's own 0..1 coordinates onto the mask's rectangle, which is
// anchored to the tile the occlusion was traced from rather than to the halo. That is what lets a halo
// slide sub-tile with a walking emitter while its shadows stay nailed to the world.
//
// PIXEL-SHADER ONLY — SpriteBatch supplies the vertex stage. Built through Content.mgcb (DesktopGL/Reach).
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float2 MaskScale;
float2 MaskOffset;
// The tile a moving emitter is entering, and how far into the step it is. Reach is answered per tile, so
// one mask can only change in a jump at a border; blending the two makes it continuous. Zero for anything
// standing still, which leaves the second sample doing nothing.
float2 IntoScale;
float2 IntoOffset;
float MaskBlend;

Texture2D SpriteTexture;
sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

Texture2D MaskTexture;
sampler2D MaskTextureSampler = sampler_state
{
    Texture = <MaskTexture>;
    Filter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

Texture2D IntoTexture;
sampler2D IntoTextureSampler = sampler_state
{
    Texture = <IntoTexture>;
    Filter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float4 c = tex2D(SpriteTextureSampler, uv) * input.Color;
    // .r because the masks are Alpha8, which DesktopGL maps to GL_LUMINANCE — sampled as (L,L,L,1). A
    // DirectX build gets A8_UNORM instead, sampled as (0,0,0,a), and would need .a here or every light in
    // the game reads as fully occluded. See GameplayScreen.ReachTexture.
    float from = tex2D(MaskTextureSampler, uv * MaskScale + MaskOffset).r;
    float into = tex2D(IntoTextureSampler, uv * IntoScale + IntoOffset).r;
    // The light map accumulates additively and ignores source alpha for color, so the mask scales the RGB.
    c.rgb *= lerp(from, into, MaskBlend);
    return c;
}

technique SpriteDrawing
{
    pass P0
    {
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
