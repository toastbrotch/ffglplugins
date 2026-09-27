/*{
  "DESCRIPTION": "4x4 CRT TV-wall generator, v2 — more restless sibling of crt-tvwall.fs. Per-tube content (oscilloscope/Lissajous, drifting SMPTE/resolution test-card, dead-channel static) switches faster and more often, wall-wide sync takeovers happen more frequently, and a slow continuous hue drift keeps the whole image never quite settling on one colour. The big change is the whole-screen overlay layer, which now rotates through six distinct looks instead of three: sync-roll/power-flicker, a travelling scan sweep, shared grain, a procedural ASCII/terminal conversion of the live mosaic, Matrix-style falling digital rain, and a Soyuz/spacecraft docking-camera HUD (alignment rings, wandering crosshair, corner brackets, blinking telemetry boxes, radar sweep) — each one crossing all 16 tube boundaries continuously before fading into the next.",
  "CREDIT": "CC0",
  "CATEGORIES": ["generator", "retro", "glitch", "crt"],
  "INPUTS": [
    { "NAME": "uSpeed",      "LABEL": "Geschwindigkeit",       "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "uGlitch",     "LABEL": "VHS-Glitch-Menge",      "TYPE": "float", "DEFAULT": 0.50, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uColorNoise", "LABEL": "Farbrauschen",          "TYPE": "float", "DEFAULT": 0.35, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uTakeover",   "LABEL": "Wall-Sync-Haeufigkeit", "TYPE": "float", "DEFAULT": 0.50, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uCurvature",  "LABEL": "Roehren-Woelbung",      "TYPE": "float", "DEFAULT": 0.45, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uBezel",      "LABEL": "Rahmen/Fugen",          "TYPE": "float", "DEFAULT": 0.60, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uScopeGlow",  "LABEL": "Oszilloskop-Glow",      "TYPE": "float", "DEFAULT": 0.75, "MIN": 0.0, "MAX": 1.5 },
    { "NAME": "uOverlayMix", "LABEL": "Overlay-Intensitaet",   "TYPE": "float", "DEFAULT": 0.85, "MIN": 0.0, "MAX": 1.2 },
    { "NAME": "uHueDrift",   "LABEL": "Farbton-Drift",         "TYPE": "float", "DEFAULT": 0.35, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uSeed",       "LABEL": "Zufalls-Seed",          "TYPE": "float", "DEFAULT": 7.00, "MIN": 0.0, "MAX": 100.0 }
  ]
}*/

// CRT TV-WALL v2 — 4x4 grid ISF generator (GLSL ES 1.00: literal loop bounds,
// gl_FragColor, TIME/RENDERSIZE). Self-contained, no image input required.
// Faster per-tube switching + a 6-way rotating full-wall overlay
// (sync-roll, sweep, grain, ASCII, Matrix rain, docking-camera HUD).

#define PI 3.14159265
#define GRID 4.0

float gT, gGlitch, gColorNoise, gCurvature, gBezel, gGlow, gTakeover, gSeed, gAspect;

// ---------------------------------------------------------------- hashing --
float hash11(float n){ return fract(sin(n)*43758.5453); }
float hash21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7)))*43758.5453); }
float sat(float x){ return clamp(x, 0.0, 1.0); }

// ------------------------------------------------------------ tube shape --
vec2 tubeWarp(vec2 p){
    float k = gCurvature*0.30;
    float r2 = dot(p, p);
    return p*(1.0 + k*r2);
}
float tubeMask(vec2 p){
    vec2 b = vec2(gAspect*0.92, 0.90);
    float r = 0.12;
    vec2 d = abs(p) - b + r;
    float dist = length(max(d, vec2(0.0))) - r;
    return 1.0 - smoothstep(-0.015, 0.015, dist);
}
float tubeGlitchiness(float seed){ return 0.15 + 0.5*hash11(seed*9.0 + 8.0); }

