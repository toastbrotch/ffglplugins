# Actually compiles every .fs in this folder (plus, for convenience, ISFBrowser's shared library)
# on the real GPU, using the exact preamble ISFEffect.cpp's LoadShader() builds (INPUTS -> uniform
# declarations, "image"-type INPUT -> sampler2D, core-profile wrapping). Needs
# `pip install moderngl` and a working GL context; run with plain `python3 check_shaders.py`.
import sys, glob, json
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

    root = json.loads(header)
    image_name = None
    float_names = []
    for item in root.get("INPUTS", []):
        name, typ = item.get("NAME"), item.get("TYPE")
        if typ == "image" and image_name is None:
            image_name = name
        elif typ in ("float", "bool", "long"):
            float_names.append(name)

    preamble_uniforms = ""
    if image_name:
        preamble_uniforms += f"uniform sampler2D {image_name};\n"
    preamble_uniforms += "\n".join(f"uniform float {n};" for n in float_names)

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
uniform vec2 MaxUV;
layout( location = 0 ) in vec4 vPosition;
layout( location = 1 ) in vec2 vUV;
out vec2 isf_FragNormCoord;
void main() {
    gl_Position = vPosition;
    isf_FragNormCoord = vUV * MaxUV;
}
"""
    try:
        ctx.program(vertex_shader=vs_src, fragment_shader=fs_src)
        return (image_name, len(float_names)), None
    except Exception as e:
        return (image_name, len(float_names)), str(e)

ctx = moderngl.create_standalone_context()
ok = True
paths = sorted(glob.glob('/home/ivo/git/ffgl/source/plugins/ISFEffect/*.fs')) + \
        sorted(glob.glob('/home/ivo/git/ffgl/binaries/x64/Debug/*.fs'))
seen = set()
for path in paths:
    name = path.split('/')[-1]
    if name in seen:
        continue
    seen.add(name)
    info, err = check(path)
    if err:
        ok = False
        print(f"FAIL  {name}  {info}")
        print("  " + err.replace("\n", "\n  "))
    else:
        print(f"OK    {name}  image={info[0]} floats={info[1]}")
sys.exit(0 if ok else 1)
