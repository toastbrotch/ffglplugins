/*{
  "DESCRIPTION": "Single big centered random alphanumeric ID string, white on black — the ISFBrowser plugin sorts *.fs files alphabetically and loads the first one by default (see ISFBrowser.cpp scanFiles/std::sort), so this file is deliberately named '0-...' to always be that first/default shader. The string itself is fully static by default (seeded once from uSeed); five independent transform knobs (character re-roll, split-flap flip, vertical bounce, dissolve, row-tear glitch) are all off at 0 and layer on top of the static base when raised.",
  "CREDIT": "CC0",
  "CATEGORIES": ["generator", "test-card", "text"],
  "INPUTS": [
    { "NAME": "uLength",     "LABEL": "String-Laenge",     "TYPE": "float", "DEFAULT": 5.0,  "MIN": 3.0, "MAX": 16.0 },
    { "NAME": "uScale",      "LABEL": "Groesse",           "TYPE": "float", "DEFAULT": 1.0,  "MIN": 0.3, "MAX": 2.0 },
    { "NAME": "uCharChange", "LABEL": "Zeichen-Wechsel",   "TYPE": "float", "DEFAULT": 0.0,  "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uFlip",       "LABEL": "Flip (Split-Flap)", "TYPE": "float", "DEFAULT": 0.0,  "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uVertical",   "LABEL": "Vertikal-Bewegung", "TYPE": "float", "DEFAULT": 0.0,  "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uDissolve",   "LABEL": "Dissolve/Aufloesen","TYPE": "float", "DEFAULT": 0.0,  "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uGlitch",     "LABEL": "Zeilen-Glitch",     "TYPE": "float", "DEFAULT": 0.0,  "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "uSeed",       "LABEL": "Seed",              "TYPE": "float", "DEFAULT": 42.0, "MIN": 0.0, "MAX": 1000.0 }
  ]
}*/

// ID DISPLAY — single big centered random alphanumeric string, ISF generator
// (GLSL ES 1.00: literal loop-free, gl_FragColor, TIME/RENDERSIZE).
// Relies on `uniform float uInstanceSeed`, which ISFBrowser.cpp declares and
// sets for every loaded shader (like TIME/RENDERSIZE) to one fixed random
// value per plugin instance — without it, two copies of this file loaded at
// once would show the identical string at the same uSeed.
// Font: procedural 5x7 dot-matrix (0-9, A-Z), each glyph packed into two
// exact-integer floats (20 + 15 bits) computed from a hand-authored bitmap
// table by font5x7.py (next to this file) — regenerate glyphHi/glyphLo from
// there if the font ever needs editing, rather than hand-editing the numbers.

float hash11(float n){ return fract(sin(n)*43758.5453); }
float hash21(vec2 p){ return fract(sin(dot(p, vec2(127.1, 311.7)))*43758.5453); }

