/*{
  "DESCRIPTION": "Raymarched flythrough of an abstract voxel world, Tron Legacy style: a camera weaves along a slowly bending path through an infinite 3D lattice of black glass blocks whose edges glow neon (cyan/orange/magenta, cycling slowly), floating above an endless glowing grid floor. Block heights slowly 'breathe', a traveling energy pulse periodically sweeps down the world lighting up every edge and floor line it passes, and the neon palette hue-shifts zone by zone as you fly deeper — so the contour geometry is complex and never settles into a repeat.",
  "CREDIT": "CC0",
  "CATEGORIES": ["generator", "raymarch", "scifi", "retro"],
  "INPUTS": [
    { "NAME": "uSpeed",      "LABEL": "Flug-Geschwindigkeit", "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "uDensity",    "LABEL": "Voxel-Dichte",         "TYPE": "float", "DEFAULT": 0.55, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uGlow",       "LABEL": "Neon-Glow",            "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uPulse",      "LABEL": "Energie-Puls",         "TYPE": "float", "DEFAULT": 0.60, "MIN": 0.0, "MAX": 1.5 },
    { "NAME": "uColorShift", "LABEL": "Farb-Zyklus-Tempo",    "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "uCurviness",  "LABEL": "Flugbahn-Kurven",      "TYPE": "float", "DEFAULT": 0.60, "MIN": 0.0, "MAX": 1.5 },
    { "NAME": "uZoom",       "LABEL": "Zoom",                 "TYPE": "float", "DEFAULT": 1.30, "MIN": 0.7, "MAX": 2.2 },
    { "NAME": "uFog",        "LABEL": "Nebel/Sichtweite",     "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.3, "MAX": 2.5 },
    { "NAME": "uSeed",       "LABEL": "Zufalls-Seed",         "TYPE": "float", "DEFAULT": 5.00, "MIN": 0.0, "MAX": 100.0 }
  ]
}*/

// VOXEL FLIGHT — Tron-style raymarched voxel-field flythrough, ISF generator
// (GLSL ES 1.00: literal loop bounds, gl_FragColor, TIME/RENDERSIZE).
// Self-contained, no image input required.

#define PI 3.14159265

float gT, gSeed, gDensity, gGlow, gPulse, gColorShift, gCurve, gFog, gAspect;

// ---------------------------------------------------------------- hashing --
float hash11(float n){ return fract(sin(n)*43758.5453); }
float hash13(vec3 p3){
    p3 = fract(p3*0.1031);
    p3 += dot(p3, p3.yzx + 19.19);
    return fract((p3.x + p3.y)*p3.z);
}

// ------------------------------------------------------------------ path --
vec2 pathXY(float z){
    float c = 0.4 + gCurve*1.2;
    return vec2(sin(z*0.07)*2.2*c + sin(z*0.019)*1.3*c,
                cos(z*0.06)*1.9*c + sin(z*0.033)*1.1*c);
}

// -------------------------------------------------------------- geometry --
float sdBox(vec3 p, vec3 b){
    vec3 d = abs(p) - b;
    return length(max(d, vec3(0.0))) + min(max(d.x, max(d.y, d.z)), 0.0);
}

// World SDF: an infinite domain-repeated lattice of boxes (sparser near the
// flight path, denser further out) unioned with an endless floor plane —
// wherever a cell is empty, or below the floor, the floor wins the union.
float worldMap(vec3 p){
    vec3 q = p; q.xy -= pathXY(p.z);
    float cs = 3.2;
    vec3 id = floor(q/cs);
    vec3 pc = q - (id + 0.5)*cs;

    float dFloor = p.y - (-6.0);

    float hpres = hash13(id + gSeed*13.0);
    float cellR = length((id.xy + 0.5)*cs);
    float clearZone = mix(5.0, 1.5, gDensity);
    float farChance = mix(0.5, 0.97, gDensity);
    float presenceChance = mix(0.05, farChance, smoothstep(clearZone, clearZone + 4.0, cellR));
    float exists = step(1.0 - presenceChance, hpres);

    if (exists < 0.5) return dFloor;

    float boxHf = mix(0.55, 1.6, hash13(id + 7.7));
    float morphPhase = hash13(id + 3.3)*6.2831;
    float breathe = 0.82 + 0.18*sin(gT*0.55 + morphPhase);
    vec3 bs = vec3(cs*0.40)*breathe;
    bs.y *= boxHf;

    float dBox = sdBox(pc, bs);
    return min(dBox, dFloor);
}

vec3 getNormal(vec3 p){
    float eps = 0.002;
    vec2 e = vec2(1.0, -1.0)*0.5773;
    return normalize(e.xyy*worldMap(p + e.xyy*eps) +
                      e.yyx*worldMap(p + e.yyx*eps) +
                      e.yxy*worldMap(p + e.yxy*eps) +
                      e.xxx*worldMap(p + e.xxx*eps));
}

float raymarch(vec3 ro, vec3 rd){
    float t = 0.0;
    for (int i = 0; i < 96; i++){
        vec3 pos = ro + rd*t;
        float d = worldMap(pos);
        if (d < 0.0015*max(t, 1.0)) return t;
        t += d*0.85;
        if (t > 90.0) break;
    }
    return -1.0;
}

