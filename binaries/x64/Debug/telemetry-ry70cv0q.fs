/*{
  "DESCRIPTION": "digital screen — a tall NERV/MAGI console that scrolls vertically and loops. Four packed bands: an ORBITAL globe (rotating wireframe Earth, tilted orbit rings, satellites, a patrolling spaceship with a dashed comms beam, an orbiting particle swarm and a rotating scan cone, all depth-dimmed front/back); a BIOMETRIC human-body hologram scanned by a sweeping scan bar, roving targeting reticle, jumping lock-on brackets, joint nodes, ribcage, pulsing heart, spine particle flow, ECG trace, detector needle gauge and a DNA double-helix sequencer; a DATA deck (decoding 7-seg title, flickering binary, twin frequency waveforms, roman-numeral selector, ms counters, level gauge, pulsing hexes); and a HARMONICS panel (phase-shifted sine bundle with particle fountain, psychographic scatter, radial MAGI diagram, spectrum analyzer, alert triangle). Streaming code rain runs behind everything, vertical data buses carry packets, and a fixed HUD frames it with status LEDs, tickers, a diagonal scan sweep and a scroll-loop gauge — then CRT post: barrel, scanlines, aperture grille, flicker, tear, grain, vignette.",
  "CREDIT": "CC0",
  "CATEGORIES": ["generator", "ui", "retro", "scifi"],
  "INPUTS": [
    { "NAME": "uSpeed",    "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "uScroll",   "TYPE": "float", "DEFAULT": 0.35, "MIN": 0.0, "MAX": 1.5 },
    { "NAME": "uGlow",     "TYPE": "float", "DEFAULT": 0.60, "MIN": 0.0, "MAX": 1.5 },
    { "NAME": "uScan",     "TYPE": "float", "DEFAULT": 0.50, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uFlicker",  "TYPE": "float", "DEFAULT": 0.40, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uCurve",    "TYPE": "float", "DEFAULT": 0.40, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uAlert",    "TYPE": "float", "DEFAULT": 0.00, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uGrid",     "TYPE": "float", "DEFAULT": 0.60, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uVignette", "TYPE": "float", "DEFAULT": 0.45, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

// MAGI MONITOR — ISF port of the Shadertoy/WebGL2 version
// (GLSL ES 1.00: literal loop bounds, gl_FragColor, TIME/RENDERSIZE).

#define PI 3.14159265
#define WORLD_H 3.6

float gPx, gSp, gT;
vec3 CO, CR, CG, CC, CY, CP;

float h11(float n){ return fract(sin(n*127.1)*43758.5453); }
float h21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7)))*43758.5453); }
float vnoise(float x){
    float i = floor(x), f = fract(x);
    return mix(h11(i), h11(i + 1.0), f*f*(3.0 - 2.0*f));
}
float fbm(float x){
    float v = 0.0, a = 0.55;
    for (int i = 0; i < 4; i++){ v += a*vnoise(x); x = x*2.17 + 9.7; a *= 0.5; }
    return v;
}

float sdBox(vec2 p, vec2 b){
    vec2 d = abs(p) - b;
    return length(max(d, vec2(0.0))) + min(max(d.x, d.y), 0.0);
}
float segH(vec2 p, float l){ return length(vec2(max(abs(p.x) - l, 0.0), p.y)); }
float sdSeg(vec2 p, vec2 a, vec2 b){
    vec2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba)/dot(ba, ba), 0.0, 1.0);
    return length(pa - ba*h);
}
// Rational bloom instead of exp(): neon() is inlined ~150 times, and dropping that
// many transcendentals is a big cut in shader compile time and per-pixel cost.
float neon(float d, float w){
    float dd = abs(d) - w;
    float core = clamp(0.5 - dd/(2.0*gPx), 0.0, 1.0);   // linear AA (cheaper than smoothstep)
    float g = max(dd, 0.0)*90.0;
    return core + (uGlow*0.8)/(1.0 + g*g);
}
float nseg(vec2 p, vec2 a, vec2 b, float w){ return neon(sdSeg(p, a, b), w); }
float fillIn(float d){ return clamp(0.5 - d/(2.0*gPx), 0.0, 1.0); }
vec2 rot2(vec2 v, float a){
    float c = cos(a), s = sin(a);
    return vec2(c*v.x - s*v.y, s*v.x + c*v.y);
}

float lcorner(vec2 p, vec2 corner, vec2 dir, float len, float w){
    float d1 = sdSeg(p, corner, corner + vec2(-dir.x*len, 0.0));
    float d2 = sdSeg(p, corner, corner + vec2(0.0, -dir.y*len));
    return neon(min(d1, d2), w);
}
float sqBrackets(vec2 p, vec2 c, vec2 h, float len, float w){
    float g = 0.0;
    g = max(g, lcorner(p, c + vec2( h.x,  h.y), vec2( 1.0,  1.0), len, w));
    g = max(g, lcorner(p, c + vec2(-h.x,  h.y), vec2(-1.0,  1.0), len, w));
    g = max(g, lcorner(p, c + vec2( h.x, -h.y), vec2( 1.0, -1.0), len, w));
    g = max(g, lcorner(p, c + vec2(-h.x, -h.y), vec2(-1.0, -1.0), len, w));
    return g;
}
float reticle(vec2 p, vec2 c, float s, float w){
    vec2 q = p - c;
    float g = 0.0;
    g = max(g, nseg(q, vec2(-s, 0.0), vec2(-s*0.35, 0.0), w));
    g = max(g, nseg(q, vec2( s, 0.0), vec2( s*0.35, 0.0), w));
    g = max(g, nseg(q, vec2(0.0, -s), vec2(0.0, -s*0.35), w));
    g = max(g, nseg(q, vec2(0.0,  s), vec2(0.0,  s*0.35), w));
    g = max(g, neon(abs(length(q) - s*0.5), w));
    g = max(g, fillIn(length(q) - s*0.08));
    return g;
}

// ---- 7-segment glyphs ------------------------------------------------------
float bitOn(float m, float b){ return step(0.5, mod(floor(m/b), 2.0)); }
// Union the 7 segment distances first, then stroke ONCE (OFF segments get a big
// distance) — seg7 is inlined at dozens of call sites.
float seg7(vec2 p, float m, float sc, float w){
    vec2 q = p / sc;
    if (abs(q.x) > 1.9 || abs(q.y) > 1.9) return 0.0;
    float d = 1e3;
    d = min(d, segH(q - vec2(0.0,  0.75), 0.30) + (1.0 - bitOn(m,  1.0))*1e3);          // A
    d = min(d, segH(vec2(q.y - 0.375, q.x - 0.45), 0.26) + (1.0 - bitOn(m,  2.0))*1e3); // B
    d = min(d, segH(vec2(q.y + 0.375, q.x - 0.45), 0.26) + (1.0 - bitOn(m,  4.0))*1e3); // C
    d = min(d, segH(q - vec2(0.0, -0.75), 0.30) + (1.0 - bitOn(m,  8.0))*1e3);          // D
    d = min(d, segH(vec2(q.y + 0.375, q.x + 0.45), 0.26) + (1.0 - bitOn(m, 16.0))*1e3); // E
    d = min(d, segH(vec2(q.y - 0.375, q.x + 0.45), 0.26) + (1.0 - bitOn(m, 32.0))*1e3); // F
    d = min(d, segH(q, 0.30) + (1.0 - bitOn(m, 64.0))*1e3);                             // G
    return neon(d*sc, w);
}
float dmask(float d){
    if (d < 0.5) return 63.0;  if (d < 1.5) return 6.0;   if (d < 2.5) return 91.0;
    if (d < 3.5) return 79.0;  if (d < 4.5) return 102.0; if (d < 5.5) return 109.0;
    if (d < 6.5) return 125.0; if (d < 7.5) return 7.0;   if (d < 8.5) return 127.0;
    return 111.0;
}
// Both text helpers are O(1): a pixel lands in exactly ONE glyph cell, so index it
// directly instead of looping every glyph (they are inlined at ~50 call sites).
float number(vec2 p, float val, float nd, float sc, float w){
    float pitch = sc*1.35;
    float halfW = nd*pitch*0.5;
    if (abs(p.x) > halfW + sc || abs(p.y) > sc*2.0) return 0.0;
    float idx = floor((p.x + halfW)/pitch);
    if (idx < 0.0 || idx >= nd) return 0.0;
    vec2 q = p - vec2((idx - (nd - 1.0)*0.5)*pitch, 0.0);
    float dg = mod(floor(val/pow(10.0, nd - 1.0 - idx)), 10.0);
    return seg7(q, dmask(dg), sc, w);
}
float glyphRow(vec2 p, vec2 c, float n, float sc, float w, float seed, float rr){
    vec2 q = p - c;
    float pitch = sc*1.5;
    float halfW = n*pitch*0.5;
    if (abs(q.x) > halfW + sc || abs(q.y) > sc*2.0) return 0.0;
    float idx = floor((q.x + halfW)/pitch);
    if (idx < 0.0 || idx >= n) return 0.0;
    vec2 pp = q - vec2((idx - (n - 1.0)*0.5)*pitch, 0.0);
    float sd = seed + idx*7.31 + floor(gT*rr + h11(seed + idx)*3.0)*13.7;
    float m = 1.0 + floor(h11(sd)*126.99);
    return seg7(pp, m, sc, w);
}

// ---- data-deck panels ------------------------------------------------------
vec3 titleBlock(vec2 p){
    if (p.y < 0.66 || p.x < 0.36) return vec3(0.0);
    vec3 acc = vec3(0.0);
    acc += CO*1.10*glyphRow(p, vec2(0.98, 0.775), 9.0, 0.058, 0.011, 33.0, 0.5);
    acc += CO*0.80*neon(segH(p - vec2(0.98, 0.700), 0.58), 0.0018);
    acc += CO*0.55*glyphRow(p, vec2(0.70, 0.868), 12.0, 0.013, 0.0032, 91.0, 0.8);
    acc += CO*0.75*glyphRow(p, vec2(1.52, 0.868), 1.0, 0.016, 0.0036, 55.0, 0.3);
    return acc;
}
vec3 binaryBox(vec2 p){
    vec2 c = vec2(0.98, 0.585), b = vec2(0.58, 0.080);
    if (sdBox(p - c, b) > 0.05) return vec3(0.0);
    vec3 acc = CO*0.9*neon(sdBox(p - c, b) - 0.012, 0.0022);
    vec2 q = p - c + b;
    q -= vec2(0.022, 0.017);
    vec2 cellSz = vec2(0.036, 0.043);
    vec2 id = floor(q/cellSz);
    vec2 cc = (fract(q/cellSz) - 0.5)*cellSz;
    if (id.x >= 0.0 && id.x < 31.0 && id.y >= 0.0 && id.y < 3.0){
        float rnd = h21(id + vec2(0.0, 7.0));
        float flip = floor(gT*(0.4 + rnd*2.2));
        float bitv = step(0.5, h21(id + vec2(flip*13.7, flip*7.1)));
        float g;
        if (bitv > 0.5) g = neon(segH(cc.yx, 0.013), 0.0045);
        else            g = neon(abs(sdBox(cc, vec2(0.004, 0.010)) - 0.006), 0.0042);
        acc += CO*g*(0.70 + 0.30*h21(id + 3.3));
    }
    return acc;
}
vec3 leftColumn(vec2 p){
    if (p.x > 0.36 || p.y > 0.55) return vec3(0.0);
    vec3 acc = vec3(0.0);
    acc += CO*0.65*glyphRow(p, vec2(0.17, 0.505), 6.0, 0.018, 0.0042, 41.0, 0.0);
    acc += CO*0.65*glyphRow(p, vec2(0.17, 0.462), 6.0, 0.018, 0.0042, 47.0, 0.0);
    acc += CC*1.10*number(p - vec2(0.185, 0.415), mod(floor(gT*77.0), 10000.0), 4.0, 0.020, 0.0038);
    vec2 gc = vec2(0.1225, 0.21), gb = vec2(0.0775, 0.16);
    float gd = sdBox(p - gc, gb) - 0.015;
    acc += CP*0.40*fillIn(gd);
    acc += CO*0.90*neon(gd, 0.0022);
    vec2 q = p - gc;
    if (gd < -0.004){
        float fy = (q.y + gb.y)/0.030;
        float row = floor(fy);
        float ry = (fract(fy) - 0.5)*0.030;
        float lhalf = (mod(row, 5.0) < 0.5) ? 0.026 : 0.013;
        float td = length(vec2(max(abs(q.x + gb.x - 0.012 - lhalf) - lhalf, 0.0), ry));
        acc += CO*0.75*neon(td, 0.0016);
        float lev = -gb.y + 2.0*gb.y*(0.5 + 0.44*sin(gT*0.5 + 1.3));
        acc += CY*neon(length(vec2(max(abs(q.x) - gb.x + 0.02, 0.0), q.y - lev)), 0.0022);
    }
    return acc;
}
vec3 romanBox(vec2 p){
    vec2 c = vec2(0.48, 0.25), b = vec2(0.08, 0.20);
    if (sdBox(p - c, b) > 0.05) return vec3(0.0);
    vec3 acc = CO*0.9*neon(sdBox(p - c, b) - 0.010, 0.0022);
    acc += CO*0.7*neon(segH(p - c, 0.08), 0.0018);
    float sel = mod(floor(gT*0.6), 2.0);
    vec2 ac = vec2(0.48, (sel < 0.5) ? 0.35 : 0.15);
    acc += CO*0.10*fillIn(sdBox(p - ac, vec2(0.068, 0.085)));
    {
        vec2 q = p - vec2(0.48, 0.35);
        float g = neon(segH(q.yx, 0.055), 0.0060);
        g = max(g, neon(segH(q - vec2(0.0, 0.062), 0.020), 0.0040));
        g = max(g, neon(segH(q + vec2(0.0, 0.062), 0.020), 0.0040));
        acc += CO*g*((sel < 0.5) ? 1.15 : 0.45);
    }
    {
        vec2 q = p - vec2(0.48, 0.15);
        float g =        neon(segH(vec2(q.y, q.x - 0.024), 0.055), 0.0060);
        g = max(g,       neon(segH(vec2(q.y, q.x + 0.024), 0.055), 0.0060));
        g = max(g, neon(segH(q - vec2(0.0, 0.062), 0.042), 0.0040));
        g = max(g, neon(segH(q + vec2(0.0, 0.062), 0.042), 0.0040));
        acc += CO*g*((sel < 0.5) ? 0.45 : 1.15);
    }
    return acc;
}
vec3 wavePanel(vec2 p, vec2 c, vec2 b, float style, float seed){
    if (sdBox(p - c, b) > 0.05) return vec3(0.0);
    vec3 acc = CO*0.9*neon(sdBox(p - c, b) - 0.012, 0.0022);
    vec2 q = p - c;
    if (abs(q.x) < b.x - 0.008 && abs(q.y) < b.y - 0.008){
        vec2 rp = vec2(q.y, -(q.x + b.x - 0.024));
        acc += CO*0.60*glyphRow(rp, vec2(0.02, 0.0), 10.0, 0.011, 0.0026, seed, 0.0);
        float xw;
        if (style < 0.5){
            xw = 0.02 + (fbm(q.y*6.0 - gT*1.3 + seed) - 0.55)*0.10;
            float dd = abs(q.x - xw);
            acc += CY*(neon(dd, 0.0065) + exp(-dd*30.0)*0.30*uGlow);
        } else {
            float env = 0.12 + pow(fbm(q.y*2.5 + gT*0.9 + seed), 2.0)*1.25;
            float n2 = (vnoise(q.y*70.0 - gT*6.0) - 0.5)*2.0;
            xw = 0.02 + n2*abs(n2)*env*0.055;
            float dd = abs(q.x - xw);
            acc += (CG*0.55 + CY*0.30)*(neon(dd, 0.0022) + exp(-dd*45.0)*0.20*uGlow);
        }
        acc += CO*0.7*number(q - vec2(0.055, -b.y + 0.035), (style < 0.5) ? 1.0 : 2.0, 2.0, 0.011, 0.0025);
    }
    return acc;
}
vec3 counterCol(vec2 p){
    if (p.x < 0.99 || p.x > 1.23 || p.y > 0.50) return vec3(0.0);
    vec3 acc = vec3(0.0);
    vec2 q = p - vec2(1.035, 0.25);
    if (abs(q.x) < 0.045 && abs(q.y) < 0.215){
        acc += CG*0.6*neon(segH(vec2(q.y, q.x + 0.017), 0.205), 0.0014);
        float fy = (q.y + 0.205)/0.018;
        float row = floor(fy);
        float ry = (fract(fy) - 0.5)*0.018;
        float lhalf = (mod(row, 5.0) < 0.5) ? 0.011 : 0.005;
        float td = length(vec2(max(abs(q.x + 0.017 - lhalf) - lhalf, 0.0), ry));
        acc += CG*0.75*neon(td, 0.0013);
    }
    acc += CO*1.15*number(p - vec2(1.13, 0.40), mod(floor(gT*100.0), 10000.0), 4.0, 0.021, 0.0038);
    acc += CO*0.50*glyphRow(p, vec2(1.13, 0.345), 6.0, 0.010, 0.0024, 77.0, 0.4);
    vec2 wc = vec2(1.13, 0.24), wb = vec2(0.075, 0.042);
    float blink = step(0.5, fract(gT*(1.2 + 3.0*uAlert)));
    float dW = sdBox(p - wc, wb) - 0.010;
    acc += CR*(0.28 + 0.34*blink)*fillIn(dW);
    acc += CR*neon(dW, 0.0022)*(0.6 + 0.6*blink);
    acc += vec3(1.0, 0.80, 0.55)*(0.35 + 0.85*blink)*glyphRow(p, wc, 5.0, 0.015, 0.0034, 3.0, 0.0);
    acc += CO*0.40*glyphRow(p, vec2(1.13, 0.130), 7.0, 0.009, 0.0022, 15.0, 0.7);
    acc += CO*0.40*glyphRow(p, vec2(1.13, 0.100), 7.0, 0.009, 0.0022, 19.0, 0.7);
    return acc;
}
vec3 hexPanel(vec2 p){
    vec2 c = vec2(1.40, 0.25), b = vec2(0.155, 0.20);
    if (sdBox(p - c, b) > 0.05) return vec3(0.0);
    vec3 acc = CO*0.9*neon(sdBox(p - c, b) - 0.012, 0.0022);
    vec2 q = p - c;
    if (abs(q.x) < b.x - 0.010 && abs(q.y) < b.y - 0.010){
        float s = 0.047;
        vec2 u = q/s;
        vec2 rr = vec2(1.0, 1.7320508);
        vec2 hh = rr*0.5;
        vec2 a1 = mod(u, rr) - hh;
        vec2 a2 = mod(u - hh, rr) - hh;
        vec2 gv = (dot(a1, a1) < dot(a2, a2)) ? a1 : a2;
        vec2 id = u - gv;
        vec2 av = abs(gv);
        float hd = max(dot(av, vec2(0.5, 0.8660254)), av.x);
        float hs = h21(id);
        float pulse = 0.5 + 0.5*sin(gT*(1.5 + uAlert*3.0) + hs*6.2831);
        pulse = pow(pulse, 3.0)*(0.45 + 0.55*step(0.30, hs));
        acc += CO*pulse*0.42*fillIn((hd - 0.40)*s);
        acc += CO*0.65*neon((0.5 - hd)*s, 0.0016);
    }
    return acc;
}
float connectorSym(vec2 p){
    float g =        neon(segH(p, 0.011), 0.0020);
    g = max(g, neon(segH(vec2(p.y, p.x + 0.015), 0.012), 0.0020));
    g = max(g, neon(segH(vec2(p.y, p.x - 0.015), 0.012), 0.0020));
    return g;
}
float plusSym(vec2 p){
    return max(neon(segH(p, 0.008), 0.0020), neon(segH(p.yx, 0.008), 0.0020));
}
vec3 ornaments(vec2 p){
    vec3 acc = vec3(0.0);
    float b0 = 0.5 + 0.6*step(0.5, fract(gT*0.8));
    float b1 = 0.5 + 0.6*step(0.5, fract(gT*0.8 + 0.31));
    float b2 = 0.5 + 0.6*step(0.5, fract(gT*0.8 + 0.62));
    acc += CG*b2*connectorSym(p - vec2(0.585, 0.250));
    acc += CG*b0*connectorSym(p - vec2(0.800, 0.250));
    acc += CG*b1*connectorSym(p - vec2(1.225, 0.250));
    acc += CG*b2*connectorSym(p - vec2(1.310, 0.478));
    acc += CY*b1*plusSym(p - vec2(0.585, 0.380));
    acc += CY*b2*plusSym(p - vec2(0.800, 0.380));
    acc += CY*b0*plusSym(p - vec2(1.225, 0.380));
    acc += CY*b0*plusSym(p - vec2(0.585, 0.105));
    acc += CY*b1*plusSym(p - vec2(0.800, 0.105));
    return acc;
}

// ---- spaceship + satellite icons -------------------------------------------
vec3 shipDraw(vec2 p, vec2 pos, float ang, float sc, float dep){
    vec2 q = rot2(p - pos, -ang);
    float w = 0.0030;
    float g = 0.0;
    g = max(g, nseg(q, vec2(1.30, 0.0)*sc, vec2(-0.70, 0.0)*sc, w));
    g = max(g, nseg(q, vec2(1.30, 0.0)*sc, vec2(-0.50, 0.72)*sc, w));
    g = max(g, nseg(q, vec2(1.30, 0.0)*sc, vec2(-0.50,-0.72)*sc, w));
    g = max(g, nseg(q, vec2(-0.50, 0.72)*sc, vec2(-0.70, 0.0)*sc, w));
    g = max(g, nseg(q, vec2(-0.50,-0.72)*sc, vec2(-0.70, 0.0)*sc, w));
    g = max(g, neon(sdBox(q - vec2(0.45, 0.0)*sc, vec2(0.24, 0.11)*sc) - 0.01, w));
    vec3 col = CO*g*dep;
    col += CC*0.9*dep*fillIn(sdBox(q - vec2(0.55, 0.0)*sc, vec2(0.07, 0.04)*sc));
    float fl = 0.55 + 0.45*h11(floor(gT*40.0));
    float td = sdSeg(q, vec2(-0.70, 0.0)*sc, vec2(-1.20 - fl*0.4, 0.0)*sc);
    col += CC*exp(-td*45.0)*0.7*dep*fl;
    return col;
}
vec3 satDraw(vec2 p, vec2 pos, float sc, float dep, float ph){
    vec2 q = (p - pos);
    float w = 0.0026;
    float g = 0.0;
    g = max(g, neon(sdBox(q, vec2(0.40, 0.30)*sc) - 0.05*sc, w));
    g = max(g, neon(sdBox(q - vec2(1.05, 0.0)*sc, vec2(0.50, 0.24)*sc) - 0.02*sc, w));
    g = max(g, neon(sdBox(q + vec2(1.05, 0.0)*sc, vec2(0.50, 0.24)*sc) - 0.02*sc, w));
    g = max(g, nseg(q, vec2(-0.40, 0.0)*sc, vec2(-0.55, 0.0)*sc, w));
    g = max(g, nseg(q, vec2( 0.40, 0.0)*sc, vec2( 0.55, 0.0)*sc, w));
    g = max(g, nseg(q - vec2(1.05, 0.0)*sc, vec2(0.0, -0.22)*sc, vec2(0.0, 0.22)*sc, w));
    g = max(g, nseg(q + vec2(1.05, 0.0)*sc, vec2(0.0, -0.22)*sc, vec2(0.0, 0.22)*sc, w));
    vec3 col = CO*g*dep;
    float bl = step(0.5, fract(gT*1.5 + ph));
    col += CR*fillIn(length(q - vec2(0.0, 0.42)*sc) - 0.10*sc)*bl*dep;
    return col;
}

// ---- extra primitives: smooth union, particles, spectrum -------------------
float smin(float a, float b, float k){
    float h = clamp(0.5 + 0.5*(b - a)/k, 0.0, 1.0);
    return mix(b, a, h) - k*h*(1.0 - h);
}
float capsule(vec2 p, vec2 a, vec2 b, float r){ return sdSeg(p, a, b) - r; }
vec3 particleField(vec2 p, vec2 c, vec2 hs, float n, float seed, vec2 vel, vec3 col, float sz){
    vec3 acc = vec3(0.0);
    for (int i = 0; i < 16; i++){
        if (float(i) >= n) continue;
        float fi = float(i);
        vec2 base = vec2(h11(seed + fi*1.7), h11(seed + fi*3.1 + 5.0));
        vec2 pos = c - hs + mod(base*2.0*hs + vel*gT, 2.0*hs);
        float tw = max(0.45 + 0.55*sin(gT*4.0 + fi*1.3), 0.0);
        acc += col*tw*fillIn(length(p - pos) - sz);
    }
    return acc;
}
vec3 spectrumBar(vec2 p, vec2 c, vec2 hs, float nb, float seed, vec3 col){
    vec3 acc = CO*0.25*neon(sdBox(p - c, hs) - 0.006, 0.0016);
    vec2 q = p - c + hs;
    if (q.x > 0.0 && q.x < 2.0*hs.x && q.y > 0.0 && q.y < 2.0*hs.y){
        float bw = 2.0*hs.x/nb;
        float bi = floor(q.x/bw);
        float xin = fract(q.x/bw);
        float h = 0.12 + 0.88*abs(sin(gT*(2.0 + bi*0.35) + seed + bi*0.7))*(0.4 + 0.6*h11(bi + seed));
        float inbar = step(0.14, xin)*step(xin, 0.86);
        if (q.y < h*2.0*hs.y) acc += col*(0.45 + 0.5*h)*inbar;
        acc += col*0.4*neon(q.y - h*2.0*hs.y, 0.0022)*inbar;
    }
    return acc;
}

// ---- background + buses + module frame -------------------------------------
vec3 bgWorld(vec2 p){
    vec3 acc = vec3(0.012, 0.005, 0.002);
    float g = 0.15;
    vec2 q = vec2(p.x + gT*0.006, p.y);
    vec2 cc = mod(q, g) - g*0.5;
    vec2 id = floor(q/g);
    float cr = max(neon(segH(cc, 0.010), 0.0013), neon(segH(cc.yx, 0.010), 0.0013));
    acc += CO*cr*0.10*uGrid*(0.5 + 0.5*h21(id));
    vec2 f2 = mod(p*vec2(1.0, 1.0)/0.05, 1.0) - 0.5;
    acc += CO*0.05*uGrid*fillIn(length(f2)*0.05 - 0.004);
    // streaming code rain — only the bright falling head costs a seg7
    vec2 rcz = vec2(0.030, 0.028);
    vec2 rid = floor(p/rcz);
    float spd = 0.4 + 0.8*h11(rid.x*1.3);
    float litw = fract(-p.y*0.9 + gT*spd*0.6 + h11(rid.x*2.7));
    float head = smoothstep(0.55, 1.0, litw);
    if (head > 0.02){
        vec2 rf = fract(p/rcz) - 0.5;
        float gm = 1.0 + floor(h11(rid.x*3.1 + rid.y*7.7 + floor(gT*4.0))*126.0);
        acc += CO*0.13*uGrid*head*seg7(rf*rcz, gm, 0.0095, 0.0022);
    }
    return acc;
}
vec3 busLayer(vec2 p){
    vec3 acc = vec3(0.0);
    for (int i = 0; i < 2; i++){
        float bx = (i == 0) ? 0.075 : 1.525;
        float d = abs(p.x - bx);
        acc += CG*0.35*neon(d, 0.0012);
        float ph = mod(p.y*3.0 - gT*(1.3 + float(i)*0.4), 1.0);
        float pk = smoothstep(0.14, 0.0, ph);
        acc += CC*0.5*pk*fillIn(d - 0.006);
        float rung = neon(segH(vec2(d - 0.006, mod(p.y, 0.12) - 0.06), 0.006), 0.0012);
        acc += CG*0.18*rung;
    }
    return acc;
}
vec3 moduleFrame(vec2 p, float cy, float titleSeed){
    vec3 acc = vec3(0.0);
    float inx = step(abs(p.x - 0.80), 0.775);
    acc += CO*0.30*neon(sdBox(p - vec2(0.80, cy), vec2(0.775, 0.435)) - 0.010, 0.0022);
    acc += CO*0.6*neon(p.y - (cy + 0.375), 0.0013)*inx;
    acc += CO*0.95*glyphRow(p, vec2(0.52, cy + 0.408), 11.0, 0.0135, 0.0032, titleSeed, 0.4);
    acc += CO*0.55*neon(segH(p - vec2(0.52, cy + 0.386), 0.34), 0.0013);
    acc += CC*0.9*number(p - vec2(1.40, cy + 0.408), mod(floor(gT*30.0), 100000.0), 5.0, 0.012, 0.0026);
    float rec = step(0.5, fract(gT*1.4 + titleSeed));
    acc += CR*rec*fillIn(length(p - vec2(0.075, cy + 0.408)) - 0.008);
    acc += CO*0.5*neon(p.y - (cy - 0.375), 0.0012)*inx;
    acc += CO*0.5*glyphRow(p, vec2(0.80, cy - 0.408), 32.0, 0.0092, 0.0021, titleSeed + 50.0 + floor(gT*4.0), 3.0);
    for (int s = 0; s < 2; s++){
        float sx = (s == 0) ? 0.135 : 1.465;
        float dd = abs(p.x - sx);
        if (dd < 0.03 && abs(p.y - cy) < 0.36){
            float fy = (p.y - cy)/0.05;
            float ry = (fract(fy) - 0.5)*0.05;
            float lhalf = (mod(floor(fy), 5.0) < 0.5) ? 0.018 : 0.008;
            float td = length(vec2(max(abs(p.x - sx - ((s == 0) ? lhalf : -lhalf)) - lhalf, 0.0), ry));
            acc += CO*0.4*neon(td, 0.0012);
        }
    }
    return acc;
}

// ---- MODULE 1: orbital globe -----------------------------------------------
vec3 globeModule(vec2 p){
    if (p.y > 0.92) return vec3(0.0);
    float cy = 0.45;
    vec3 acc = moduleFrame(p, cy, 131.0);
    vec2 c = vec2(0.44, 0.44);
    float R = 0.25;

    for (int i = 0; i < 10; i++){
        float fi = float(i);
        vec2 sp = c + vec2((h11(fi*1.7) - 0.5)*1.05, (h11(fi*3.9 + 2.0) - 0.5)*0.70);
        float tw = 0.4 + 0.6*abs(sin(gT*2.5 + fi*1.3));
        acc += CY*0.28*tw*fillIn(length(p - sp) - 0.0035);
    }
    for (int k = 0; k < 3; k++){
        float fk = float(k);
        float rx = 0.40 - fk*0.05, ry = 0.15 - fk*0.017;
        float tl = -0.28 + fk*0.22;
        vec2 e = rot2(p - c, tl);
        float d = (length(vec2(e.x/rx, e.y/ry)) - 1.0)*ry;
        acc += CC*0.22*neon(d, 0.0016);
    }
    vec2 qs = (p - c)/R;
    float r = length(qs);
    if (r < 1.35){
        acc += CP*0.55*neon((r - 1.0)*R, 0.0024);
        if (r < 1.0){
            float z = sqrt(1.0 - r*r);
            acc += CP*0.10*(0.35 + 0.65*z);
            for (int s = 0; s < 2; s++){
                float zz = (s == 0) ? z : -z;
                vec3 P = vec3(qs, zz);
                P.xz = rot2(P.xz, gT*0.35);
                P.yz = rot2(P.yz, 0.42);
                float lon = atan(P.z, P.x);
                float lat = asin(clamp(P.y, -1.0, 1.0));
                float clat = max(cos(lat), 0.05);
                float dLon = abs(fract(lon/0.5236 + 0.5) - 0.5)*0.5236*clat*R;
                float dLat = abs(fract(lat/0.4488 + 0.5) - 0.5)*0.4488*R;
                float wire = max(neon(dLon, 0.0015), neon(dLat, 0.0015));
                acc += CC*wire*((s == 0) ? 0.85 : 0.24)*(0.30 + 0.70*abs(zz));
            }
        }
    }
    for (int i = 0; i < 4; i++){
        float fi = float(i);
        float k = mod(fi, 3.0);
        float rx = 0.40 - k*0.05, ry = 0.15 - k*0.017;
        float tl = -0.28 + k*0.22;
        float a = gT*(0.55 + 0.12*fi) + fi*1.9;
        vec2 pos = c + rot2(vec2(cos(a)*rx, sin(a)*ry), -tl);
        float dep = (sin(a) > 0.0) ? 1.0 : 0.28;
        acc += satDraw(p, pos, 0.030, dep, fi*1.3);
    }
    // orbiting particle swarm
    for (int i = 0; i < 12; i++){
        float fi = float(i);
        float a = gT*0.9 + fi*0.5236;
        vec2 pos = c + rot2(vec2(cos(a)*0.35, sin(a)*0.125), 0.15);
        float dep = (sin(a) > 0.0) ? 1.0 : 0.30;
        acc += CY*0.5*dep*fillIn(length(p - pos) - 0.0038);
    }
    // spaceship + dashed comms beam
    {
        float a = gT*0.32 + 1.2;
        float rx = 0.44, ry = 0.18, tl = 0.20;
        vec2 shipPos = c + rot2(vec2(cos(a)*rx, sin(a)*ry), -tl);
        vec2 tang = rot2(vec2(-sin(a)*rx, cos(a)*ry), -tl);
        float ang = atan(tang.y, tang.x);
        float dep = (sin(a) > 0.0) ? 1.0 : 0.32;
        acc += shipDraw(p, shipPos, ang, 0.052, dep);
        vec2 grnd = c + vec2(0.11, -0.16);
        vec2 bdir = normalize(grnd - shipPos);
        float bdist = sdSeg(p, shipPos, grnd);
        float along = dot(p - shipPos, bdir);
        float dash = step(0.5, fract(along*36.0 - gT*5.0));
        acc += CC*0.45*neon(bdist, 0.0015)*dash*dep;
        acc += CR*fillIn(length(p - grnd) - 0.006)*step(0.5, fract(gT*2.0));
    }
    // expanding radar ping rings
    for (int i = 0; i < 2; i++){
        float t = fract(gT*0.4 + float(i)*0.5);
        acc += CG*0.40*(1.0 - t)*neon(length(p - c) - t*0.42, 0.0016);
    }
    // rotating scan cone
    {
        float ca = gT*0.7;
        vec2 apex = c + vec2(0.0, 0.30);
        vec2 rdp = p - apex;
        float pang = atan(rdp.x, -rdp.y);
        float cone = smoothstep(0.28, 0.0, abs(mod(pang - ca + PI, 6.2831) - PI));
        acc += CG*0.10*cone*smoothstep(0.34, 0.0, length(rdp))*step(rdp.y, 0.0);
    }
    acc += CO*0.22*neon(sdBox(p - vec2(1.20, 0.63), vec2(0.24, 0.10)) - 0.008, 0.0018);
    acc += CO*0.6*glyphRow(p, vec2(1.05, 0.685), 4.0, 0.011, 0.0026, 51.0, 0.0);
    acc += CC*0.9*number(p - vec2(1.28, 0.685), mod(floor(gT*43.0), 100000.0), 5.0, 0.012, 0.0026);
    acc += CO*0.6*glyphRow(p, vec2(1.05, 0.58), 4.0, 0.011, 0.0026, 59.0, 0.0);
    acc += CC*0.9*number(p - vec2(1.28, 0.58), mod(floor(gT*67.0), 100000.0), 5.0, 0.012, 0.0026);
    for (int i = 0; i < 5; i++){
        float fi = float(i);
        float bl = 0.05 + 0.13*(0.5 + 0.5*sin(gT*1.3 + fi*0.9));
        acc += CY*0.6*fillIn(sdBox(p - vec2(1.02 + fi*0.045, 0.40 + bl*0.5), vec2(0.014, bl)) );
    }
    vec2 rc = vec2(1.30, 0.30); float rr2 = 0.11;
    acc += CG*0.7*neon(length(p - rc) - rr2, 0.0016);
    acc += CG*0.4*neon(length(p - rc) - rr2*0.5, 0.0013);
    {
        float sa = gT*1.6;
        vec2 dir = vec2(cos(sa), sin(sa));
        vec2 rp = p - rc;
        float along = dot(rp, dir);
        float perp = abs(dot(rp, vec2(-dir.y, dir.x)));
        if (along > 0.0 && length(rp) < rr2)
            acc += CG*0.5*(1.0 - smoothstep(0.0, 0.010, perp));
        float blip = h11(floor(gT*0.9));
        vec2 bp = rc + vec2(cos(blip*6.28)*rr2*0.6, sin(blip*9.0)*rr2*0.6);
        acc += CR*fillIn(length(p - bp) - 0.006)*step(0.5, fract(gT*2.0));
    }
    return acc;
}

// Normally-proportioned standing figure (~7.7 heads tall) from capsules. `hp` must
// already be folded to the right half — vec2(abs(x), y) relative to the figure
// centre. `bh` is total height in world units (figure spans ~±0.5*bh).
float bodySDF(vec2 hp, float bh, float br){
    float k = 0.014*bh;
    vec2 hq = hp - vec2(0.0, 0.428)*bh;
    float d = (length(hq/vec2(0.86, 1.0)) - 0.065*bh)*0.86;                                  // head
    d = smin(d, capsule(hp, vec2(0.0, 0.368)*bh, vec2(0.0, 0.315)*bh, 0.030*bh), k);         // neck
    d = smin(d, capsule(hp, vec2(0.0, 0.295)*bh, vec2(0.115, 0.283)*bh, 0.038*bh), k*2.0);   // shoulders
    d = smin(d, capsule(hp, vec2(0.0, 0.283)*bh, vec2(0.0, 0.165)*bh, 0.082*bh*br), k*2.0);  // chest
    d = smin(d, capsule(hp, vec2(0.0, 0.165)*bh, vec2(0.0, 0.065)*bh, 0.064*bh), k*2.0);     // waist
    // pelvis: a flat-sided rounded BOX, not a capsule — a capsule's round end cap
    // (plus a wide blend into the thighs) read as a bulging backside.
    d = smin(d, sdBox(hp - vec2(0.0, 0.036)*bh, vec2(0.048, 0.036)*bh) - 0.018*bh, k);       // pelvis
    d = smin(d, capsule(hp, vec2(0.115, 0.275)*bh, vec2(0.136, 0.100)*bh, 0.034*bh), k);     // upper arm
    d = smin(d, capsule(hp, vec2(0.136, 0.100)*bh, vec2(0.150, -0.055)*bh, 0.027*bh), k);    // forearm
    d = smin(d, capsule(hp, vec2(0.150, -0.055)*bh, vec2(0.156, -0.108)*bh, 0.024*bh), k);   // hand
    d = smin(d, capsule(hp, vec2(0.052, 0.010)*bh, vec2(0.047, -0.245)*bh, 0.044*bh), k);    // thigh
    d = smin(d, capsule(hp, vec2(0.047, -0.245)*bh, vec2(0.043, -0.462)*bh, 0.032*bh), k);   // shin
    d = smin(d, capsule(hp, vec2(0.043, -0.462)*bh, vec2(0.088, -0.490)*bh, 0.020*bh), k);   // foot
    return d;
}
// Coarse 4-primitive stand-in for the glitch ghost — calling the full bodySDF twice
// inlines it twice and quadruples compile time, and at burst speed nobody reads detail.
float bodyGhost(vec2 hp, float bh){
    float k = 0.03*bh;
    float d = length(hp - vec2(0.0, 0.428)*bh) - 0.065*bh;
    d = smin(d, capsule(hp, vec2(0.0, 0.300)*bh, vec2(0.0, 0.020)*bh, 0.078*bh), k*2.0);
    d = smin(d, capsule(hp, vec2(0.115, 0.280)*bh, vec2(0.150, -0.100)*bh, 0.030*bh), k);
    d = smin(d, capsule(hp, vec2(0.050, 0.010)*bh, vec2(0.043, -0.490)*bh, 0.040*bh), k);
    return d;
}

// ---- MODULE 2: human-body biometric analysis -------------------------------
vec3 bodyModule(vec2 p){
    if (p.y < 0.88 || p.y > 1.82) return vec3(0.0);
    vec3 acc = moduleFrame(p, 1.35, 71.0);
    vec2 bc = vec2(0.50, 1.35);
    float bh = 0.58;

    float sway = sin(gT*0.5)*0.025;
    vec2 q = rot2(p - bc, sway);
    float breathe = 1.0 + 0.025*sin(gT*1.6);

    // glitch: per-band horizontal slip, dropout bands, RGB ghost
    float burst = step(0.86, h11(floor(gT*2.3)));
    float bandY = floor(q.y*46.0 + gT*2.0);
    float goff = (h11(bandY*3.7 + floor(gT*7.0)) - 0.5)*bh*0.07*(0.16 + burst*1.7);
    float drop = step(0.04 + burst*0.12, h11(bandY*1.31 + floor(gT*5.0)));
    vec2 qg = vec2(q.x + goff, q.y);
    vec2 hp = vec2(abs(qg.x), qg.y);

    float sf = fract(gT*0.30);
    float sy = bc.y + (0.52 - 1.04*sf)*bh;
    float scanNear = exp(-abs(p.y - sy)*6.0);
    float sx = bc.x + (0.5 - fract(gT*0.17))*0.42;

    float bd = bodySDF(hp, bh, breathe);

    acc += CC*0.06*fillIn(bd)*drop;
    acc += CC*0.10*scanNear*fillIn(bd)*drop;
    acc += CC*0.09*fillIn(bd)*(0.5 + 0.5*sin(p.y*150.0 - gT*3.0))*drop;
    acc += mix(CC, CO, 0.25)*(0.9 + 0.7*scanNear)*neon(bd, 0.0022)*drop;
    if (burst > 0.5){
        float bdR = bodyGhost(vec2(abs(qg.x + goff*2.5 + 0.012*bh), qg.y), bh);
        acc += CR*0.40*neon(bdR, 0.0026);
        vec2 blk = floor(p*vec2(62.0, 94.0) + vec2(0.0, gT*3.0));
        float bn = step(0.90, h11(blk.x*2.1 + blk.y*5.3 + floor(gT*8.0)));
        acc += CC*0.35*bn*fillIn(bd);
    }
    acc += CG*0.55*exp(-abs(p.x - sx)*70.0)*(1.0 - smoothstep(0.0, 0.012, bd));

    acc += CO*0.5*drop*neon(sdSeg(hp, vec2(0.0, 0.30)*bh, vec2(0.0, 0.07)*bh), 0.0012);
    for (int i = 0; i < 3; i++){
        float ry = 0.25 - float(i)*0.050;
        float rib = abs(length((hp - vec2(0.0, ry)*bh)/vec2(1.0, 0.55)) - 0.075*bh);
        acc += CO*0.32*drop*neon(rib, 0.0012)*step(qg.y, 0.29*bh)*step(0.11*bh, qg.y);
    }
    float hb = pow(0.5 + 0.5*sin(gT*7.0), 5.0);
    acc += CR*(0.5 + 1.1*hb)*fillIn(length(qg - vec2(-0.028, 0.215)*bh) - 0.013);

    acc += CG*(0.45 + 0.55*sin(gT*3.0      ))*drop*neon(length(hp - vec2(0.115,  0.283)*bh) - 0.011, 0.0014);
    acc += CG*(0.45 + 0.55*sin(gT*3.0 + 1.2))*drop*neon(length(hp - vec2(0.136,  0.100)*bh) - 0.010, 0.0014);
    acc += CG*(0.45 + 0.55*sin(gT*3.0 + 2.4))*drop*neon(length(hp - vec2(0.150, -0.055)*bh) - 0.009, 0.0014);
    acc += CG*(0.45 + 0.55*sin(gT*3.0 + 3.6))*drop*neon(length(hp - vec2(0.052,  0.010)*bh) - 0.011, 0.0014);
    acc += CG*(0.45 + 0.55*sin(gT*3.0 + 4.8))*drop*neon(length(hp - vec2(0.047, -0.245)*bh) - 0.010, 0.0014);

    float inBody = 1.0 - smoothstep(0.0, 0.012, bd);
    acc += CG*inBody*exp(-abs(p.y - sy)*70.0)*0.9;
    acc += CG*inBody*glyphRow(p, vec2(bc.x, sy + 0.02), 6.0, 0.010, 0.0026, 300.0 + floor(gT*3.0), 0.0)*0.6;

    vec2 rp = bc + vec2(sin(gT*0.9)*0.16, sin(gT*0.7 + 1.0)*0.42)*bh;
    acc += CR*reticle(p, rp, 0.042, 0.0020);
    acc += CR*0.7*number(p - (rp + vec2(0.072, 0.03)), mod(floor(gT*57.0), 1000.0), 3.0, 0.010, 0.0024);

    vec2 f0 = bc + vec2(0.0, 0.428)*bh, f1 = bc + vec2(0.156, -0.108)*bh, f2 = bc + vec2(-0.047, -0.245)*bh;
    for (int i = 0; i < 2; i++){
        float sd = floor(gT*0.6 + float(i)*0.5);
        float pick = mod(sd + float(i), 3.0);
        vec2 fp = (pick < 0.5) ? f0 : (pick < 1.5) ? f1 : f2;
        float pl = 0.5 + 0.5*sin(gT*4.0 + float(i)*2.0);
        acc += CY*(0.4 + 0.5*pl)*sqBrackets(p, fp, vec2(0.042, 0.042), 0.020, 0.0020);
    }

    // particle systems: spine energy flow + thought cloud
    acc += particleField(p, bc + vec2(0.0, 0.18*bh), vec2(0.010, 0.14*bh), 10.0, 33.0, vec2(0.0, 0.14), CC, 0.0034);
    acc += particleField(p, bc + vec2(0.0, 0.47*bh), vec2(0.09*bh, 0.06*bh), 12.0, 77.0, vec2(0.03, 0.02), CY, 0.0030);

    // vector scope (rotating, breathing ellipse)
    {
        vec2 vsc = vec2(0.745, 1.56); float vr = 0.070;
        acc += CG*0.45*neon(length(p - vsc) - vr, 0.0013);
        acc += CG*0.25*neon(segH(p - vsc, vr), 0.0011);
        acc += CG*0.25*neon(segH((p - vsc).yx, vr), 0.0011);
        vec2 vq = rot2(p - vsc, gT*0.8);
        float ea = vr*0.82, eb = vr*(0.10 + 0.30*abs(sin(gT*1.3)));
        acc += CC*0.75*neon((length(vq/vec2(ea, eb)) - 1.0)*eb, 0.0016);
    }
    // spectral waterfall / heat cells
    {
        vec2 wc3 = vec2(0.745, 1.20), wb3 = vec2(0.082, 0.082);
        acc += CO*0.22*neon(sdBox(p - wc3, wb3) - 0.006, 0.0016);
        if (abs(p.x - wc3.x) < wb3.x - 0.005 && abs(p.y - wc3.y) < wb3.y - 0.005){
            vec2 cid = floor((p - wc3 + wb3)/vec2(0.017, 0.014));
            float v = h11(cid.x*3.1 + cid.y*7.7 + floor(gT*2.5));
            v = v*v;
            acc += mix(CR, CY, v)*0.55*v;
        }
        acc += CO*0.45*glyphRow(p, vec2(0.745, 1.098), 6.0, 0.0095, 0.0022, 555.0, 2.5);
    }

    // left column: ECG vitals + detector needle gauge
    vec2 vc = vec2(0.165, 1.42), vb = vec2(0.09, 0.05);
    acc += CO*0.25*neon(sdBox(p - vc, vb) - 0.006, 0.0016);
    if (abs(p.x - vc.x) < vb.x - 0.004 && abs(p.y - vc.y) < vb.y - 0.004){
        float xin = (p.x - (vc.x - vb.x))/(2.0*vb.x);
        float ph = fract(xin*2.0 - gT*0.6);
        float spike = exp(-pow((ph - 0.5)*22.0, 2.0)) - 0.35*exp(-pow((ph - 0.42)*30.0, 2.0)) + 0.55*exp(-pow((ph - 0.58)*30.0, 2.0));
        float yv = vc.y + spike*0.032;
        acc += CG*(neon(p.y - yv, 0.0022) + exp(-abs(p.y - yv)*40.0)*0.15);
    }
    acc += CO*0.5*glyphRow(p, vec2(0.125, 1.335), 3.0, 0.010, 0.0024, 90.0, 0.0);
    acc += CR*0.9*number(p - vec2(0.205, 1.335), mod(floor(gT*8.0), 1000.0), 3.0, 0.012, 0.0026);
    vec2 dc = vec2(0.165, 1.15); float dr = 0.06;
    acc += CG*0.5*neon(length(p - dc) - dr, 0.0013)*step(dc.y, p.y);
    for (int i = 0; i < 5; i++){
        float ta = PI*(0.1 + 0.8*float(i)/4.0);
        acc += CG*0.4*neon(sdSeg(p, dc + vec2(cos(ta), sin(ta))*dr*0.82, dc + vec2(cos(ta), sin(ta))*dr), 0.0012);
    }
    float na = PI*(0.5 + 0.45*sin(gT*2.3));
    acc += CR*0.9*neon(sdSeg(p, dc, dc + vec2(cos(na), sin(na))*dr*0.9), 0.0018);
    acc += CY*fillIn(length(p - dc) - 0.006);

    // DNA double-helix sequencer (O(1): strands direct, rungs quantized)
    {
        vec2 dh = vec2(0.93, 1.35);
        float hw = 0.05, hh = 0.27;
        if (abs(p.x - dh.x) < hw + 0.03 && abs(p.y - dh.y) < hh){
            float yy = p.y - dh.y;
            float ph = yy*24.0 + gT*1.6;
            float x1 = dh.x + sin(ph)*hw;
            float x2 = dh.x + sin(ph + PI)*hw;
            acc += CC*(0.30 + 0.55*max(cos(ph), 0.0))*neon(p.x - x1, 0.0032);
            acc += CY*(0.30 + 0.55*max(cos(ph + PI), 0.0))*neon(p.x - x2, 0.0032);
            float sh = 0.034;
            float ry = floor(yy/sh)*sh + sh*0.5;
            float rph = ry*24.0 + gT*1.6;
            vec2 ra = vec2(dh.x + sin(rph)*hw, dh.y + ry);
            vec2 rb = vec2(dh.x + sin(rph + PI)*hw, dh.y + ry);
            acc += CG*0.40*neon(sdSeg(p, ra, rb), 0.0014);
        }
        acc += CO*0.20*neon(sdBox(p - dh, vec2(hw + 0.03, hh)) - 0.006, 0.0016);
        acc += CO*0.45*glyphRow(p, vec2(dh.x, dh.y - hh - 0.022), 6.0, 0.0095, 0.0022, 620.0, 2.0);
    }

    // right column: streaming genome/analysis code
    acc += CO*0.55*glyphRow(p, vec2(1.30, 1.62), 7.0, 0.011, 0.0026, 361.0, 0.3);
    for (int i = 0; i < 7; i++){
        float yy = 1.55 - float(i)*0.058;
        acc += CO*0.5*glyphRow(p, vec2(1.30, yy), 9.0, 0.0088, 0.0021, 400.0 + float(i)*11.0, 1.4);
    }
    acc += CO*0.5*glyphRow(p, vec2(1.26, 1.15), 5.0, 0.010, 0.0024, 480.0, 0.0);
    acc += CC*1.0*number(p - vec2(1.44, 1.15), mod(floor(gT*13.0), 1000.0), 3.0, 0.014, 0.0028);

    float alB = step(0.5, fract(gT*2.5));
    vec2 wc = vec2(0.52, 1.02), wb2 = vec2(0.15, 0.026);
    acc += CR*(0.18 + 0.45*alB)*fillIn(sdBox(p - wc, wb2) - 0.008);
    acc += vec3(1.0, 0.85, 0.6)*(0.4 + 0.6*alB)*glyphRow(p, wc, 10.0, 0.011, 0.0026, 5.0, 0.0);
    return acc;
}

// ---- MODULE 3: data deck ----------------------------------------------------
vec3 dataModule(vec2 p){
    if (p.y < 1.78 || p.y > 2.72) return vec3(0.0);
    vec2 q = p - vec2(0.0, 1.80);
    vec3 acc = CO*0.30*neon(sdBox(q - vec2(0.80, 0.45), vec2(0.775, 0.435)) - 0.010, 0.0022);
    acc += titleBlock(q);
    acc += binaryBox(q);
    acc += leftColumn(q);
    acc += romanBox(q);
    acc += wavePanel(q, vec2(0.69, 0.25), vec2(0.09, 0.20), 0.0, 5.0);
    acc += wavePanel(q, vec2(0.91, 0.25), vec2(0.09, 0.20), 1.0, 11.0);
    acc += counterCol(q);
    acc += hexPanel(q);
    acc += ornaments(q);
    // sweeping highlight bar travelling up the deck
    acc += CO*0.055*exp(-abs(q.y - fract(gT*0.18)*0.9)*22.0);
    return acc;
}

// ---- MODULE 4: harmonics + scatter + radial --------------------------------
vec3 harmonicsModule(vec2 p){
    if (p.y < 2.68 || p.y > 3.62) return vec3(0.0);
    vec3 acc = moduleFrame(p, 3.15, 211.0);

    vec2 gc = vec2(0.60, 3.28), gb = vec2(0.50, 0.17);
    if (abs(p.x - gc.x) < gb.x && abs(p.y - gc.y) < gb.y){
        float xx = (p.x - gc.x);
        acc += CO*0.35*neon(p.y - gc.y, 0.0016);
        for (int j = 0; j < 6; j++){
            float fj = float(j);
            float env = 0.13*(1.0 - 0.10*fj);
            float y = gc.y + env*sin(xx*(9.0 + fj*2.5) + gT*(1.2 + fj*0.3) + fj*0.9)
                                  *(0.5 + 0.5*sin(xx*3.0 - gT));
            float dd = abs(p.y - y);
            vec3 cc = mix(CR, CC, fj/5.0);
            acc += cc*(neon(dd, 0.0022) + exp(-dd*40.0)*0.12);
        }
        float ph = gc.x - gb.x + fract(gT*0.25)*2.0*gb.x;
        acc += CY*0.5*neon(p.x - ph, 0.0014)*step(abs(p.y - gc.y), gb.y);
        // marker dot riding the lead curve at the playhead
        float mxx = ph - gc.x;
        float myy = gc.y + 0.13*sin(mxx*9.0 + gT*1.2)*(0.5 + 0.5*sin(mxx*3.0 - gT));
        acc += CY*fillIn(length(p - vec2(ph, myy)) - 0.007);
    }
    acc += CO*0.22*neon(sdBox(p - gc, vec2(gb.x + 0.02, gb.y + 0.02)) - 0.006, 0.0018);

    vec2 sc0 = vec2(0.34, 2.96);
    for (int i = 0; i < 16; i++){
        float fi = float(i);
        float t = gT*0.4 + fi;
        vec2 sp = sc0 + vec2((h11(fi*1.3) - 0.5)*0.44 + 0.02*sin(t),
                             (h11(fi*2.7 + 1.0) - 0.5)*0.16 + 0.01*cos(t*1.3));
        acc += CY*0.55*fillIn(length(p - sp) - 0.006);
        acc += CY*0.25*neon(length(p - sp) - 0.014, 0.0016);
    }
    acc += CO*0.4*neon(sdBox(p - sc0, vec2(0.26, 0.10)) - 0.008, 0.0018);
    acc += CO*0.5*glyphRow(p, vec2(0.34, 2.83), 8.0, 0.0095, 0.0022, 260.0, 0.0);

    vec2 mc = vec2(1.24, 2.98);
    for (int k = 0; k < 4; k++){
        float rr = 0.045 + float(k)*0.032;
        acc += CG*0.35*neon(length(p - mc) - rr, 0.0013);
    }
    {
        vec2 rp = p - mc;
        float ang = atan(rp.y, rp.x);
        float tri = cos(mod(ang*1.5 - gT*0.5, 2.0944) - 1.0472);
        acc += CO*0.5*neon((0.155 - length(rp)) + (1.0 - tri)*0.05, 0.0016)*step(length(rp), 0.165);
        for (int k = 0; k < 3; k++){
            float aa = gT*0.4 + float(k)*2.0944;
            acc += CR*0.7*nseg(p, mc, mc + vec2(cos(aa), sin(aa))*0.14, 0.0016);
        }
    }
    acc += CC*0.7*number(p - vec2(1.24, 2.80), mod(floor(gT*29.0), 1000000.0), 6.0, 0.0095, 0.0022);

    // particle fountain feeding the graph
    acc += particleField(p, gc, vec2(gb.x, gb.y), 16.0, 91.0, vec2(-0.06, 0.03), CC, 0.0030);
    // spectrum analyzer
    acc += spectrumBar(p, vec2(0.88, 2.88), vec2(0.16, 0.075), 13.0, 5.0, CG);
    // blinking alert triangle
    {
        vec2 ac = vec2(0.58, 2.86);
        float bl = step(0.5, fract(gT*2.0));
        float td = min(min(sdSeg(p, ac + vec2(0.0, 0.05), ac + vec2(0.05, -0.04)),
                           sdSeg(p, ac + vec2(0.05, -0.04), ac + vec2(-0.05, -0.04))),
                           sdSeg(p, ac + vec2(-0.05, -0.04), ac + vec2(0.0, 0.05)));
        acc += CR*(0.4 + 0.6*bl)*neon(td, 0.0020);
        acc += CR*(0.4 + 0.6*bl)*fillIn(sdBox(p - ac - vec2(0.0, 0.006), vec2(0.004, 0.017)));
        acc += CR*(0.4 + 0.6*bl)*fillIn(length(p - ac - vec2(0.0, -0.024)) - 0.005);
    }
    return acc;
}

vec3 scene(vec2 p){
    vec3 c = bgWorld(p);
    c += busLayer(p);
    c += globeModule(p);
    c += bodyModule(p);
    c += dataModule(p);
    c += harmonicsModule(p);
    return c;
}

// ---- fixed screen-space HUD frame ------------------------------------------
float onl(float d, float w){
    float dd = abs(d) - w;
    return (1.0 - smoothstep(-gSp, gSp, dd)) + exp(-max(dd, 0.0)*80.0)*uGlow*0.5;
}
vec3 overlay(vec2 v, float aspect, float scrollN){
    vec3 acc = vec3(0.0);
    float pxAA = gPx;
    gPx = gSp;
    acc += CO*0.7*onl(v.y - 0.965, 0.0016);
    acc += CO*0.7*onl(v.y - 0.035, 0.0016);
    acc += CO*0.9*sqBrackets(v, vec2(aspect*0.5, 0.5), vec2(aspect*0.5 - 0.012, 0.5 - 0.012),
                             0.045 + 0.014*sin(gT*1.5), 0.0022);
    acc += CO*0.9*glyphRow(v, vec2(0.16, 0.984), 8.0, 0.012, 0.0028, 700.0, 0.0);
    float rec = step(0.5, fract(gT*1.3));
    acc += CR*rec*fillIn(length(v - vec2(0.02, 0.984)) - 0.008);
    acc += CO*0.9*number(v - vec2(aspect - 0.16, 0.984), mod(floor(gT*10.0), 1000000.0), 6.0, 0.012, 0.0026);
    acc += CO*0.6*glyphRow(v, vec2(aspect*0.5, 0.016), 26.0, 0.010, 0.0022, 800.0 + floor(gT*4.0), 4.0);
    acc += CO*0.8*glyphRow(v, vec2(0.14, 0.016), 6.0, 0.011, 0.0026, 640.0, 0.0);
    float gx = aspect - 0.022;
    acc += CO*0.5*onl(v.x - gx, 0.0012)*step(abs(v.y - 0.5), 0.44);
    float fillTop = 0.06 + scrollN*0.88;
    if (v.y > 0.06 && v.y < fillTop && abs(v.x - gx) < 0.010)
        acc += CY*0.7;
    acc += CY*0.9*fillIn(sdBox(v - vec2(gx, fillTop), vec2(0.012, 0.004)));
    // status LEDs
    for (int i = 0; i < 3; i++){
        float b = step(0.5, fract(gT*(1.5 + float(i)*0.4) + float(i)));
        vec3 lc = (i == 0) ? CR : (i == 1) ? CY : CG;
        acc += lc*(0.3 + 0.7*b)*fillIn(length(v - vec2(0.31 + float(i)*0.024, 0.984)) - 0.006);
    }
    // travelling diagonal scan sweep
    float sw = mod(gT*0.55, aspect + 1.2) - 0.6;
    float sd2 = abs((v.x + v.y*0.5) - sw);
    acc += CO*0.05*exp(-sd2*11.0);
    gPx = pxAA;
    return acc;
}

// ---- main ------------------------------------------------------------------
void main(){
    vec2 R = RENDERSIZE.xy;
    vec2 fragCoord = gl_FragCoord.xy;
    gT = TIME*uSpeed;

    float al = clamp(uAlert, 0.0, 1.0);
    CO = mix(vec3(1.00, 0.32, 0.05), vec3(1.00, 0.12, 0.05), al*0.60);
    CR = vec3(1.00, 0.12, 0.07);
    CG = mix(vec3(0.30, 1.00, 0.45), vec3(1.00, 0.50, 0.10), al*0.55);
    CC = mix(vec3(0.50, 0.95, 1.00), vec3(1.00, 0.40, 0.30), al*0.50);
    CY = mix(vec3(1.00, 0.85, 0.20), vec3(1.00, 0.35, 0.10), al*0.45);
    CP = mix(vec3(0.75, 0.40, 1.00), vec3(1.00, 0.25, 0.35), al*0.50);

    vec2 uv = fragCoord/R;
    vec2 nn = uv*2.0 - 1.0;
    nn *= 1.0 + uCurve*0.07*dot(nn, nn);
    vec2 uv2 = nn*0.5 + 0.5;

    float aspect = R.x/R.y;
    float viewH = 1.6/aspect;
    gPx = 1.2*viewH/R.y;
    gSp = 1.4/R.y;

    float scroll = gT*uScroll;
    float scrollN = fract(scroll/WORLD_H);

    float tear = step(0.96, h11(floor(gT*3.0)));
    float rowr = h21(vec2(floor(uv2.y*300.0), floor(gT*30.0)));
    float xoff = uFlicker*tear*(rowr - 0.5)*0.02;

    float x = uv2.x*1.6 + xoff;
    float wy = mod(uv2.y*viewH + scroll, WORLD_H);
    vec3 col = scene(vec2(x, wy));

    col += overlay(vec2(uv2.x*aspect, uv2.y), aspect, scrollN);

    col = mix(col, col*vec3(1.25, 0.45, 0.35), al*0.35);
    col += CR*0.05*al*(0.5 + 0.5*sin(gT*6.0));

    float scan = 0.75 + 0.25*sin(fragCoord.y*3.14159);
    col *= mix(1.0, scan, uScan*0.6);
    float m3 = mod(floor(fragCoord.x), 3.0);
    vec3 mask = (m3 < 0.5) ? vec3(1.18, 0.91, 0.91)
              : (m3 < 1.5) ? vec3(0.91, 1.18, 0.91) : vec3(0.91, 0.91, 1.18);
    col *= mix(vec3(1.0), mask, uScan*0.5);

    col *= 1.0 - uFlicker*(0.06*h11(floor(gT*24.0)) + 0.02*(0.5 + 0.5*sin(gT*117.0 + uv2.y*3.0)));
    col += (h21(fragCoord + fract(gT)*vec2(17.0, 3.0)) - 0.5)*0.035*uFlicker;

    float edge = smoothstep(0.0, 0.006, uv2.x)*smoothstep(1.0, 0.994, uv2.x)
               * smoothstep(0.0, 0.006, uv2.y)*smoothstep(1.0, 0.994, uv2.y);
    col *= edge;
    col *= clamp(1.0 - uVignette*0.6*dot(nn*0.85, nn*0.85), 0.0, 1.0);

    col = 1.0 - exp(-col*1.6);
    col = pow(max(col, vec3(0.0)), vec3(0.9));
    gl_FragColor = vec4(col, 1.0);
}
