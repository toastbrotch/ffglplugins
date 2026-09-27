#include "VideoDecoder.h"

extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>
}

#include <FFGLSDK.h>//For FFGLLog::LogToHost, used to surface decode errors to the host.

#include <chrono>

namespace videopong
{
namespace
{
	void LogError( const std::string& filePath, const std::string& message )
	{
		FFGLLog::LogToHost( ( "VideoPong: " + message + " (" + filePath + ")" ).c_str() );
	}
}//anonymous namespace

VideoDecoder::VideoDecoder() :
	stopRequested( false ),
	playing( true ),
	looping( true ),
	isOpen( false ),
	latestWidth( 0 ),
	latestHeight( 0 ),
	frameDirty( false )
{
}
VideoDecoder::~VideoDecoder()
{
	Close();
}

void VideoDecoder::Open( const std::string& filePath )
{
	Close();

	stopRequested = false;
	playing       = true;
	looping       = true;
	frameDirty    = false;

	thread = std::thread( &VideoDecoder::ThreadMain, this, filePath );
}

void VideoDecoder::Close()
{
	stopRequested = true;
	if( thread.joinable() )
		thread.join();
	isOpen = false;
}

void VideoDecoder::SetPlaying( bool value )
{
	playing = value;
}
void VideoDecoder::SetLooping( bool value )
{
	looping = value;
}

bool VideoDecoder::TryGetLatestFrame( std::vector< uint8_t >& outRgba, int& outWidth, int& outHeight )
{
	std::lock_guard< std::mutex > lock( frameMutex );
	if( !frameDirty )
		return false;

	outRgba     = latestFrameRgba;
	outWidth    = latestWidth;
	outHeight   = latestHeight;
	frameDirty  = false;
	return true;
}

void VideoDecoder::ThreadMain( std::string filePath )
{
	AVFormatContext* formatCtx = nullptr;
	if( avformat_open_input( &formatCtx, filePath.c_str(), nullptr, nullptr ) != 0 )
	{
		LogError( filePath, "failed to open file" );
		return;
	}

	if( avformat_find_stream_info( formatCtx, nullptr ) < 0 )
	{
		LogError( filePath, "failed to read stream info" );
		avformat_close_input( &formatCtx );
		return;
	}

	int videoStreamIndex = av_find_best_stream( formatCtx, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0 );
	if( videoStreamIndex < 0 )
	{
		LogError( filePath, "no video stream found" );
		avformat_close_input( &formatCtx );
		return;
	}

	AVStream* stream        = formatCtx->streams[ videoStreamIndex ];
	const AVCodec* codec    = avcodec_find_decoder( stream->codecpar->codec_id );
	if( codec == nullptr )
	{
		LogError( filePath, "unsupported codec" );
		avformat_close_input( &formatCtx );
		return;
	}

	AVCodecContext* codecCtx = avcodec_alloc_context3( codec );
	if( codecCtx == nullptr || avcodec_parameters_to_context( codecCtx, stream->codecpar ) < 0 )
	{
		LogError( filePath, "failed to set up decoder context" );
		if( codecCtx != nullptr )
			avcodec_free_context( &codecCtx );
		avformat_close_input( &formatCtx );
		return;
	}

	if( avcodec_open2( codecCtx, codec, nullptr ) < 0 )
	{
		LogError( filePath, "failed to open decoder" );
		avcodec_free_context( &codecCtx );
		avformat_close_input( &formatCtx );
		return;
	}

	int width  = codecCtx->width;
	int height = codecCtx->height;
	if( width <= 0 || height <= 0 )
	{
		LogError( filePath, "invalid video dimensions" );
		avcodec_free_context( &codecCtx );
		avformat_close_input( &formatCtx );
		return;
	}

	SwsContext* swsCtx = sws_getContext( width, height, codecCtx->pix_fmt, width, height, AV_PIX_FMT_RGBA, SWS_BILINEAR, nullptr, nullptr, nullptr );
	if( swsCtx == nullptr )
	{
		LogError( filePath, "failed to create scaler" );
		avcodec_free_context( &codecCtx );
		avformat_close_input( &formatCtx );
		return;
	}

	AVFrame* frame     = av_frame_alloc();
	AVFrame* rgbaFrame = av_frame_alloc();
	AVPacket* packet   = av_packet_alloc();

	std::vector< uint8_t > rgbaBuffer( (size_t)width * (size_t)height * 4 );
	av_image_fill_arrays( rgbaFrame->data, rgbaFrame->linesize, rgbaBuffer.data(), AV_PIX_FMT_RGBA, width, height, 1 );

	double frameDurationSeconds = 1.0 / 30.0;
	if( stream->avg_frame_rate.num > 0 && stream->avg_frame_rate.den > 0 )
		frameDurationSeconds = (double)stream->avg_frame_rate.den / (double)stream->avg_frame_rate.num;

	isOpen = true;

	while( !stopRequested.load() )
	{
		if( !playing.load() )
		{
			std::this_thread::sleep_for( std::chrono::milliseconds( 30 ) );
			continue;
		}

		int readResult = av_read_frame( formatCtx, packet );
		if( readResult < 0 )
		{
			if( !looping.load() )
				break;

			if( av_seek_frame( formatCtx, videoStreamIndex, 0, AVSEEK_FLAG_BACKWARD ) < 0 )
				break;
			avcodec_flush_buffers( codecCtx );
			continue;
		}

		if( packet->stream_index != videoStreamIndex )
		{
			av_packet_unref( packet );
			continue;
		}

		if( avcodec_send_packet( codecCtx, packet ) == 0 )
		{
			while( avcodec_receive_frame( codecCtx, frame ) == 0 )
			{
				sws_scale( swsCtx, frame->data, frame->linesize, 0, height, rgbaFrame->data, rgbaFrame->linesize );

				{
					std::lock_guard< std::mutex > lock( frameMutex );
					latestFrameRgba = rgbaBuffer;
					latestWidth     = width;
					latestHeight    = height;
					frameDirty      = true;
				}

				std::this_thread::sleep_for( std::chrono::duration< double >( frameDurationSeconds ) );

				if( stopRequested.load() )
					break;
			}
		}

		av_packet_unref( packet );
	}

	av_packet_free( &packet );
	av_frame_free( &rgbaFrame );
	av_frame_free( &frame );
	sws_freeContext( swsCtx );
	avcodec_free_context( &codecCtx );
	avformat_close_input( &formatCtx );

	isOpen = false;
}

}//namespace videopong
