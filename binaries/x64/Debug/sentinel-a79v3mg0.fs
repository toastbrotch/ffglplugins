/*{
  "DESCRIPTION": "Signal grid — an isometric panel field swept by radar-like interference, lighting cold cells and the occasional hot alert.",
  "CREDIT": "claude-opus-4-8",
  "INPUTS": [
    { "NAME": "scale",     "TYPE": "float", "DEFAULT": 6.0, "MIN": 3.0, "MAX": 18.0 },
    { "NAME": "scanSpeed", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "sharp",     "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.1, "MAX": 0.9 },
    { "NAME": "alert",     "TYPE": "float", "DEFAULT": 0.6, "MIN": 0.0, "MAX": 1.5 }
  ]
}*/

mat2 rot(float a){ float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }
float hash(vec2 p){ p = fract(p * vec2(127.1, 311.7)); p += dot(p, p + 34.5); return fract(p.x * p.y); }

void main(){
  vec2 uv = (gl_FragCoord.xy - 0.5 * RENDERSIZE) / RENDERSIZE.y;

  // 45deg rotation turns square cells into isometric diamonds
  mat2 R = rot(0.785398);
  vec2 p = (R * uv) * scale;
  vec2 id = floor(p);
  vec2 gv = fract(p) - 0.5;

  // cell centre back in screen space (for interference sampling)
  vec2 cc = rot(-0.785398) * ((id + 0.5) / scale);

  // SONAR: a dominant central emitter so lit cells form expanding concentric rings
  float t = TIME;
  float rc = length(cc);
  float field = sin(rc * 8.0 - t * 2.5 * scanSpeed) * 1.8;              // central rings (dominant)
  for (int s = 0; s < 2; s++){
    float fs = float(s);
    vec2 src = 0.9 * vec2(sin(t * 0.4 + fs * 2.3), cos(t * 0.33 + fs * 1.9));
    field += 0.5 * sin(length(cc - src) * 9.0 - t * 2.0 * scanSpeed + fs * 1.7);
  }
  field /= 2.8;

  float rnd = hash(id);
  float lit = smoothstep(sharp, 1.0 - sharp * 0.4, 0.5 + 0.5 * field);
  lit = pow(lit, 1.4);
  lit *= 0.82 + 0.32 * sin(t * 3.0 + rnd * 30.0);                       // flicker
  lit *= 0.55 + 0.7 * exp(-rc * rc * 0.35);                            // energy concentrates toward core

  float edge = max(abs(gv.x), abs(gv.y));
  float panel = smoothstep(0.49, 0.40, edge);
  float rim   = smoothstep(0.40, 0.49, edge) * smoothstep(0.5, 0.49, edge);
  float bevel = clamp(0.75 + 0.6 * (-gv.x + gv.y), 0.4, 1.4);           // per-cell directional light

  vec3 base = vec3(0.0, 0.012, 0.02);
  vec3 cyan = vec3(0.0, 0.9, 0.85);
  vec3 hot  = vec3(1.0, 0.2, 0.3);

  // central reactor glow gives the grid a focal core
  float core = exp(-dot(uv, uv) * 5.0);
  vec3 col = base + core * vec3(0.0, 0.16, 0.17);

  col += panel * cyan * (0.05 + 0.8 * lit * lit) * bevel;               // saturated cyan cells
  col += rim * mix(cyan, vec3(0.8, 1.0, 1.0), lit * lit) * (0.15 + 0.9 * lit);  // white-hot bevel

  // RED threat-blips: cells riding the wavefront flip to a hard red warning
  float canAlert = step(0.55, rnd);
  float alertPulse = 0.5 + 0.5 * sin(t * 3.0 + rnd * 50.0);
  float aCell = canAlert * smoothstep(0.35, 0.7, lit) * alertPulse;
  col = mix(col, hot * (0.6 + 0.8 * lit) * bevel, panel * aCell * alert);   // red replaces cyan
  col += aCell * aCell * 0.8 * hot * alert;                                 // bloom

  col *= 0.92 + 0.08 * sin(gl_FragCoord.y * 1.6);                       // faint scanlines
  col *= 1.0 - 0.62 * dot(uv, uv);                                      // strong vignette (menace)
  col = col / (1.0 + 0.45 * col);
  col = pow(max(col, 0.0), vec3(0.84));
  gl_FragColor = vec4(col, 1.0);
}
