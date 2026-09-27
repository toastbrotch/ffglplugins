#pragma once
#include <atomic>
#include <mutex>
#include <string>
#include <thread>
#include <vector>
#include <cstdint>

//Decodes a local video file on a background thread and hands decoded RGBA frames over to
//whoever's rendering (typically the plugin's ProcessOpenGL, on the host's render thread) via
//TryGetLatestFrame(). Deliberately doesn't touch OpenGL itself - decoding happens off the GL
//thread, uploading the result to a texture is the caller's job.
//
//This header intentionally doesn't include any FFmpeg headers, they're only needed inside
//VideoDecoder.cpp.
namespace videopong
{
class VideoDecoder
{
public:
	VideoDecoder();
	~VideoDecoder();

	VideoDecoder( const VideoDecoder& )            = delete;
	VideoDecoder& operator=( const VideoDecoder& ) = delete;

	//Starts decoding/playing `filePath` on a background thread. Failures (bad file, unsupported
	//codec, etc.) are logged to the host rather than returned here, since opening happens
	//asynchronously; IsOpen() reports whether decoding is actually under way.
	void Open( const std::string& filePath );

	//Stops the decode thread and releases all decoder resources. Safe to call at any time,
	//including when nothing is open.
	void Close();

	void SetPlaying( bool playing );
	void SetLooping( bool looping );

	bool IsOpen() const { return isOpen.load(); }

	//If a new frame has been decoded since the last call, copies its pixels (tightly packed
	//RGBA8, top row first) into `outRgba`/`outWidth`/`outHeight` and returns true. Returns false
	//if there's nothing new to show yet.
	bool TryGetLatestFrame( std::vector< uint8_t >& outRgba, int& outWidth, int& outHeight );

private:
	void ThreadMain( std::string filePath );

	std::thread thread;
	std::atomic< bool > stopRequested;
	std::atomic< bool > playing;
	std::atomic< bool > looping;
	std::atomic< bool > isOpen;

	std::mutex frameMutex;
	std::vector< uint8_t > latestFrameRgba;
	int latestWidth;
	int latestHeight;
	bool frameDirty;
};

}//namespace videopong
