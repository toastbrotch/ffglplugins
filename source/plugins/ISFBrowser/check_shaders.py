# Actually compiles every .fs in binaries/x64/Debug on the real GPU, using the exact preamble
# ISFBrowser.cpp's LoadShader() builds (INPUTS -> uniform declarations, core-profile wrapping),
# so GLSL errors surface here instead of only inside Resolume. Needs `pip install moderngl` and
# a working GL context; run with plain `python3 check_shaders.py`.
import re, sys, glob
import moderngl

def check(path):
    src = open(path).read()
    hs = src.find('/*')
    he = src.find('*/', hs)
    if hs < 0 or he < 0:
        return None, "no /*{ ... }*/ ISF header found"
    header = src[hs+2:he]
    body = src[he+2:]
    if body.startswith('\n'):
        body = body[1:]

    names = re.findall(r'"NAME"\s*:\s*"([^"]+)"', header)
    preamble_uniforms = "\n".join(f"uniform float {n};" for n in names)

    # Exact match to the preamble ISFBrowser.cpp (FFGL version) builds in LoadShader().
    fs_src = f"""#version 410 core
uniform float TIME;
uniform float TIMEDELTA;
uniform int   FRAMEINDEX;
uniform vec2  RENDERSIZE;
uniform float uInstanceSeed;
in vec2 isf_FragNormCoord;
out vec4 fragColor;
#define gl_FragColor fragColor
{preamble_uniforms}
{body}
"""
    vs_src = """#version 410 core
layout( location = 0 ) in vec4 vPosition;
layout( location = 1 ) in vec2 vUV;
out vec2 isf_FragNormCoord;
void main() {
    gl_Position = vPosition;
    isf_FragNormCoord = vUV;
}
"""
    try:
        ctx.program(vertex_shader=vs_src, fragment_shader=fs_src)
        return len(names), None
    except Exception as e:
        return len(names), str(e)

ctx = moderngl.create_standalone_context()
ok = True
for path in sorted(glob.glob('/home/ivo/git/ffgl/binaries/x64/Debug/*.fs')):
    n, err = check(path)
    name = path.split('/')[-1]
    if err:
        ok = False
        print(f"FAIL  {name}  ({n} inputs)")
        print("  " + err.replace("\n", "\n  "))
    else:
        print(f"OK    {name}  ({n} inputs)")
sys.exit(0 if ok else 1)
