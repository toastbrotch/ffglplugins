#pragma once
#include <FFGLSDK.h>
#include <chrono>
#include <string>
#include <vector>

// FFGL (Resolume 7.x, 64-bit) port of the FreeFrame-1.0 ISF Browser plugin used elsewhere in this
// project's sibling repo. Scans its own folder for .fs (ISF/GLSL) files, lets you pick one via a
// native option dropdown, and — unlike the FreeFrame version, which was capped at 4 sliders and
// needed a custom popup window plus a slot-assignment UI to work around that — exposes every one
// of the shader's own ISF inputs directly as its own real, named Resolume parameter. No custom
// window, no background thread: everything lives in Resolume's own parameter panel.
class FFGLISFBrowser : public CFFGLPlugin
{
public:
	FFGLISFBrowser();

	FFResult InitGL( const FFGLViewportStruct* vp ) override;
	FFResult ProcessOpenGL( ProcessOpenGLStruct* pGL ) override;
	FFResult DeInitGL() override;

	FFResult SetFloatParameter( unsigned int dwIndex, float value ) override;
	float GetFloatParameter( unsigned int index ) override;
	char* GetParameterDisplay( unsigned int index ) override;

private:
	// Param 0 is the shader-select dropdown; params 1..MAX_INPUTS mirror an ISF shader's own
	// "INPUTS" array, shown/hidden/renamed/ranged to match whichever shader is currently loaded.
	static const unsigned int MAX_INPUTS  = 16;
	static const unsigned int PARAM_SHADER = 0;
	static const unsigned int PARAM_INPUT0 = 1;

	struct IsfInput
	{
		std::string name;      // the actual uniform name declared in the shader
		std::string label;     // ISF LABEL if present, else name — shown to the user
		float minVal     = 0.0f;
		float maxVal     = 1.0f;
		float defaultVal = 0.0f;
	};

	void ScanShaderFolder();
	bool LoadShader( int idx );

	std::vector< std::string > fsPaths;  // full paths, sorted, scanned once at construction
	std::vector< std::string > fsLabels; // display names (filename, no dir/extension)
	int  currentIdx;
	int  pendingIdx;   // set by SetFloatParameter(PARAM_SHADER, ...); consumed in ProcessOpenGL
	bool needReload;
	char displayBuf[ 128 ];

	std::vector< IsfInput > inputs;      // active shader's float/bool/long inputs (<= MAX_INPUTS)
	float inputNorm[ MAX_INPUTS ];       // normalised 0..1 value per slot, like the value FFGL itself uses on the wire

	ffglex::FFGLShader     shader;
	ffglex::FFGLScreenQuad quad;

	// TIME uniform: wall-clock seconds since this instance started, never reset on shader switch.
	// Deliberately NOT derived from the host's SetTime()/hostTime — that's the clip/composition
	// transport position, which FFGL never documents as running at real-time speed (a clip's own
	// speed/BPM-sync setting affects it), and did visibly run faster than real time. The old
	// FreeFrame version never had this problem because it used its own fixed 1/60-per-frame
	// wall-clock accumulator, completely independent of the host's transport — this does the same
	// via steady_clock instead of a fixed-step assumption.
	std::chrono::steady_clock::time_point startTime;
	double localTime;
	double lastElapsed;
	int    frameIndex;
	float  instanceSeed;    // unique per plugin instance, so multiple copies loaded at once don't look identical
};