// rows 0-3 of the 5x7 glyph, packed as bits (row*5+col), order "0-9A-Z"
float glyphHi(float ch){
    if      (ch < 1.0)  return 708142.0;  // '0'
    else if (ch < 2.0)  return 135556.0;  // '1'
    else if (ch < 3.0)  return 67118.0;   // '2'
    else if (ch < 4.0)  return 69727.0;   // '3'
    else if (ch < 5.0)  return 600258.0;  // '4'
    else if (ch < 6.0)  return 64031.0;   // '5'
    else if (ch < 7.0)  return 999686.0;  // '6'
    else if (ch < 8.0)  return 133183.0;  // '7'
    else if (ch < 9.0)  return 476718.0;  // '8'
    else if (ch < 10.0) return 509486.0;  // '9'
    else if (ch < 11.0) return 1033774.0; // 'A'
    else if (ch < 12.0) return 1001022.0; // 'B'
    else if (ch < 13.0) return 541199.0;  // 'C'
    else if (ch < 14.0) return 575038.0;  // 'D'
    else if (ch < 15.0) return 999967.0;  // 'E'
    else if (ch < 16.0) return 999967.0;  // 'F'
    else if (ch < 17.0) return 770575.0;  // 'G'
    else if (ch < 18.0) return 1033777.0; // 'H'
    else if (ch < 19.0) return 135310.0;  // 'I'
    else if (ch < 20.0) return 67655.0;   // 'J'
    else if (ch < 21.0) return 807505.0;  // 'K'
    else if (ch < 22.0) return 541200.0;  // 'L'
    else if (ch < 23.0) return 710513.0;  // 'M'
    else if (ch < 24.0) return 710449.0;  // 'N'
    else if (ch < 25.0) return 575022.0;  // 'O'
    else if (ch < 26.0) return 1001022.0; // 'P'
    else if (ch < 27.0) return 575022.0;  // 'Q'
    else if (ch < 28.0) return 1001022.0; // 'R'
    else if (ch < 29.0) return 475663.0;  // 'S'
    else if (ch < 30.0) return 135327.0;  // 'T'
    else if (ch < 31.0) return 575025.0;  // 'U'
    else if (ch < 32.0) return 575025.0;  // 'V'
    else if (ch < 33.0) return 706097.0;  // 'W'
    else if (ch < 34.0) return 141873.0;  // 'X'
    else if (ch < 35.0) return 141873.0;  // 'Y'
    else                return 133183.0;  // 'Z'
}
// rows 4-6 of the 5x7 glyph, packed as bits ((row-4)*5+col)
float glyphLo(float ch){
    if      (ch < 1.0)  return 14905.0; // '0'
    else if (ch < 2.0)  return 14468.0; // '1'
    else if (ch < 3.0)  return 32004.0; // '2'
    else if (ch < 4.0)  return 14881.0; // '3'
    else if (ch < 5.0)  return 2143.0;  // '4'
    else if (ch < 6.0)  return 14881.0; // '5'
    else if (ch < 7.0)  return 14897.0; // '6'
    else if (ch < 8.0)  return 8456.0;  // '7'
    else if (ch < 9.0)  return 14897.0; // '8'
    else if (ch < 10.0) return 12353.0; // '9'
    else if (ch < 11.0) return 17969.0; // 'A'
    else if (ch < 12.0) return 31281.0; // 'B'
    else if (ch < 13.0) return 15888.0; // 'C'
    else if (ch < 14.0) return 31281.0; // 'D'
    else if (ch < 15.0) return 32272.0; // 'E'
    else if (ch < 16.0) return 16912.0; // 'F'
    else if (ch < 17.0) return 15921.0; // 'G'
    else if (ch < 18.0) return 17969.0; // 'H'
    else if (ch < 19.0) return 14468.0; // 'I'
    else if (ch < 20.0) return 12866.0; // 'J'
    else if (ch < 21.0) return 18004.0; // 'K'
    else if (ch < 22.0) return 32272.0; // 'L'
    else if (ch < 23.0) return 17969.0; // 'M'
    else if (ch < 24.0) return 17971.0; // 'N'
    else if (ch < 25.0) return 14897.0; // 'O'
    else if (ch < 26.0) return 16912.0; // 'P'
    else if (ch < 27.0) return 13909.0; // 'Q'
    else if (ch < 28.0) return 18004.0; // 'R'
    else if (ch < 29.0) return 30753.0; // 'S'
    else if (ch < 30.0) return 4228.0;  // 'T'
    else if (ch < 31.0) return 14897.0; // 'U'
    else if (ch < 32.0) return 4433.0;  // 'V'
    else if (ch < 33.0) return 18293.0; // 'W'
    else if (ch < 34.0) return 17962.0; // 'X'
    else if (ch < 35.0) return 4228.0;  // 'Y'
    else                return 32264.0; // 'Z'
}
float fontInk(float ch, float col, float row){
    // encoder packs column 0 as the high bit of each 5-bit row group
    // (weight 16), so decoding must mirror col back: (4-col), not col.
    float bits, idx;
    if (row < 4.0){ bits = glyphHi(ch); idx = row*5.0 + (4.0 - col); }
    else          { bits = glyphLo(ch); idx = (row - 4.0)*5.0 + (4.0 - col); }
    return mod(floor(bits/pow(2.0, idx)), 2.0);
}

