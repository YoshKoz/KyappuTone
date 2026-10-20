using FFmpeg.AutoGen;

namespace KyappuTone.Audio.Decoding;

/// <summary>
/// Decodes any FFmpeg-supported audio file into interleaved float32 at 48 kHz stereo.
/// Not thread-safe: one instance is owned by one deck.
/// </summary>
public sealed unsafe class FfmpegDecoder : IDisposable
{
    private AVFormatContext* _format;
    private AVCodecContext* _codec;
    private SwrContext* _resampler;
    private AVPacket* _packet;
    private AVFrame* _frame;
    private int _streamIndex;
    private bool _disposed;

    private float[] _pending = new float[16384];
    private int _pendingOffset;
    private int _pendingCount;

    private bool _draining;
    private long _framesDelivered;
    private TimeSpan _positionOrigin;

    private FfmpegDecoder() { }

    /// <summary>Opens <paramref name="path"/> and prepares the resampler.</summary>
    /// <exception cref="AudioFormatException">The file cannot be opened or decoded.</exception>
    public static FfmpegDecoder Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FfmpegBootstrapper.EnsureLoaded();

        var decoder = new FfmpegDecoder();
        try
        {
            decoder.Initialize(path);
            return decoder;
        }
        catch
        {
            decoder.Dispose();
            throw;
        }
    }

    /// <summary>Normalized output format.</summary>
    public DecodedFormat Format { get; } = new();

    /// <summary>Total duration reported by the container.</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>Current playback position.</summary>
    public TimeSpan Position => _positionOrigin + TimeSpan.FromSeconds(_framesDelivered / (double)DecodedFormat.TargetSampleRate);

    private void Initialize(string path)
    {
        AVFormatContext* format = null;
        var openResult = ffmpeg.avformat_open_input(&format, path, null, null);
        if (openResult < 0)
        {
            throw new AudioFormatException($"Cannot open '{path}': {FfmpegBootstrapper.ErrorText(openResult)}");
        }

        _format = format;

        var infoResult = ffmpeg.avformat_find_stream_info(_format, null);
        if (infoResult < 0)
        {
            throw new AudioFormatException($"Cannot read stream info: {FfmpegBootstrapper.ErrorText(infoResult)}");
        }

        _streamIndex = ffmpeg.av_find_best_stream(_format, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
        if (_streamIndex < 0)
        {
            throw new AudioFormatException("No audio stream found.");
        }

        var stream = _format->streams[_streamIndex];
        var codecParameters = stream->codecpar;

        var codec = ffmpeg.avcodec_find_decoder(codecParameters->codec_id);
        if (codec == null)
        {
            throw new AudioFormatException($"No decoder for codec {codecParameters->codec_id}.");
        }

        _codec = ffmpeg.avcodec_alloc_context3(codec);
        var parametersResult = ffmpeg.avcodec_parameters_to_context(_codec, codecParameters);
        if (parametersResult < 0)
        {
            throw new AudioFormatException($"Cannot copy codec parameters: {FfmpegBootstrapper.ErrorText(parametersResult)}");
        }

        var openCodecResult = ffmpeg.avcodec_open2(_codec, codec, null);
        if (openCodecResult < 0)
        {
            throw new AudioFormatException($"Cannot open codec: {FfmpegBootstrapper.ErrorText(openCodecResult)}");
        }

        _packet = ffmpeg.av_packet_alloc();
        _frame = ffmpeg.av_frame_alloc();
        if (_packet == null || _frame == null)
        {
            throw new AudioFormatException("Cannot allocate FFmpeg packet or frame.");
        }

        AVChannelLayout outputLayout;
        ffmpeg.av_channel_layout_default(&outputLayout, DecodedFormat.TargetChannels);

        SwrContext* resampler = null;
        var resampleResult = ffmpeg.swr_alloc_set_opts2(
            &resampler,
            &outputLayout,
            AVSampleFormat.AV_SAMPLE_FMT_FLT,
            DecodedFormat.TargetSampleRate,
            &_codec->ch_layout,
            _codec->sample_fmt,
            _codec->sample_rate,
            0,
            null);

        if (resampleResult < 0 || resampler == null)
        {
            throw new AudioFormatException($"Cannot create resampler: {FfmpegBootstrapper.ErrorText(resampleResult)}");
        }

        _resampler = resampler;
        var initResult = ffmpeg.swr_init(_resampler);
        if (initResult < 0)
        {
            throw new AudioFormatException($"Cannot initialise resampler: {FfmpegBootstrapper.ErrorText(initResult)}");
        }

        Duration = ReadDuration(stream, _format);
    }

    private static TimeSpan ReadDuration(AVStream* stream, AVFormatContext* format)
    {
        if (stream->duration > 0)
        {
            return TimeSpan.FromSeconds(stream->duration * ffmpeg.av_q2d(stream->time_base));
        }

        if (format->duration > 0)
        {
            return TimeSpan.FromSeconds(format->duration / (double)ffmpeg.AV_TIME_BASE);
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Reads up to <paramref name="destination"/>.Length interleaved floats.
    /// Returns 0 at end of stream.
    /// </summary>
    public int Read(Span<float> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var written = 0;
        while (written < destination.Length)
        {
            if (_pendingCount > 0)
            {
                var take = Math.Min(_pendingCount, destination.Length - written);
                _pending.AsSpan(_pendingOffset, take).CopyTo(destination[written..]);
                _pendingOffset += take;
                _pendingCount -= take;
                _framesDelivered += take / DecodedFormat.TargetChannels;
                written += take;
                continue;
            }

            if (!FillPending()) break;
        }

        return written;
    }

    /// <summary>Seeks to <paramref name="position"/> (backwards to the nearest seek point).</summary>
    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;

        var target = position == TimeSpan.Zero
            ? 0
            : Math.Max(0, (long)(position.TotalSeconds * ffmpeg.AV_TIME_BASE) / 1000 * 1000);

        var result = ffmpeg.av_seek_frame(_format, -1, target, ffmpeg.AVSEEK_FLAG_BACKWARD);
        if (result < 0)
        {
            throw new AudioFormatException($"Seek failed: {FfmpegBootstrapper.ErrorText(result)}");
        }

        ffmpeg.avcodec_flush_buffers(_codec);
        ffmpeg.swr_init(_resampler);
        _pendingOffset = 0;
        _pendingCount = 0;
        _draining = false;
        _framesDelivered = 0;
        _positionOrigin = position;
    }

    private bool FillPending()
    {
        while (true)
        {
            var receive = ffmpeg.avcodec_receive_frame(_codec, _frame);
            if (receive == 0)
            {
                ConvertFrame(_frame);
                ffmpeg.av_frame_unref(_frame);
                if (_pendingCount > 0) return true;
                continue;
            }

            if (receive == ffmpeg.AVERROR_EOF) return false;

            if (receive != ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                throw new AudioFormatException($"Decode failed: {FfmpegBootstrapper.ErrorText(receive)}");
            }

            if (_draining) return false;

            var read = ffmpeg.av_read_frame(_format, _packet);
            if (read == ffmpeg.AVERROR_EOF || read < 0)
            {
                ffmpeg.avcodec_send_packet(_codec, null);
                _draining = true;
                continue;
            }

            try
            {
                if (_packet->stream_index == _streamIndex)
                {
                    var send = ffmpeg.avcodec_send_packet(_codec, _packet);
                    if (send < 0 && send != ffmpeg.AVERROR(ffmpeg.EAGAIN))
                    {
                        throw new AudioFormatException($"Cannot submit packet: {FfmpegBootstrapper.ErrorText(send)}");
                    }
                }
            }
            finally
            {
                ffmpeg.av_packet_unref(_packet);
            }
        }
    }

    private void ConvertFrame(AVFrame* frame)
    {
        var capacity = ffmpeg.swr_get_out_samples(_resampler, frame->nb_samples);
        if (capacity <= 0) return;

        var floatCount = capacity * DecodedFormat.TargetChannels;
        if (_pending.Length < floatCount) _pending = new float[floatCount];

        fixed (float* output = _pending)
        {
            var outputPointer = (byte*)output;
            var converted = ffmpeg.swr_convert(
                _resampler,
                &outputPointer,
                capacity,
                frame->extended_data,
                frame->nb_samples);

            if (converted < 0)
            {
                throw new AudioFormatException($"Resample failed: {FfmpegBootstrapper.ErrorText(converted)}");
            }

            _pendingOffset = 0;
            _pendingCount = converted * DecodedFormat.TargetChannels;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        fixed (SwrContext** resampler = &_resampler)
        {
            ffmpeg.swr_free(resampler);
        }

        fixed (AVFrame** frame = &_frame)
        {
            ffmpeg.av_frame_free(frame);
        }

        fixed (AVPacket** packet = &_packet)
        {
            ffmpeg.av_packet_free(packet);
        }

        fixed (AVCodecContext** codec = &_codec)
        {
            ffmpeg.avcodec_free_context(codec);
        }

        fixed (AVFormatContext** format = &_format)
        {
            ffmpeg.avformat_close_input(format);
        }
    }
}