// ------------------------------------------------------- oscilloscope --
vec3 scopeCell(vec2 p, float seed){
    float fa = 2.0 + floor(hash11(seed*3.1 + 1.0)*4.0);
    float fb = 2.0 + floor(hash11(seed*4.7 + 2.0)*4.0);
    float ph = hash11(seed*5.3 + 3.0)*6.2831;
    float spd = 0.5 + hash11(seed*6.1 + 4.0)*0.6;

    vec3 phosGreen = vec3(0.25, 1.0, 0.35);
    vec3 phosAmber = vec3(1.0, 0.55, 0.08);
    vec3 phos = hash11(seed*7.7 + 5.0) > 0.5 ? phosGreen : phosAmber;

    vec2 g = abs(fract(p*2.5) - 0.5);
    float grid = smoothstep(0.02, 0.0, min(g.x, g.y));
    float axis = smoothstep(0.015, 0.0, min(abs(p.x), abs(p.y)));
    vec3 col = phos*0.05*grid + phos*0.10*axis;

    float minD = 10.0;
    float minD2 = 10.0;
    for (int i = 0; i < 40; i++){
        float u = float(i)/40.0*2.0*PI;
        vec2 q  = vec2(sin(fa*u + ph + gT*spd),        sin(fb*u + gT*spd*0.7));
        vec2 q2 = vec2(sin(fa*u + ph + (gT-0.10)*spd), sin(fb*u + (gT-0.10)*spd*0.7));
        minD  = min(minD,  length(p - q));
        minD2 = min(minD2, length(p - q2));
    }
    float trace = 0.010/(minD*minD*26.0 + 0.010);
    float trail = 0.010/(minD2*minD2*30.0 + 0.010)*0.35;
    col += phos*(trace + trail)*gGlow;
    return col;
}

// -------------------------------------------------------------- test-card --
vec3 smpteBars(float x, float y){
    vec3 col;
    if (y > 0.33){
        float idx = floor(x*7.0);
        if      (idx < 1.0) col = vec3(0.75);
        else if (idx < 2.0) col = vec3(0.75, 0.75, 0.0);
        else if (idx < 3.0) col = vec3(0.0, 0.75, 0.75);
        else if (idx < 4.0) col = vec3(0.0, 0.75, 0.0);
        else if (idx < 5.0) col = vec3(0.75, 0.0, 0.75);
        else if (idx < 6.0) col = vec3(0.75, 0.0, 0.0);
        else                col = vec3(0.0, 0.0, 0.75);
    } else if (y > 0.25){
        float idx = floor(x*7.0);
        if      (idx < 1.0) col = vec3(0.0, 0.0, 0.75);
        else if (idx < 2.0) col = vec3(0.05);
        else if (idx < 3.0) col = vec3(0.75, 0.0, 0.75);
        else if (idx < 4.0) col = vec3(0.05);
        else if (idx < 5.0) col = vec3(0.0, 0.75, 0.75);
        else if (idx < 6.0) col = vec3(0.05);
        else                col = vec3(0.75);
    } else {
        if      (x < 0.15) col = vec3(0.0, 0.10, 0.30);
        else if (x < 0.30) col = vec3(0.05);
        else if (x < 0.45) col = vec3(0.75);
        else if (x < 0.60) col = vec3(0.15, 0.0, 0.30);
        else {
            float px = (x - 0.60)/0.40;
            float pidx = floor(px*4.0);
            if      (pidx < 1.0) col = vec3(0.02);
            else if (pidx < 2.0) col = vec3(0.05);
            else if (pidx < 3.0) col = vec3(0.08);
            else                 col = vec3(0.75);
        }
    }
    return col;
}
vec3 resChart(vec2 p, vec2 uv01){
    vec3 col;
    if (uv01.y > 0.5){
        float steps = 10.0;
        float idx = floor(uv01.x*steps);
        col = vec3(idx/(steps - 1.0));
    } else {
        float r = length(p);
        float ring = step(0.5, fract(r*8.0));
        col = vec3(0.05) + vec3(ring)*0.65;
        float cross = 1.0 - smoothstep(0.0, 0.01, min(abs(p.x), abs(p.y)));
        col = mix(col, vec3(1.0, 0.82, 0.1), cross);
    }
    return col;
}
vec3 testcardCell(vec2 uv01, vec2 p, float seed){
    float variant = hash11(seed*21.0 + 6.0);
    float drift = sin(gT*0.11 + seed*10.0)*0.02 + gT*0.004*sin(seed*3.0);
    float jumpH = hash21(vec2(seed, floor(gT/2.2)));
    float jump = step(0.85, jumpH)*(hash11(seed*8.0 + floor(gT/2.2)) - 0.5)*0.5;
    float dx = drift + jump;

    if (variant < 0.6) return smpteBars(fract(uv01.x + dx + 1.0), uv01.y);
    return resChart(p, uv01);
}

