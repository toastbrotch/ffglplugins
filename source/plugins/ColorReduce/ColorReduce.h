#pragma once
#include <FFGLSDK.h>
#include <vector>

// FFGL (Resolume 7.x, 64-bit) port of the FreeFrame-1.0 ColorReduce effect from this project's
// sibling repo: finds the N most dominant colours in the frame (via a coarse 6x6x6 histogram)
// and remaps every pixel to its nearest dominant colour.
//
// The histogram step is inherently whole-frame (you need every pixel counted before you know
// which N colours are dominant), which doesn't fit a single stateless fragment shader pass. This
// reads the input texture back to the CPU once per frame and runs the exact same histogram/top-N
// algorithm as the original, then uploads the resulting palette to a GPU shader that does only
// the final per-pixel remap. That CPU readback is a real (if usually small) performance cost
// compared to a pure-GPU effect - a GPU histogram (compute shader + atomics) would avoid it, but
// is out of scope for this port.
class ColorReduce : public CFFGLPlugin
{
public:
	ColorReduce();

	FFResult InitGL( const FFGLViewportStruct* vp ) override;
	FFResult ProcessOpenGL( ProcessOpenGLStruct* pGL ) override;
	FFResult DeInitGL() override;

	FFResult SetFloatParameter( unsigned int dwIndex, float value ) override;
	float GetFloatParameter( unsigned int index ) override;
	char* GetParameterDisplay( unsigned int index ) override;

	// Public so the free-function histogram helpers in the .cpp (kept as free functions, matching
	// the original FreeFrame source's style) can reference them.
	static const int MIN_COLORS  = 2;
	static const int MAX_COLORS  = 64;// beyond 64 the effect is invisible - matches the original
	static const int HIST_LEVELS = 6;
	static const int HIST_BINS   = HIST_LEVELS * HIST_LEVELS * HIST_LEVELS;

private:
	static const unsigned int PARAM_COLORS = 0;
	static const unsigned int PARAM_PALROT = 1;

	int NormToColors( float norm ) const;

	ffglex::FFGLShader     remapShader;
	ffglex::FFGLScreenQuad quad;

	std::vector< unsigned char > readback;// CPU copy of the input texture, resized to match it

	float colorsNorm;
	float palRotNorm;
	char  displayBuf[ 32 ];
};
