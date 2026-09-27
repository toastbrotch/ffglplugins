/*{
  "DESCRIPTION": "ISF port of Shadertoy 'Md23RK' — an endless fractal terrain flythrough. The terrain isn't a classic SDF: each step recursively subdivides the current grid cell (5 levels, diamond-square-style midpoint displacement) down to the sub-cell containing the ray point, then returns the analytic distance to that sub-cell's local tangent plane — cheap and exact enough for raymarching without ever storing a heightmap. Added: flight speed/zoom/fog controls, a terrain reseed, an overall contrast punch, and edge emphasis (slope + grazing-angle rim, both cheap single-ray stand-ins for a real depth/normal edge-detect) so ridgelines read as distinct 'edges' against the flatter 'areas' between them.",
  "CREDIT": "Shadertoy 'Md23RK' (base) + Claude Sonnet 5 (ISF port, parameters, edge contrast)",
  "CATEGORIES": ["generator", "3d", "landscape"],
  "INPUTS": [
    { "NAME": "uSpeed",         "LABEL": "Flug-Tempo",       "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "uZoom",          "LABEL": "Zoom",             "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.5, "MAX": 2.5 },
    { "NAME": "uContrast",      "LABEL": "Kontrast",         "TYPE": "float", "DEFAULT": 1.15, "MIN": 0.5, "MAX": 3.0 },
    { "NAME": "uEdgeAmount",    "LABEL": "Kanten-Betonung",  "TYPE": "float", "DEFAULT": 0.60, "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uEdgeSharpness", "LABEL": "Kanten-Schaerfe",  "TYPE": "float", "DEFAULT": 3.00, "MIN": 0.5, "MAX": 8.0 },
    { "NAME": "uFog",           "LABEL": "Nebel-Dichte",     "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.2, "MAX": 3.0 },
    { "NAME": "uSeed",          "LABEL": "Terrain-Seed",     "TYPE": "float", "DEFAULT": 0.00, "MIN": 0.0, "MAX": 100.0 },
    { "NAME": "uHue",           "LABEL": "Farbton-Drift",    "TYPE": "float", "DEFAULT": 0.00, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

// FRACTAL TERRAIN — ISF port of Shadertoy Md23RK. Self-contained generator, no image input.
// GLSL body only (gl_FragColor / TIME / RENDERSIZE come from the host preamble), so this loads
// through both the FreeFrame ISFBrowser (legacy GLSL ES 1.00 style preamble) and the FFGL
// ISFBrowser/ISFEffect (core-profile preamble) unchanged.

float gTime;
vec3 gPln;

float terrain(vec3 p)
{
    float nx = floor(p.x)*10.0 + floor(p.z)*100.0 + uSeed*997.0, center = 0.0, scale = 2.0;
    vec4 heights = vec4(0.0, 0.0, 0.0, 0.0);

    for (int i = 0; i < 5; i += 1)
    {
        vec2 spxz = step(vec2(0.0), p.xz);
        float corner_height = mix(mix(heights.x, heights.y, spxz.x),
                                   mix(heights.w, heights.z, spxz.x), spxz.y);

        vec4 mid_heights = (heights + heights.yzwx)*0.5;

        heights = mix(mix(vec4(heights.x, mid_heights.x, center, mid_heights.w),
                           vec4(mid_heights.x, heights.y, mid_heights.y, center), spxz.x),
                      mix(vec4(mid_heights.w, center, mid_heights.z, heights.w),
                          vec4(center, mid_heights.y, heights.z, mid_heights.z), spxz.x), spxz.y);

        nx = nx*4.0 + spxz.x + 2.0*spxz.y;

        center = (center + corner_height)*0.5 + cos(nx*20.0)/scale*30.0;
        p.xz = fract(p.xz) - vec2(0.5);
        p *= 2.0;
        scale *= 2.0;
    }

    float d0 = p.x + p.z;

    vec2 plh = mix(mix(heights.xw, heights.zw, step(0.0, d0)),
                    mix(heights.xy, heights.zy, step(0.0, d0)), step(p.z, p.x));

    gPln = normalize(vec3(plh.x - plh.y, 2.0, (plh.x - center) + (plh.y - center)));

    if (p.x + p.z > 0.0)
        gPln.xz = -gPln.zx;

    if (p.x < p.z)
        gPln.xz = gPln.zx;

    p.y -= center;
    return dot(p, gPln)/scale;
}

vec3 hueRotate(vec3 v, float a){
    float c = cos(a), s = sin(a);
    float r = v.r*(0.299 + 0.701*c + 0.168*s) + v.g*(0.587 - 0.587*c + 0.330*s) + v.b*(0.114 - 0.114*c - 0.497*s);
    float g = v.r*(0.299 - 0.299*c - 0.328*s) + v.g*(0.587 + 0.413*c + 0.035*s) + v.b*(0.114 - 0.114*c + 0.292*s);
    float b = v.r*(0.299 - 0.300*c + 1.250*s) + v.g*(0.587 - 0.588*c - 1.050*s) + v.b*(0.114 + 0.886*c - 0.203*s);
    return clamp(vec3(r, g, b), 0.0, 1.0);
}

void main()
{
    gTime = TIME*uSpeed*0.4;
    vec2 uv = (gl_FragCoord.xy/RENDERSIZE.xy)*2.0 - vec2(1.0);
    uv.x *= RENDERSIZE.x/RENDERSIZE.y;

    float sc = (gTime + sin(gTime*0.2)*4.0)*0.8;
    vec3 camo = vec3(sc + cos(gTime*0.2)*0.5, 0.7 + sin(gTime*0.3)*0.4, 0.3 + sin(gTime*0.4)*0.8);
    vec3 camt = vec3(sc + cos(gTime*0.04)*1.5, -1.5, 0.0);
    vec3 camd = normalize(camt - camo);

    vec3 camu = normalize(cross(camd, vec3(0.5, 1.0, 0.0))), camv = normalize(cross(camu, camd));
    camu = normalize(cross(camd, camv));

    mat3 m = mat3(camu, camv, camd);

    vec3 rd = m*normalize(vec3(uv, 1.8*uZoom)), rp;

    float t = 0.0;
    for (int i = 0; i < 100; i += 1)
    {
        rp = camo + rd*t;
        float d = terrain(rp);
        if (d < 4e-3) break;
        t += d;
    }

    vec3 ld = normalize(vec3(1.0, 0.6, 2.0));
    vec3 col = mix(vec3(0.1, 0.1, 0.5)*0.4, vec3(1.0, 1.0, 0.8), pow(0.5 + 0.5*dot(gPln, ld), 0.7));

    // --- edge / area contrast ---
    // Slope (how far the surface tilts from straight up) is a cheap single-ray stand-in for a
    // real depth/normal edge-detect (which would need extra neighbour raymarches this shader
    // doesn't otherwise do): ridgelines and cliff faces read as "edges" against the flatter
    // plateau "areas" between them, so darkening by slope emphasises exactly that boundary.
    float slope = 1.0 - clamp(gPln.y, 0.0, 1.0);
    float edge  = pow(slope, max(uEdgeSharpness, 0.001));
    col *= mix(1.0, 1.0 - 0.85*uEdgeAmount, edge);

    // Grazing-angle (fresnel-ish) rim: silhouette-facing slopes pick up extra contrast too.
    float rim = pow(1.0 - abs(dot(gPln, -rd)), max(uEdgeSharpness, 0.001));
    col += rim*uEdgeAmount*0.25*vec3(1.0, 0.97, 0.9);

    // Overall contrast punch (pushes everything away from flat grey toward the lit/shadow colours).
    col = mix(vec3(dot(col, vec3(0.299, 0.587, 0.114))), col, uContrast);

    // Distance fog — also doubles as the sky colour for rays that never hit anything.
    col = mix(vec3(0.5, 0.6, 1.0), col, exp(-t*0.02*uFog));

    if (uHue > 0.0001) col = hueRotate(col, uHue*6.2831853);

    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