// ------------------------------------------------------------- VHS glitch --
vec3 vhsGlitch(vec3 color, vec2 uv01, float seed, float burst){
    float rowH = hash21(vec2(floor(uv01.y*30.0), floor(gT*2.0 + seed*10.0)));
    float tracking = step(0.90 - gGlitch*0.5, rowH)*burst;
    color = mix(color, vec3(1.0), tracking*0.55);

    vec2 blk = floor(uv01*vec2(14.0, 10.0));
    float blkH = hash21(blk + floor(gT*6.0 + seed*30.0));
    float dropout = step(0.965 - gGlitch*0.25, blkH)*burst;
    float dn = hash21(uv01*500.0 + gT);
    color = mix(color, vec3(dn), dropout);

    float cn = (hash21(uv01*800.0 + gT*3.0 + seed) - 0.5)*gColorNoise;
    color.r = sat(color.r + cn*0.8);
    color.g = sat(color.g - cn*0.3);
    color.b = sat(color.b + cn*0.9);

    float speck = step(0.995 - gColorNoise*0.3, hash21(uv01*900.0 - gT*7.0 + seed));
    color = mix(color, vec3(hash21(uv01*1000.0 + gT)), speck*0.8);

    return color;
}

// --------------------------------------------------------- per-cell director --
// Faster cycle than v1 (4-10s instead of 6-15s) so the wall reads busier.
vec3 cellContent(vec2 cellIdx, vec2 p, vec2 uv01){
    float seed = hash21(cellIdx*1.37 + gSeed);
    float period = mix(4.0, 10.0, hash11(seed*11.0 + 1.0));
    float phaseOff = hash11(seed*13.0 + 2.0)*100.0;
    float cyc = floor((gT + phaseOff)/period);
    float modeH = hash21(cellIdx + vec2(cyc*3.71, cyc*1.93) + seed);

    float localPhase = fract((gT + phaseOff)/period);
    float changeFlash = sat((1.0 - smoothstep(0.0, 0.08, localPhase)) + smoothstep(0.92, 1.0, localPhase));

    vec3 col;
    float burst = tubeGlitchiness(seed);
    if (modeH < 0.42){
        col = scopeCell(p, seed);
    } else if (modeH < 0.80){
        col = testcardCell(uv01, p, seed);
    } else {
        float n = hash21(uv01*600.0 + gT*20.0 + seed);
        col = vec3(n)*0.9 + vec3(0.05, 0.08, 0.12);
        burst = max(burst, 0.8);
    }

    col = vhsGlitch(col, uv01, seed, sat(burst*0.5 + changeFlash*0.8 + gGlitch*0.3));
    col = mix(col, vec3(1.0), changeFlash*0.5*step(0.5, hash21(vec2(seed, cyc))));

    float tint = 0.85 + 0.25*hash11(seed*17.0 + 4.0);
    col *= tint;
    return col;
}

// Recomputes just the tube mosaic colour at an arbitrary fragCoord — used to
// sample a stable per-block brightness for the ASCII overlay.
float sceneLuma(vec2 fragCoord){
    vec2 res = RENDERSIZE.xy;
    vec2 wallUV = fragCoord/res;
    vec2 cellF = wallUV*GRID;
    vec2 cellIdx = floor(cellF);
    vec2 local01 = fract(cellF);
    vec2 pRaw = local01*2.0 - 1.0;
    vec2 p = vec2(pRaw.x*gAspect, pRaw.y);
    vec3 c = cellContent(cellIdx, tubeWarp(p), local01);
    return dot(c, vec3(0.299, 0.587, 0.114));
}

