/*{
  "DESCRIPTION": "Voxel Flight, v2 — same Tron-style raymarched voxel-lattice flythrough as voxel-flight.fs, cranked up: smaller cells for a much denser block field, a faster/tighter flight path with camera shake, two overlapping traveling energy pulses instead of one, periodic full-field brightness surges, and radial hyperspace speed-lines that kick in with velocity. The uChaos knob is the whole point — it goes to 11 (a Spinal Tap nod), and turning it up scales the shake, pulses, flashes and speed-lines all at once on top of the already-hot base settings.",
  "CREDIT": "CC0",
  "CATEGORIES": ["generator", "raymarch", "scifi", "retro"],
  "INPUTS": [
    { "NAME": "uSpeed",      "LABEL": "Flug-Geschwindigkeit", "TYPE": "float", "DEFAULT": 2.20,  "MIN": 0.0, "MAX": 5.0 },
    { "NAME": "uDensity",    "LABEL": "Voxel-Dichte",         "TYPE": "float", "DEFAULT": 0.85,  "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uGlow",       "LABEL": "Neon-Glow",            "TYPE": "float", "DEFAULT": 1.50,  "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "uPulse",      "LABEL": "Energie-Puls",         "TYPE": "float", "DEFAULT": 1.10,  "MIN": 0.0, "MAX": 2.5 },
    { "NAME": "uColorShift", "LABEL": "Farb-Zyklus-Tempo",    "TYPE": "float", "DEFAULT": 2.00,  "MIN": 0.0, "MAX": 5.0 },
    { "NAME": "uCurviness",  "LABEL": "Flugbahn-Kurven",      "TYPE": "float", "DEFAULT": 1.00,  "MIN": 0.0, "MAX": 1.8 },
    { "NAME": "uZoom",       "LABEL": "Zoom",                 "TYPE": "float", "DEFAULT": 1.15,  "MIN": 0.7, "MAX": 2.2 },
    { "NAME": "uFog",        "LABEL": "Nebel/Sichtweite",     "TYPE": "float", "DEFAULT": 0.80,  "MIN": 0.3, "MAX": 2.5 },
    { "NAME": "uSeed",       "LABEL": "Zufalls-Seed",         "TYPE": "float", "DEFAULT": 5.00,  "MIN": 0.0, "MAX": 100.0 },
    { "NAME": "uChaos",      "LABEL": "Chaos (geht bis 11)",  "TYPE": "float", "DEFAULT": 11.00, "MIN": 0.0, "MAX": 11.0 }
  ]
}*/

// VOXEL FLIGHT v2 — denser/faster Tron-style raymarched voxel-field
// flythrough, ISF generator (GLSL ES 1.00: literal loop bounds,
// gl_FragColor, TIME/RENDERSIZE). Self-contained, no image input required.

#define PI 3.14159265

float gT, gSeed, gDensity, gGlow, gPulse, gColorShift, gCurve, gFog, gAspect, gChaos, gSpeedIn;

// ---------------------------------------------------------------- hashing --
float hash11(float n){ return fract(sin(n)*43758.5453); }
float hash13(vec3 p3){
    p3 = fract(p3*0.1031);
    p3 += dot(p3, p3.yzx + 19.19);
    return fract((p3.x + p3.y)*p3.z);
}

// ------------------------------------------------------------------ path --
vec2 pathXY(float z){
    float c = 0.5 + gCurve*1.4;
    return vec2(sin(z*0.09)*2.2*c + sin(z*0.024)*1.4*c,
                cos(z*0.075)*1.9*c + sin(z*0.041)*1.2*c);
}

// -------------------------------------------------------------- geometry --
float sdBox(vec3 p, vec3 b){
    vec3 d = abs(p) - b;
    return length(max(d, vec3(0.0))) + min(max(d.x, max(d.y, d.z)), 0.0);
}

