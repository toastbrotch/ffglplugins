#pragma once
#include <FFGLSDK.h>
#include <chrono>
#include <string>
#include <vector>

// FFGL (Resolume 7.x, 64-bit) EFFECT sibling of ISFBrowser: same scan-a-folder-of-.fs,
// dynamic-native-parameters design, but FF_EFFECT/MinInputs(1) instead of FF_SOURCE, so it applies
// to a clip/layer instead of generating standalone. An ISF shader's own "TYPE": "image" input
// (by ISF convention usually named inputImage) is bound to the plugin's actual input texture each
// frame; every other float/bool/long input still becomes its own real, named Resolume parameter
// exactly like the source version.
class FFGLISFEffect : public CFFGLPlugin
{
public:
	FFGLISFEffect();

	FFResult InitGL( const FFGLViewportStruct* vp ) override;
	FFResult ProcessOpenGL( ProcessOpenGLStruct* pGL ) override;
	FFResult DeInitGL() override;

	FFResult SetFloatParameter( unsigned int dwIndex, float value ) override;
	float GetFloatParameter( unsigned int index ) override;
	char* GetParameterDisplay( unsigned int index ) override;

private:
	static const unsigned int MAX_INPUTS   = 16;
	static const unsigned int PARAM_SHADER = 0;
	static const unsigned int PARAM_INPUT0 = 1;

	struct IsfInput
	{
		std::string name;
		std::string label;
		float minVal     = 0.0f;
		float maxVal     = 1.0f;
		float defaultVal = 0.0f;
	};

	void ScanShaderFolder();
	bool LoadShader( int idx );

	std::vector< std::string > fsPaths;
	std::vector< std::string > fsLabels;
	int  currentIdx;
	int  pendingIdx;
	bool needReload;
	char displayBuf[ 128 ];

	std::vector< IsfInput > inputs;
	float inputNorm[ MAX_INPUTS ];

	std::string imageInputName;// the shader's own "TYPE":"image" INPUT name, if it declared one;
	                            // empty if this shader doesn't take an image input at all.

	ffglex::FFGLShader     shader;
	ffglex::FFGLScreenQuad quad;

	std::chrono::steady_clock::time_point startTime;// see ISFBrowser: deliberately wall-clock, not host transport time
	double localTime;
	double lastElapsed;
	int    frameIndex;
	float  instanceSeed;
};