// --------------------------------------------------------- wall takeover --
// More frequent than v1 (8-20s cycle instead of 14-30s).
vec2 takeoverEnvelope(){
    float period = mix(8.0, 20.0, 1.0 - gTakeover);
    float cyc = floor(gT/period);
    float h = hash11(cyc*7.13 + 3.0);
    float chance = 0.30 + 0.55*gTakeover;
    float trigger = step(1.0 - chance, h);

    float localT = fract(gT/period)*period;
    float dur = mix(2.0, 6.0, hash11(cyc*3.3 + 1.0));
    float env = 0.0;
    if (trigger > 0.5 && localT < dur){
        float f = localT/dur;
        env = smoothstep(0.0, 0.15, f)*(1.0 - smoothstep(0.7, 1.0, f));
    }
    return vec2(env, cyc);
}
vec3 wallContent(vec2 wp, vec2 wallUV, float seed){
    float modeH = hash11(seed*5.5 + 2.0);
    vec3 col = modeH < 0.5 ? scopeCell(wp*0.9, seed) : testcardCell(wallUV, wp, seed);
    return vhsGlitch(col, wallUV, seed, 0.3 + gGlitch*0.4);
}

// --------------------------------------------------------- ASCII overlay --
// Fakes a monospace ASCII/terminal conversion of the live mosaic: each pixel
// block samples one stable brightness (via sceneLuma) and picks a procedural
// glyph shape from an emptiest-to-fullest ramp (space . - + / x o #).
vec3 asciiOverlay(vec2 fragCoord){
    float cellPx = 10.0;
    vec2 blockID = floor(fragCoord/cellPx);
    vec2 blockCenter = (blockID + 0.5)*cellPx;
    float lum = sceneLuma(blockCenter);
    vec2 q = (fract(fragCoord/cellPx) - 0.5)*2.0;

    float ink = 0.0;
    if (lum < 0.12){
        ink = 0.0;
    } else if (lum < 0.28){
        ink = 1.0 - smoothstep(0.0, 0.35, length(q));
    } else if (lum < 0.42){
        ink = (1.0 - smoothstep(0.0, 0.30, abs(q.y)))*step(abs(q.x), 0.7);
    } else if (lum < 0.56){
        float h = (1.0 - smoothstep(0.0, 0.28, abs(q.y)))*step(abs(q.x), 0.7);
        float v = (1.0 - smoothstep(0.0, 0.28, abs(q.x)))*step(abs(q.y), 0.7);
        ink = max(h, v);
    } else if (lum < 0.70){
        float d = abs(q.x - q.y)/1.41421356;
        ink = (1.0 - smoothstep(0.0, 0.28, d))*step(length(q), 0.9);
    } else if (lum < 0.82){
        float d1 = abs(q.x - q.y)/1.41421356;
        float d2 = abs(q.x + q.y)/1.41421356;
        ink = max(1.0 - smoothstep(0.0, 0.28, d1), 1.0 - smoothstep(0.0, 0.28, d2))*step(length(q), 0.9);
    } else if (lum < 0.92){
        float m = max(abs(q.x), abs(q.y));
        ink = 1.0 - smoothstep(0.0, 0.10, abs(m - 0.75));
    } else {
        ink = step(max(abs(q.x), abs(q.y)), 0.85);
    }

    return vec3(0.25, 1.0, 0.4)*ink;
}

// -------------------------------------------------------- Matrix rain --
vec3 matrixOverlay(vec2 fragCoord){
    float colW = 14.0;
    float colId = floor(fragCoord.x/colW);
    float colSeed = hash11(colId*17.13 + 3.7);
    float rowH = 16.0;
    float row = floor(fragCoord.y/rowH);

    float fallSpeed = mix(3.0, 10.0, colSeed);
    float streamLen = mix(5.0, 18.0, hash11(colId*5.5 + 1.0));
    float phase = row - gT*fallSpeed;
    float posInStream = mod(phase, streamLen);
    float bright = max(1.0 - posInStream/streamLen, 0.0);

    float flick = step(0.15, hash21(vec2(colId, row) + floor(gT*10.0)));
    bright *= flick;

    float headMask = smoothstep(0.85, 1.0, bright);
    vec3 green = vec3(0.1, 1.0, 0.3);
    vec3 headC = vec3(0.85, 1.0, 0.9);
    return mix(green, headC, headMask)*bright;
}

