/*{
  "DESCRIPTION": "Cymatics: a Chladni vibrating plate. Fine sand collects along the nodal lines of a square plate's standing waves and migrates as the driving mode sweeps a closed loop through frequency space, drawing an endless succession of symmetric figures. Pale gold grains on dark steel.",
  "CREDIT": "glslop agent (Claude)",
  "CATEGORIES": ["cymatics", "chladni", "physics", "standing-wave", "generative"],
  "INPUTS": [
    { "NAME": "speed",    "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "n0",       "TYPE": "float", "DEFAULT": 3.0, "MIN": 2.0, "MAX": 9.0 },
    { "NAME": "m0",       "TYPE": "float", "DEFAULT": 4.5, "MIN": 2.0, "MAX": 12.0 },
    { "NAME": "sweep",    "TYPE": "float", "DEFAULT": 1.6, "MIN": 0.0, "MAX": 4.0 },
    { "NAME": "grainAmt", "TYPE": "float", "DEFAULT": 0.7, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "zoom",     "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.6, "MAX": 1.6 }
  ]
}*/

#define PI  3.14159265359
#define TAU 6.28318530718
const float PERIOD = 16.0;

float hash(vec2 p){ return fract(sin(dot(p, vec2(41.31, 289.17))) * 43758.5453); }

float chladni(vec2 p, float n, float m){
  return cos(n*PI*p.x)*cos(m*PI*p.y) - cos(m*PI*p.x)*cos(n*PI*p.y);
}

void main(){
  vec2 p = (2.0*gl_FragCoord.xy - RENDERSIZE.xy) / RENDERSIZE.y;
  // one square plate, centred, with a dark border around it
  vec2 s = p * 1.28 / zoom;

  float ph = fract(TIME * speed / PERIOD);
  float th = TAU * ph;

  // closed loop through mode space -> continuous morph + seamless return
  float n = n0 + sweep * cos(th);
  float m = m0 + sweep * sin(th);

  float f = chladni(s, n, m);
  float d = abs(f);

  // sand piles where the plate amplitude is ~0 (the nodal lines)
  float w = 0.04;
  float sand = w / (d + w);
  sand = pow(sand, 2.3);

  // static plate grain showing through the moving sand mask -> reads as grains
  float g = hash(floor(s * 360.0));
  sand *= mix(1.0, g * 1.7, grainAmt);
  sand *= 0.55 + 0.45 * hash(floor(s * 820.0) + 7.0);

  // square plate boundary
  vec2 a = abs(s);
  float sq = max(a.x, a.y);
  float plate = smoothstep(1.0, 0.965, sq);          // inside the plate
  float bevel = smoothstep(0.92, 1.0, sq) * plate;     // bright rim

  // dark brushed steel surface with a soft top-left sheen
  vec3 steel = vec3(0.045, 0.05, 0.062);
  steel += vec3(0.05, 0.06, 0.08) * (0.5 - 0.5*s.y) * (0.5 - 0.5*s.x);
  steel = mix(steel, vec3(0.10, 0.11, 0.13), bevel);   // metal rim catches light

  vec3 sandCol = vec3(1.0, 0.85, 0.52);
  vec3 col = steel * plate;
  col += sandCol * sand * 1.9 * plate;
  // a hot white crest along only the very thinnest lines
  col += vec3(1.0, 0.97, 0.9) * pow(w * 0.45 / (d + w * 0.45), 6.0) * 0.6 * plate;

  // background outside the plate: near-black with a faint drop shadow
  float shadow = smoothstep(1.18, 1.0, sq) * (1.0 - plate);
  col += vec3(0.012, 0.013, 0.017) * shadow;

  col = col / (1.0 + col);
  col = pow(col, vec3(0.84));
  gl_FragColor = vec4(col, 1.0);
}
