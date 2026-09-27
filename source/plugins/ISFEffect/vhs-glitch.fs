/*{
  "DESCRIPTION": "ISF port of the FreeFrame VHSGlitch effect, for ISF Effect (Resolume 7.x FFGL). Four stacking tape artefacts computed per pixel: horizontal scan-line jitter, RGB chroma separation, static noise bands, and tracking-error block displacement. Defaults are deliberately subtle (barely-there drift/fringing, tracking off) — dial uChaos up for the original's full intensity and well beyond ('geht bis 11', matching the voxel-flight shaders' chaos knob).",
  "CREDIT": "CC0",
  "CATEGORIES": ["color-effect", "glitch"],
  "INPUTS": [
    { "NAME": "inputImage", "TYPE": "image" },
    { "NAME": "uShift",    "LABEL": "Zeilen-Jitter",       "TYPE": "float", "DEFAULT": 0.06,  "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uChroma",   "LABEL": "Farbkanal-Versatz",   "TYPE": "float", "DEFAULT": 0.05,  "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uNoise",    "LABEL": "Rausch-Baender",      "TYPE": "float", "DEFAULT": 0.04,  "MIN": 0.0, "MAX": 1.5 },
    { "NAME": "uTracking", "LABEL": "Tracking-Fehler",     "TYPE": "float", "DEFAULT": 0.00,  "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uSpeed",    "LABEL": "Glitch-Tempo",        "TYPE": "float", "DEFAULT": 1.00,  "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "uChaos",    "LABEL": "Chaos (geht bis 11)", "TYPE": "float", "DEFAULT": 0.00,  "MIN": 0.0, "MAX": 11.0 }
  ]
}*/

float hash11( float n ){ return fract( sin( n )*43758.5453 ); }
float hash21( vec2 p ){ return fract( sin( dot( p, vec2( 127.1, 311.7 ) ) )*43758.5453 ); }

void main()
{
    vec2 uv  = isf_FragNormCoord;
    vec2 res = RENDERSIZE;
    float y  = uv.y*res.y;

    float chaos = uChaos/11.0;
    float shiftAmt    = uShift*( 1.0 + 2.0*chaos );
    float chromaAmt   = uChroma*( 1.0 + 2.0*chaos );
    float noiseAmt    = uNoise*( 1.0 + 2.0*chaos );
    float trackingAmt = uTracking*( 1.0 + 2.0*chaos );

    // Glitch pattern advances on its own clock (uSpeed), independent of actual render fps.
    float fSeed = floor( TIME*max( uSpeed, 0.0001 )*30.0 );

    // 1. Row jitter: most rows drift a little, 1/16 snap to a large offset ("tape snap").
    float rh  = hash21( vec2( fSeed, y + 0.5 ) );
    float rh2 = hash21( vec2( fSeed*3.1, y + 7.7 ) );
    float jitter = rh*2.0 - 1.0;
    if( rh2 > 1.0/16.0 ) jitter *= 0.12;
    float rowShift = jitter*( shiftAmt*60.0 );

    // 2. Tracking-error blocks: a few bands displaced sideways by a large amount.
    float numBlocksF = trackingAmt*4.0;
    int numBlocks = int( floor( numBlocksF + 0.5 ) );
    if( numBlocks > 8 ) numBlocks = 8;
    for( int b = 0; b < 8; ++b )
    {
        if( b >= numBlocks ) break;
        float r0 = hash21( vec2( fSeed, float( b )*4.0 + 0.0 ) );
        float r1 = hash21( vec2( fSeed, float( b )*4.0 + 1.0 ) );
        float r2 = hash21( vec2( fSeed, float( b )*4.0 + 2.0 ) );
        float r3 = hash21( vec2( fSeed, float( b )*4.0 + 3.0 ) );
        float blockH = 8.0 + r0*60.0;
        float y0 = r1*res.y;
        float y1 = min( y0 + blockH, res.y - 1.0 );
        float dxMag = 20.0 + r2*( res.x*0.5 );
        float dx = ( r3 > 0.5 ) ? dxMag : -dxMag;
        if( y >= y0 && y <= y1 ) rowShift += dx;
    }

    // 3. Static noise: a fraction of rows blend toward random per-pixel colour.
    float noiseProb = clamp( noiseAmt*0.45, 0.0, 0.95 );
    float rn    = hash21( vec2( fSeed + 1.0, y ) );
    bool  noisy = rn < noiseProb;
    float pixSeed = hash21( vec2( fSeed + 2.0, y ) );

    // 4. Sample with per-channel horizontal offset (chroma separation), wrapping at the edges.
    float chromaPx = chromaAmt*20.0;
    float xR = mod( uv.x*res.x + rowShift - chromaPx, res.x );
    float xG = mod( uv.x*res.x + rowShift,             res.x );
    float xB = mod( uv.x*res.x + rowShift + chromaPx,  res.x );

    vec4 cR = texture( inputImage, vec2( xR/res.x, uv.y ) );
    vec4 cG = texture( inputImage, vec2( xG/res.x, uv.y ) );
    vec4 cB = texture( inputImage, vec2( xB/res.x, uv.y ) );
    if( cR.a > 0.0 ) cR.rgb /= cR.a;
    if( cG.a > 0.0 ) cG.rgb /= cG.a;
    if( cB.a > 0.0 ) cB.rgb /= cB.a;
    float alpha = texture( inputImage, uv ).a;

    vec3 outRGB = vec3( cR.r, cG.g, cB.b );

    if( noisy )
    {
        float px = fract( pixSeed*97.0 + uv.x*res.x*0.6180339887 );
        vec3 n = vec3( hash11( px*13.1 ), hash11( px*29.7 + 1.0 ), hash11( px*51.3 + 2.0 ) );
        outRGB = mix( outRGB, n, clamp( noiseAmt, 0.0, 1.0 ) );
    }

    gl_FragColor = vec4( outRGB*alpha, alpha );
}
