/*{
  "DESCRIPTION": "ISF port of the FreeFrame VHSClog effect, for ISF Effect (Resolume 7.x FFGL) — simulates a partially clogged VHS read head: horizontal signal-loss bands where the picture smears/holds instead of updating. The original's per-pixel left-to-right hold/fresh recurrence is reworked as an independent per-pixel hash plus a backward search for the nearest 'fresh' column (same visual streaking, GPU-parallel instead of sequential). Defaults are subtle (one small band, low smear); dial uChaos up for the original's full intensity and well beyond.",
  "CREDIT": "CC0",
  "CATEGORIES": ["color-effect", "glitch"],
  "INPUTS": [
    { "NAME": "inputImage", "TYPE": "image" },
    { "NAME": "uClog",      "LABEL": "Ausfall-Baender",     "TYPE": "float", "DEFAULT": 0.10, "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uThickness", "LABEL": "Streifen-Dicke",      "TYPE": "float", "DEFAULT": 0.15, "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uSmear",     "LABEL": "Schmier-Menge",       "TYPE": "float", "DEFAULT": 0.10, "MIN": 0.0, "MAX": 1.3 },
    { "NAME": "uFlutter",   "LABEL": "Flatter-Tempo",       "TYPE": "float", "DEFAULT": 0.05, "MIN": 0.0, "MAX": 2.0 },
    { "NAME": "uChaos",     "LABEL": "Chaos (geht bis 11)", "TYPE": "float", "DEFAULT": 0.00, "MIN": 0.0, "MAX": 11.0 }
  ]
}*/

float hash21( vec2 p ){ return fract( sin( dot( p, vec2( 127.1, 311.7 ) ) )*43758.5453 ); }

void main()
{
    vec2 uv  = isf_FragNormCoord;
    vec2 res = RENDERSIZE;
    float y  = uv.y*res.y;

    float chaos = uChaos/11.0;
    float clogAmt    = uClog*( 1.0 + 2.0*chaos );
    float threshAmt  = uSmear*( 1.0 + 1.0*chaos );
    float thickAmt   = uThickness*( 1.0 + 2.0*chaos );
    float flutterAmt = uFlutter*( 1.0 + 2.0*chaos );

    int numBands = int( floor( clogAmt*5.0 + 0.5 ) );
    if( numBands > 10 ) numBands = 10;

    // Flutter: how often band positions reshuffle, on TIME's own clock rather than a frame count
    // (matches the intent - "static" to "chaotic every frame" - without depending on host fps).
    float period = 8.0/( 1.0 + flutterAmt*flutterAmt*200.0 );
    float slowFrame = floor( TIME/max( period, 0.001 ) );

    bool  matched     = false;
    float matchedSeed = 0.0;
    for( int b = 0; b < 10; ++b )
    {
        if( b >= numBands ) break;
        float h0 = hash21( vec2( slowFrame, float( b )*4.0 + 0.0 ) );
        float h1 = hash21( vec2( slowFrame, float( b )*4.0 + 1.0 ) );
        float h3 = hash21( vec2( slowFrame, float( b )*4.0 + 9.0 ) );
        float bandH = mix( 5.0, 48.0, h0 );
        float y0 = h1*res.y;
        float y1 = min( y0 + bandH, res.y - 1.0 );
        if( y >= y0 && y <= y1 )
        {
            matched     = true;
            matchedSeed = h3;
            break;
        }
    }

    if( !matched )
    {
        gl_FragColor = texture( inputImage, uv );
        return;
    }

    // Rows sharing a "slab" (Thickness) use the same hold/fresh pattern, so smear streaks are
    // vertically fat instead of a different random pattern on every single row.
    float thickPx = max( 1.0, thickAmt*16.0 );
    float slabY   = floor( y/thickPx )*thickPx;
    float smearProb = clamp( threshAmt*2.0, 0.0, 0.99 );

    // Independent per-pixel hold/fresh decision (unlike the original's sequential LCG walk, this
    // parallelises across pixels): search backward for the nearest column at/before this one that
    // reads "fresh" and sample there instead - same run-length streaking, GPU-friendly.
    int xi = int( floor( uv.x*res.x ) );
    int maxIter = int( min( res.x, 4096.0 ) );
    int found = 0;
    for( int i = 0; i < 4096; ++i )
    {
        if( i >= maxIter ) { found = 0; break; }
        int cx = xi - i;
        if( cx <= 0 ) { found = 0; break; }
        float h = hash21( vec2( matchedSeed*131.0 + slabY*0.7, float( cx ) ) );
        if( h > smearProb ) { found = cx; break; }
    }

    vec2 srcUV = vec2( ( float( found ) + 0.5 )/res.x, uv.y );
    gl_FragColor = texture( inputImage, srcUV );
}