// ------------------------------------------------------------------ hue --
vec3 hueRotate(vec3 v, float a){
    float c = cos(a), s = sin(a);
    float r = v.r*(0.299 + 0.701*c + 0.168*s) + v.g*(0.587 - 0.587*c + 0.330*s) + v.b*(0.114 - 0.114*c - 0.497*s);
    float g = v.r*(0.299 - 0.299*c - 0.328*s) + v.g*(0.587 + 0.413*c + 0.035*s) + v.b*(0.114 - 0.114*c + 0.292*s);
    float b = v.r*(0.299 - 0.300*c + 1.250*s) + v.g*(0.587 - 0.588*c - 1.050*s) + v.b*(0.114 + 0.886*c - 0.203*s);
    return clamp(vec3(r, g, b), 0.0, 1.0);
}

// Global neon mood slowly cycles cyan -> orange -> magenta -> cyan, then
// each ~50-unit stretch of the world gets its own extra hue tint so no two
// zones you fly through read quite the same.
vec3 paletteAt(float z){
    vec3 cyan    = vec3(0.15, 0.85, 1.0);
    vec3 orange  = vec3(1.0, 0.45, 0.08);
    vec3 magenta = vec3(0.85, 0.15, 0.95);

    float phase = fract(gT*0.05*gColorShift)*3.0;
    vec3 pal;
    if      (phase < 1.0) pal = mix(cyan, orange, smoothstep(0.0, 1.0, phase));
    else if (phase < 2.0) pal = mix(orange, magenta, smoothstep(0.0, 1.0, phase - 1.0));
    else                  pal = mix(magenta, cyan, smoothstep(0.0, 1.0, phase - 2.0));

    float zone = floor(z/50.0);
    float zoneH = hash11(zone*7.7 + gSeed*3.1);
    return hueRotate(pal, (zoneH - 0.5)*1.6);
}

float pulseWave(float z){
    float w = sin(z*0.35 - gT*3.0);
    return smoothstep(0.985, 1.0, w)*gPulse;
}

vec3 bg(vec3 rd, vec3 pal){
    float horizon = exp(-abs(rd.y)*3.0);
    vec3 col = pal*horizon*0.12;
    vec3 sd = floor(rd*350.0);
    float star = step(0.9975, hash13(sd));
    col += vec3(star)*0.5;
    return col;
}

// Recomputes the same cell data as worldMap at the hit point so it can tell
// edges (bright neon) from face interiors (near-black) and floor from box.
vec3 shadeHit(vec3 p, vec3 n, vec3 rd){
    vec3 pal = paletteAt(p.z);
    float pulse = pulseWave(p.z);

    if (p.y < -5.85){
        vec2 g = abs(fract(p.xz/2.0) - 0.5);
        float lineMask = 1.0 - smoothstep(0.0, 0.03, min(g.x, g.y));
        vec3 col = vec3(0.005) + pal*lineMask*0.9*gGlow;
        col += pal*pulse*0.6;
        return col;
    }

    vec3 q = p; q.xy -= pathXY(p.z);
    float cs = 3.2;
    vec3 id = floor(q/cs);
    vec3 pc = q - (id + 0.5)*cs;

    float boxHf = mix(0.55, 1.6, hash13(id + 7.7));
    float morphPhase = hash13(id + 3.3)*6.2831;
    float breathe = 0.82 + 0.18*sin(gT*0.55 + morphPhase);
    vec3 bs = vec3(cs*0.40)*breathe;
    bs.y *= boxHf;

    vec3 ap = abs(pc);
    vec3 d3 = bs - ap;
    float mn = min(d3.x, min(d3.y, d3.z));
    float mx = max(d3.x, max(d3.y, d3.z));
    float mid = d3.x + d3.y + d3.z - mn - mx;
    float edgeGlow = 1.0 - smoothstep(0.0, 0.06, mid);

    float fres = pow(1.0 - max(dot(n, -rd), 0.0), 3.0);
    vec3 col = vec3(0.008, 0.01, 0.014) + pal*fres*0.10;
    col += pal*edgeGlow*gGlow;
    col += pal*pulse*edgeGlow*1.5;
    col += pal*pulse*0.05;
    return col;
}

// ------------------------------------------------------------------ main --
void main(){
    vec2 res = RENDERSIZE.xy;
    gAspect = res.x/res.y;
    gT = TIME*uSpeed;
    gSeed = uSeed; gDensity = uDensity; gGlow = uGlow; gPulse = uPulse;
    gColorShift = uColorShift; gCurve = uCurviness; gFog = uFog;

    vec2 uv = (gl_FragCoord.xy - 0.5*res)/res.y;

    float camZ = gT*4.0;
    vec3 ro = vec3(pathXY(camZ), camZ);
    vec3 target = vec3(pathXY(camZ + 3.0), camZ + 3.0);
    vec3 fwd = normalize(target - ro);
    vec3 worldUp = vec3(0.0, 1.0, 0.0);
    vec3 right = normalize(cross(fwd, worldUp));
    vec3 up = cross(right, fwd);

    float bank = (pathXY(camZ + 3.0).x - pathXY(camZ - 3.0).x)*0.15 + sin(gT*0.2)*0.05;
    vec3 rightB = right*cos(bank) + up*sin(bank);
    vec3 upB = up*cos(bank) - right*sin(bank);

    vec3 rd = normalize(fwd*uZoom + rightB*uv.x + upB*uv.y);

    float t = raymarch(ro, rd);
    vec3 col;
    if (t > 0.0){
        vec3 p = ro + rd*t;
        vec3 n = getNormal(p);
        col = shadeHit(p, n, rd);
        float fog = 1.0 - exp(-t*t*0.0009*gFog);
        col = mix(col, vec3(0.0), fog);
    } else {
        col = bg(rd, paletteAt(ro.z));
    }

    col *= 1.0 - 0.30*dot(uv, uv);
    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