// Smaller cells than v1 (2.2 instead of 3.2) => a noticeably denser lattice.
float worldMap(vec3 p){
    vec3 q = p; q.xy -= pathXY(p.z);
    float cs = 2.2;
    vec3 id = floor(q/cs);
    vec3 pc = q - (id + 0.5)*cs;

    float dFloor = p.y - (-6.0);

    float hpres = hash13(id + gSeed*13.0);
    float cellR = length((id.xy + 0.5)*cs);
    float clearZone = mix(3.6, 1.0, gDensity);
    float farChance = mix(0.6, 0.98, gDensity);
    float presenceChance = mix(0.10, farChance, smoothstep(clearZone, clearZone + 3.0, cellR));
    float exists = step(1.0 - presenceChance, hpres);

    if (exists < 0.5) return dFloor;

    float boxHf = mix(0.4, 2.4, hash13(id + 7.7));
    float morphPhase = hash13(id + 3.3)*6.2831;
    float breathe = 0.78 + (0.20 + 0.10*gChaos)*sin(gT*1.1 + morphPhase);
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
    for (int i = 0; i < 110; i++){
        vec3 pos = ro + rd*t;
        float d = worldMap(pos);
        if (d < 0.0015*max(t, 1.0)) return t;
        t += d*0.80;
        if (t > 100.0) break;
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

vec3 paletteAt(float z){
    vec3 cyan    = vec3(0.15, 0.85, 1.0);
    vec3 orange  = vec3(1.0, 0.45, 0.08);
    vec3 magenta = vec3(0.85, 0.15, 0.95);

    float phase = fract(gT*0.08*gColorShift)*3.0;
    vec3 pal;
    if      (phase < 1.0) pal = mix(cyan, orange, smoothstep(0.0, 1.0, phase));
    else if (phase < 2.0) pal = mix(orange, magenta, smoothstep(0.0, 1.0, phase - 1.0));
    else                  pal = mix(magenta, cyan, smoothstep(0.0, 1.0, phase - 2.0));

    float zone = floor(z/38.0);
    float zoneH = hash11(zone*7.7 + gSeed*3.1);
    return hueRotate(pal, (zoneH - 0.5)*1.6);
}

// Two overlapping traveling waves (slow/heavy + fast/sharp) instead of one.
float pulseWave(float z){
    float w1 = sin(z*0.35 - gT*4.0);
    float w2 = sin(z*0.9  - gT*8.5);
    float p1 = smoothstep(0.985, 1.0, w1)*gPulse;
    float p2 = smoothstep(0.975, 1.0, w2)*gPulse*0.6*gChaos;
    return p1 + p2;
}

vec3 bg(vec3 rd, vec3 pal){
    float horizon = exp(-abs(rd.y)*3.0);
    vec3 col = pal*horizon*0.14;
    vec3 sd = floor(rd*350.0);
    float star = step(0.997, hash13(sd));
    col += vec3(star)*0.55;
    return col;
}

// Cheap radial hyperspace streaks that flicker in/out by angle bucket,
// intensity scaled by both chaos and how fast we're actually flying.
float speedLines(vec2 uv){
    float ang = atan(uv.y, uv.x);
    float n = hash11(floor(ang*70.0) + floor(gT*45.0));
    float streak = step(0.78, n)*smoothstep(0.0, 1.1, length(uv));
    return streak;
}

vec3 shadeHit(vec3 p, vec3 n, vec3 rd){
    vec3 pal = paletteAt(p.z);
    float pulse = pulseWave(p.z);

    if (p.y < -5.85){
        vec2 g = abs(fract(p.xz/2.0) - 0.5);
        float lineMask = 1.0 - smoothstep(0.0, 0.03, min(g.x, g.y));
        vec3 col = vec3(0.006) + pal*lineMask*0.95*gGlow;
        col += pal*pulse*0.7;
        return col;
    }

    vec3 q = p; q.xy -= pathXY(p.z);
    float cs = 2.2;
    vec3 id = floor(q/cs);
    vec3 pc = q - (id + 0.5)*cs;

    float boxHf = mix(0.4, 2.4, hash13(id + 7.7));
    float morphPhase = hash13(id + 3.3)*6.2831;
    float breathe = 0.78 + (0.20 + 0.10*gChaos)*sin(gT*1.1 + morphPhase);
    vec3 bs = vec3(cs*0.40)*breathe;
    bs.y *= boxHf;

    vec3 ap = abs(pc);
    vec3 d3 = bs - ap;
    float mn = min(d3.x, min(d3.y, d3.z));
    float mx = max(d3.x, max(d3.y, d3.z));
    float mid = d3.x + d3.y + d3.z - mn - mx;
    float edgeGlow = 1.0 - smoothstep(0.0, 0.07, mid);

    float fres = pow(1.0 - max(dot(n, -rd), 0.0), 3.0);
    float glowBoost = gGlow*(1.0 + 0.5*gChaos);
    vec3 col = vec3(0.008, 0.01, 0.014) + pal*fres*0.12;
    col += pal*edgeGlow*glowBoost;
    col += pal*pulse*edgeGlow*1.8;
    col += pal*pulse*0.06;
    return col;
}

// ------------------------------------------------------------------ main --
void main(){
    vec2 res = RENDERSIZE.xy;
    gAspect = res.x/res.y;
    gT = TIME*uSpeed;
    gSeed = uSeed; gDensity = uDensity; gGlow = uGlow; gPulse = uPulse;
    gColorShift = uColorShift; gCurve = uCurviness; gFog = uFog;
    gChaos = uChaos/11.0; gSpeedIn = uSpeed;

    vec2 uv = (gl_FragCoord.xy - 0.5*res)/res.y;

    float camZ = gT*9.0;
    vec3 ro = vec3(pathXY(camZ), camZ);
    vec3 target = vec3(pathXY(camZ + 3.0), camZ + 3.0);
    vec3 fwd = normalize(target - ro);
    vec3 worldUp = vec3(0.0, 1.0, 0.0);
    vec3 right = normalize(cross(fwd, worldUp));
    vec3 up = cross(right, fwd);

    float bank = (pathXY(camZ + 3.0).x - pathXY(camZ - 3.0).x)*0.22 + sin(gT*0.35)*0.08;
    vec3 rightB = right*cos(bank) + up*sin(bank);
    vec3 upB = up*cos(bank) - right*sin(bank);

    vec2 shakeT = vec2(floor(gT*30.0));
    vec2 shake = (vec2(hash11(shakeT.x*3.1 + 1.0), hash11(shakeT.x*7.7 + 2.0)) - 0.5)*0.024*gChaos;

    vec3 rd = normalize(fwd*uZoom + rightB*(uv.x + shake.x) + upB*(uv.y + shake.y));

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

    vec3 pal = paletteAt(ro.z);
    float sl = speedLines(uv)*gChaos*clamp(gSpeedIn/3.0, 0.0, 1.0);
    col += pal*sl*0.6;

    float flash = pow(max(sin(gT*0.5), 0.0), 20.0)*gChaos;
    col *= 1.0 + flash*0.8;
    col += pal*flash*0.3;

    col *= 1.0 - 0.30*dot(uv, uv);
    gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