// ------------------------------------------------------- docking HUD --
// Soyuz/spacecraft docking-camera style overlay: alignment rings, a
// wandering crosshair (simulating a probe hunting for lock), corner
// brackets, blinking telemetry boxes and a slow radar sweep.
vec3 dockingOverlay(vec2 wallUV){
    vec2 wc = wallUV*2.0 - 1.0;
    wc.x *= gAspect;
    vec3 hc = vec3(0.25, 1.0, 0.65);
    vec3 hud = vec3(0.0);

    float r = length(wc);
    hud += hc*(1.0 - smoothstep(0.0, 0.006, abs(r - 0.55)));
    hud += hc*(1.0 - smoothstep(0.0, 0.006, abs(r - 0.30)));

    vec2 drift = vec2(sin(gT*0.37)*0.05, cos(gT*0.29)*0.04);
    vec2 wcd = wc - drift;
    float armLen = 0.9; float gap = 0.06; float armW = 0.004;
    float hLine = step(abs(wcd.y), armW)*step(gap, abs(wcd.x))*step(abs(wcd.x), armLen);
    float vLine = step(abs(wcd.x), armW)*step(gap, abs(wcd.y))*step(abs(wcd.y), armLen);
    hud += hc*(hLine + vLine);
    hud += hc*0.6*(1.0 - smoothstep(0.0, 0.015, length(wc)));

    vec2 ac = vec2(abs(wc.x), abs(wc.y));
    vec2 ca = vec2(gAspect*0.9, 0.85);
    float bl = 0.15; float bw = 0.01;
    float legH = step(abs(ac.x - ca.x), bw)*step(ac.y, ca.y)*step(ca.y - bl, ac.y);
    float legV = step(abs(ac.y - ca.y), bw)*step(ac.x, ca.x)*step(ca.x - bl, ac.x);
    hud += hc*(legH + legV);

    float blinkA = step(0.5, fract(gT*2.3));
    vec2 tl = wc - vec2(-ca.x + 0.12, ca.y - 0.08);
    float tlBox = step(abs(tl.x), 0.05)*step(abs(tl.y), 0.02);
    hud += hc*tlBox*blinkA*1.2;

    float blinkB = step(0.5, fract(gT*1.7 + 0.5));
    vec2 tr = wc - vec2(ca.x - 0.12, ca.y - 0.08);
    float trBox = step(abs(tr.x), 0.05)*step(abs(tr.y), 0.02);
    hud += hc*trBox*blinkB*1.2;

    float ang = atan(wc.y, wc.x);
    float sweepAng = mod(gT*0.6, 6.2831853) - 3.14159265;
    float angDiff = abs(mod(ang - sweepAng + 3.14159265, 6.2831853) - 3.14159265);
    float sweep = (1.0 - smoothstep(0.0, 0.05, angDiff))*step(r, 0.55);
    hud += hc*sweep*0.5;

    return hud;
}

// ---------------------------------------------------------------- hue drift --
vec3 hueRotate(vec3 v, float a){
    float c = cos(a), s = sin(a);
    float r = v.r*(0.299 + 0.701*c + 0.168*s) + v.g*(0.587 - 0.587*c + 0.330*s) + v.b*(0.114 - 0.114*c - 0.497*s);
    float g = v.r*(0.299 - 0.299*c - 0.328*s) + v.g*(0.587 + 0.413*c + 0.035*s) + v.b*(0.114 - 0.114*c + 0.292*s);
    float b = v.r*(0.299 - 0.300*c + 1.250*s) + v.g*(0.587 - 0.588*c - 1.050*s) + v.b*(0.114 + 0.886*c - 0.203*s);
    return clamp(vec3(r, g, b), 0.0, 1.0);
}

