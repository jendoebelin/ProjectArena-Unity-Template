// THE CUTAWAY (TASKS.md E42; native 7240d49 render.cpp uCut / uRoof, Renderer::setCut; Runtime/Cutaway.cs): from a PC
// spectator's preset camera outside the glass, what lies within _PA_CutY.z of any of the walls it is outside of (up to
// four planes, Unity space: xyz the normal, w the distance) is left out, where (y - y0) * sign > 0 (the floor's edge
// kept); from above, the roof too, inside its footprint (_PA_Roof: its height, half-length (or apothem), half-width,
// shape: 1 a box, 2 a square turned 45 degrees, 3 an octagon; 0 no roof). Only on what is drawn with a material that
// says so (_Cuttable: 1 the room, -1 the floor's mirror image of it: y0 and the roof's height mirrored)
#ifndef PA_CUTAWAY_INCLUDED
#define PA_CUTAWAY_INCLUDED
float _PA_CutN;
float4 _PA_Cut[4];
float4 _PA_CutY;   // (y0, sign, half-width)
float4 _PA_Roof;

void PA_CutAway(float3 wp, float cuttable)
{
    if (abs(cuttable) < 0.5 || (_PA_CutN < 0.5 && _PA_Roof.w < 0.5)) return;
    float sg = cuttable > 0.0 ? 1.0 : -1.0;
    float s = _PA_CutY.y * sg;
    if ((wp.y - _PA_CutY.x * sg) * s <= 0.0) return;
    [unroll] for (int k = 0; k < 4; ++k)
        if (k < _PA_CutN && abs(dot(_PA_Cut[k].xyz, wp) - _PA_Cut[k].w) < _PA_CutY.z) discard;
    if (_PA_Roof.w > 0.5 && abs(wp.y - _PA_Roof.x * sg) < _PA_CutY.z)
    {
        float2 q = abs(wp.xz);
        bool inside = _PA_Roof.w < 1.5 ? (q.x < _PA_Roof.y && q.y < _PA_Roof.z)
                    : _PA_Roof.w < 2.5 ? (q.x + q.y < _PA_Roof.y)
                    : (max(max(q.x, q.y), (q.x + q.y) * 0.70710678) < _PA_Roof.y);
        if (inside) discard;
    }
}
#endif