void main(){
    vec2 res = RENDERSIZE.xy;
    float gT = TIME;

    float len = floor(clamp(uLength, 3.0, 16.0) + 0.5);
    float cellsWide = 5.0, cellsTall = 7.0, spacingCells = 6.0;
    float totalCellsWide = len*spacingCells - 1.0;

    float unit = min((res.x*0.85)/totalCellsWide, (res.y*0.55)/cellsTall)*clamp(uScale, 0.3, 2.0);
    float blockW = totalCellsWide*unit;
    float blockH = cellsTall*unit;

    vec2 pix = gl_FragCoord.xy - 0.5*res;

    // row-tear glitch: shift x per coarse row band, per short time-slice (no-op at uGlitch=0)
    float glitchRow = floor((blockH*0.5 - pix.y)/max(unit, 0.0001));
    pix.x += (hash21(vec2(glitchRow, floor(gT*18.0))) - 0.5)*unit*4.0*uGlitch;

    float localX = pix.x + blockW*0.5;
    float charIndexF = floor(localX/(spacingCells*unit));
    float colCont = (localX - charIndexF*(spacingCells*unit))/unit;
    float colF = floor(colCont);
    float colFrac = fract(colCont);

    float gapMaskX = 1.0 - step(cellsWide, colCont);
    float inBoundsX = step(0.0, charIndexF)*step(charIndexF, len - 1.0)*gapMaskX;

    // uInstanceSeed is supplied by the ISFBrowser host itself (one fixed
    // random value per loaded plugin instance, like TIME/RENDERSIZE) so two
    // copies of this shader show different strings even at identical uSeed.
    float chSeed = charIndexF + uSeed*7.0 + uInstanceSeed*11.3;

    // vertical bounce per character (no-op at uVertical=0)
    float yOff = sin(gT*1.3 + chSeed*3.7)*unit*1.8*uVertical;

    // split-flap flip: squashes the glyph vertically to ~0 height and swaps
    // its identity at the flat point, then unsquashes (no-op at uFlip=0)
    float flipRate = 0.6*uFlip;
    float flipCyclePos = fract(gT*flipRate + chSeed*0.13);
    float squash = cos(flipCyclePos*6.2831853);
    float gen = floor(gT*flipRate + chSeed*0.13 + 0.25);

    float rowContTop = (blockH*0.5 - (pix.y - yOff))/unit;
    float rowCentered = (rowContTop - cellsTall*0.5)/max(abs(squash), 0.06);
    float rowContFinal = rowCentered + cellsTall*0.5;
    float rowF = floor(rowContFinal);
    float rowFrac = fract(rowContFinal);

    float inBoundsY = step(0.0, rowF)*step(rowF, cellsTall - 1.0);

    // periodic full re-roll of a character's identity (no-op at uCharChange=0);
    // max rate is a rapid scramble/flicker, not a gentle cycle.
    float swap = floor(gT*(60.0*uCharChange) + chSeed*3.0);
    float combinedGen = gen*97.0 + swap*31.0;
    float charSel = clamp(floor(hash21(vec2(charIndexF*1.7 + uSeed*7.0 + uInstanceSeed*11.3, combinedGen))*36.0), 0.0, 35.0);

    float ink = 0.0;
    if (inBoundsX > 0.5 && inBoundsY > 0.5){
        ink = fontInk(charSel, colF, rowF);

        float margin = 0.14; // small gap between dots for a dot-matrix look
        float dotFill = step(margin, colFrac)*step(colFrac, 1.0 - margin)*
                         step(margin, rowFrac)*step(rowFrac, 1.0 - margin);
        ink *= dotFill;

        // dissolve: at uDissolve=0, dPhase is pinned to 1.0 -> fully solid
        float dPhase = mix(1.0, 0.5 + 0.5*sin(gT*0.7 + chSeed*5.0), uDissolve);
        float noiseV = hash21(vec2(colF + charIndexF*13.0, rowF + 7.0) + chSeed*11.0);
        ink *= step(1.0 - dPhase, noiseV);
    }

    gl_FragColor = vec4(vec3(ink), 1.0);
}