// -------------------------------------------------------------- wall FX --
// Rotates through 6 full-wall looks (plus an occasional calm gap) every ~7s.
vec3 globalOverlay(vec3 col, vec2 wallUV, vec2 fragCoord){
    float fxPeriod = 7.0;
    float fxCyc = floor(gT/fxPeriod);
    float fxLocal = fract(gT/fxPeriod);
    float fxH = hash11(fxCyc*4.21 + 9.0);
    float fxMode = 0.0;
    if      (fxH >= 0.12 && fxH < 0.26) fxMode = 1.0; // sync-roll / power-flicker
    else if (fxH >= 0.26 && fxH < 0.40) fxMode = 2.0; // travelling scan sweep
    else if (fxH >= 0.40 && fxH < 0.54) fxMode = 3.0; // shared grain
    else if (fxH >= 0.54 && fxH < 0.70) fxMode = 4.0; // ASCII conversion
    else if (fxH >= 0.70 && fxH < 0.86) fxMode = 5.0; // Matrix rain
    else if (fxH >= 0.86)               fxMode = 6.0; // docking-camera HUD

    float fade = smoothstep(0.0, 0.15, fxLocal)*(1.0 - smoothstep(0.85, 1.0, fxLocal))*uOverlayMix;

    if (fxMode == 1.0){
        float rollY = fract(gT*0.12 + fxCyc*0.37);
        float band = 1.0 - smoothstep(0.0, 0.02, abs(wallUV.y - rollY));
        col = mix(col, vec3(1.0), band*fade*0.8);
        col *= 1.0 + 0.12*sin(gT*45.0)*fade;
    } else if (fxMode == 2.0){
        float diag = (wallUV.x + wallUV.y)*0.5;
        float travel = fract(gT*0.15);
        float band = 1.0 - smoothstep(0.0, 0.06, abs(diag - travel));
        col += vec3(0.6, 0.7, 0.8)*band*fade*0.5;
    } else if (fxMode == 3.0){
        float n = hash21(fragCoord + floor(gT*24.0));
        col += (n - 0.5)*0.25*fade;
    } else if (fxMode == 4.0){
        vec3 a = asciiOverlay(fragCoord);
        col = mix(col, col*0.15 + a, fade);
    } else if (fxMode == 5.0){
        vec3 m = matrixOverlay(fragCoord);
        col = mix(col, col*0.15 + m, fade);
    } else if (fxMode == 6.0){
        vec3 d = dockingOverlay(wallUV);
        col = mix(col, col*0.6 + d, fade);
    }

    return col;
}

// ------------------------------------------------------------------ main --
void main(){
    vec2 res = RENDERSIZE.xy;
    gAspect = res.x/res.y;
    gT = TIME*uSpeed;
    gGlitch = uGlitch; gColorNoise = uColorNoise; gCurvature = uCurvature;
    gBezel = uBezel; gGlow = uScopeGlow;
    gTakeover = uTakeover; gSeed = uSeed;

    vec2 wallUV = gl_FragCoord.xy/res;
    vec2 cellF = wallUV*GRID;
    vec2 cellIdx = floor(cellF);
    vec2 local01 = fract(cellF);
    vec2 pRaw = local01*2.0 - 1.0;
    vec2 p = vec2(pRaw.x*gAspect, pRaw.y);
    vec2 pWarp = tubeWarp(p);

    vec3 mosaic = cellContent(cellIdx, pWarp, local01);
    float mask = tubeMask(pWarp);
    mosaic = mix(vec3(0.01, 0.01, 0.015)*(1.0 - gBezel*0.9), mosaic, mask);

    vec2 toE = takeoverEnvelope();
    if (toE.x > 0.001){
        vec2 wc = wallUV*2.0 - 1.0;
        vec2 wp = vec2(wc.x*gAspect, wc.y)*0.85;
        vec3 wallCol = wallContent(wp, wallUV, toE.y);
        mosaic = mix(mosaic, wallCol, toE.x);
    }

    vec3 final = globalOverlay(mosaic, wallUV, gl_FragCoord.xy);
    final = hueRotate(final, sin(gT*0.11)*uHueDrift*PI);
    gl_FragColor = vec4(clamp(final, 0.0, 1.0), 1.0);
}
