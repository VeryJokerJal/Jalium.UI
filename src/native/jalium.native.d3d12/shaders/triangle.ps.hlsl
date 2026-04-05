struct PsInput
{
    float4 clipPos : SV_Position;
    float4 color   : COLOR0;
};

float4 main(PsInput input) : SV_Target
{
    if (input.color.a < 1.0 / 255.0) discard;
    return input.color;
}
