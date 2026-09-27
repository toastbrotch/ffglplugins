/*{
  "DESCRIPTION": "Needle field — a grid of tiny compass needles swinging into alignment with a drifting interference flow, like iron filings breathing.",
  "CREDIT": "claude-opus-4-8",
  "INPUTS": [
    { "NAME": "scale", "TYPE": "float", "DEFAULT": 16.0, "MIN": 6.0, "MAX": 34.0 },
    { "NAME": "flow",  "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 2.5 },
    { "NAME": "len",   "TYPE": "float", "DEFAULT": 0.3, "MIN": 0.1, "MAX": 0.45 },
    { "NAME": "tint",  "TYPE": "float", "DEFAULT": 0.5, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

mat2 rot(float a){ float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }

// flow field from drifting in-view vortices + sources -> needles swirl like iron filings
vec2 fieldVec(vec2 q, float t){
  vec2 v = 0.3 * vec2(cos(q.y * 1.1 + t * 0.4), sin(q.x * 1.0 - t * 0.3));  // gentle base flow
  for (int i = 0; i < 3; i++){
    float fi = float(i);
    vec2 c = 0.85 * vec2(sin(t * 0.3 + fi * 2.1), cos(t * 0.25 + fi * 1.7));
    vec2 d = q - c;
    float r2 = dot(d, d) + 0.05;
    float sgn = (mod(fi, 2.0) < 0.5) ? 1.0 : -1.0;
    v += sgn * vec2(-d.y, d.x) / r2;            // vortex circulation
    v += 0.25 * (fi - 1.0) * d / r2;            // source/sink
  }
  return v;
}

void main(){
  vec2 uv = (gl_FragCoord.xy - 0.5 * RENDERSIZE) / RENDERSIZE.y;
  vec2 p = uv * scale;
  vec2 id = floor(p);
  vec2 gv = fract(p) - 0.5;

  vec2 cc = (id + 0.5) / scale;               // cell centre in screen space
  vec2 fv = fieldVec(cc * 2.5, TIME) * flow;
  float ang = atan(fv.y, fv.x);
  float mag = clamp(length(fv) * 0.5, 0.25, 1.0);

  // rotated capsule (the needle) aligned to the field
  vec2 g = rot(-ang) * gv;
  float L = len * mag;
  float d = length(vec2(max(abs(g.x) - L, 0.0), g.y));
  float needle = smoothstep(0.07, 0.03, d);
  float glow = smoothstep(0.16, 0.0, d);

  vec3 deep = vec3(0.0, 0.02, 0.05);
  vec3 cyan = mix(vec3(0.2, 0.8, 1.0), vec3(0.5, 0.5, 1.0), tint);

  vec3 col = deep;
  col += cyan * needle * (0.4 + 0.8 * mag);
  col += cyan * glow * 0.18 * mag;            // soft halo
  col += vec3(0.8, 0.95, 1.0) * smoothstep(0.04, 0.0, d) * mag;   // bright tip core

  col *= 1.0 - 0.3 * dot(uv, uv);
  col = pow(max(col, 0.0), vec3(0.9));
  gl_FragColor = vec4(col, 1.0);
}
