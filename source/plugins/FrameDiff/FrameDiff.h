#pragma once
#include <FFGLSDK.h>

// FFGL (Resolume 7.x, 64-bit) port of the FreeFrame-1.0 FrameDiff effect from this project's
// sibling repo. The CPU version kept a ring of up to 30 raw frame buffers in system RAM; this
// keeps the same ring, but as GPU framebuffer objects, and does the diff in a fragment shader
// instead of a per-pixel C loop.
class FrameDiff : public CFFGLPlugin
{
public:
	FrameDiff();

	FFResult InitGL( const FFGLViewportStruct* vp ) override;
	FFResult ProcessOpenGL( ProcessOpenGLStruct* pGL ) override;
	FFResult DeInitGL() override;

	FFResult SetFloatParameter( unsigned int dwIndex, float value ) override;
	float GetFloatParameter( unsigned int index ) override;
	char* GetParameterDisplay( unsigned int index ) override;

private:
	static const int MIN_DELAY_FRAMES = 1;
	static const int MAX_DELAY_FRAMES = 30;
	static const unsigned int PARAM_FRAMEDELAY = 0;
	static const unsigned int PARAM_HARDNESS   = 1;

	int NormToFrames( float norm ) const;

	ffglex::FFGLShader     copyShader;// unpremultiplies + stores one frame straight-color into a ring slot
	ffglex::FFGLShader     diffShader;// abs(current - delayed) luma, with the Hardness contrast curve
	ffglex::FFGLScreenQuad quad;

	ffglex::FFGLFBO ring[ MAX_DELAY_FRAMES ];
	bool ringReady;
	int  ringW, ringH;
	int  writeHead;

	float delayNorm;
	float hardnessNorm;
	char  displayBuf[ 32 ];
};
