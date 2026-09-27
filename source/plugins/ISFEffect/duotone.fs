/*{
  "DESCRIPTION": "Duotone colour-grade filter for ISF Effect (Resolume 7.x FFGL) — remaps the incoming clip's luminance between two adjustable colours. A minimal example of an ISF filter (as opposed to generator) shader: declares an \"image\" INPUT named inputImage, which ISF Effect binds to the layer's own video.",
  "CREDIT": "CC0",
  "CATEGORIES": ["color-effect"],
  "INPUTS": [
    { "NAME": "inputImage", "TYPE": "image" },
    { "NAME": "uShadowR",    "LABEL": "Shadow Rot",       "TYPE": "float", "DEFAULT": 0.05, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uShadowG",    "LABEL": "Shadow Gruen",     "TYPE": "float", "DEFAULT": 0.05, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uShadowB",    "LABEL": "Shadow Blau",      "TYPE": "float", "DEFAULT": 0.20, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uHighR",      "LABEL": "Licht Rot",        "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uHighG",      "LABEL": "Licht Gruen",      "TYPE": "float", "DEFAULT": 0.85, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uHighB",      "LABEL": "Licht Blau",       "TYPE": "float", "DEFAULT": 0.40, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uMix",        "LABEL": "Effekt-Staerke",   "TYPE": "float", "DEFAULT": 1.00, "MIN": 0.0, "MAX": 1.0 }
  ]
}*/

// Simple ISF FILTER example — plain GLSL, no ISF-ES1.00-style conventions needed here since this
// loader always wraps shaders in core-profile GLSL regardless.
void main()
{
    vec4 c = texture( inputImage, isf_FragNormCoord );
    if( c.a > 0.0 )
        c.rgb /= c.a;// ISF Effect passes inputImage premultiplied, same as any FFGL input texture

    float luma = dot( c.rgb, vec3( 0.299, 0.587, 0.114 ) );
    vec3 shadow    = vec3( uShadowR, uShadowG, uShadowB );
    vec3 highlight = vec3( uHighR, uHighG, uHighB );
    vec3 duo = mix( shadow, highlight, luma );

    vec3 outRGB = mix( c.rgb, duo, uMix );
    gl_FragColor = vec4( outRGB * c.a, c.a );// re-premultiply for correct compositing
}
