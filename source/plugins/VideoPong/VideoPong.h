#pragma once
#include <FFGLSDK.h>
#include "VideoDecoder.h"
#include "VideoPongBrowserWindow.h"

#include <memory>
#include <mutex>

class VideoPong : public CFFGLPlugin
{
public:
	VideoPong();
	~VideoPong();

	//CFFGLPlugin
	FFResult InitGL( const FFGLViewportStruct* vp ) override;
	FFResult ProcessOpenGL( ProcessOpenGLStruct* pGL ) override;
	FFResult DeInitGL() override;

	FFResult SetFloatParameter( unsigned int dwIndex, float value ) override;
	float GetFloatParameter( unsigned int index ) override;

private:
	//Opens the clip browser window (Windows only, elsewhere it just logs and does nothing) -
	//the only way to load a clip; there's no more standalone Search/Fetch Clip parameter.
	void OpenBrowser();

	//Opens the clip cache folder in Explorer (Windows only elsewhere it just logs the path).
	void OpenCacheFolder();

	//Picks up a clip picked in the browser window (if any) and (re)starts the decoder for it.
	//Called from ProcessOpenGL, never touches GL itself.
	void ConsumePendingClip();

	bool playing;
	bool looping;

	std::mutex pendingClipMutex;
	std::string pendingLocalPath;//Set by the browser window once a clip has been downloaded.
	bool pendingClipReady;

	ffglex::FFGLShader shader;
	ffglex::FFGLScreenQuad quad;

	GLuint videoTexture;
	int videoTextureWidth;
	int videoTextureHeight;

	std::unique_ptr< videopong::VideoDecoder > decoder;
	std::unique_ptr< videopong::BrowserWindow > browserWindow;
};
