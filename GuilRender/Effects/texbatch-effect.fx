#if OPENGL
#define VS_PROFILE vs_3_0
#define PS_PROFILE ps_3_0
#define SV_POSITION POSITION
#define SV_Target COLOR0
#else
#define VS_PROFILE vs_4_0
#define PS_PROFILE ps_4_0
#define SV_POSITION SV_Position
#define SV_Target SV_Target0
#endif

// --- Parameters ---

float4x4 Projection;

sampler2D TexSampler : register(s0);

// --- Structs ---

struct VertexInput
{
    float2 Position : POSITION0;
    float4 Color : COLOR0;
    float2 TexCoords : TEXCOORD0;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TexCoords : TEXCOORD0;
};

// --- Vertex Shader ---

PixelInput VS(VertexInput input)
{
    PixelInput output;
    output.Position = mul(float4(input.Position, 0.0, 1.0), Projection);
    output.Color = input.Color;
    output.TexCoords = input.TexCoords;
    return output;
}

// --- Pixel Shader ---

float4 PS(PixelInput input) : SV_Target
{
    float4 texColor = tex2D(TexSampler, input.TexCoords);
    return texColor * input.Color;
}

// --- Technique ---

technique Texbatch
{
    pass Pass0
    {
        VertexShader = compile VS_PROFILE VS();
        PixelShader = compile PS_PROFILE PS();
    }
}