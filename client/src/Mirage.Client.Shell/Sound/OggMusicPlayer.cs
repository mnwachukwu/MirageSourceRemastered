using Microsoft.Xna.Framework.Audio;
using NVorbis;

namespace Mirage.Client.Shell.Sound;

/// <summary>
/// Streams a looping OGG track through a <see cref="DynamicSoundEffectInstance"/>. We own the PCM
/// queue: at the loop point the decoder rewinds and keeps filling the SAME buffer, so the seam falls
/// mid-buffer and OpenAL plays it contiguously with no gap. (MonoGame's <c>Song</c>/
/// <c>MediaPlayer.IsRepeating</c> stops and restarts the track instead, which is audibly gappy and
/// cannot skip to an arbitrary loop point.)
/// </summary>
/// <remarks>
/// Honors the RPG-Maker-style loop tags in the file's Vorbis comments, in sample frames:
/// <c>LOOPSTART</c> (frame to loop back to) and optional <c>LOOPLENGTH</c> (loop body length; loop
/// end = LOOPSTART + LOOPLENGTH, defaulting to end-of-file when absent). A track with no valid tags
/// simply loops in full, so untagged files need no changes.
/// </remarks>
public sealed class OggMusicPlayer : IMusicPlayer, IDisposable
{
    // ~186ms of audio per submitted buffer at 44.1kHz; MonoGame keeps two queued, so ~370ms sits
    // ahead of the play head — enough cushion to refill across a GC pause or a loading hitch.
    private const int FramesPerChunk = 8192;
    private const int PrefillChunks = 3;

    private VorbisReader? _reader;
    private DynamicSoundEffectInstance? _sfx;
    private string? _currentPath;
    private int _channels;
    private long _loopStart;          // per-channel frame to loop back to
    private long _loopEnd;            // per-channel frame the loop body ends at (exclusive)
    private float[] _floatBuf = [];
    private byte[] _pcmBuf = [];
    private float _volume = 1f;

    public void Play(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        // Already streaming this track — don't restart it (map re-entry, volume toggles, etc.).
        if (_currentPath == fullPath && _sfx is not null)
            return;
        Stop();
        // A missing audio device throws NoAudioHardwareException here; callers catch it and swap in
        // the NullMusicPlayer, so let it propagate.
        _reader = new VorbisReader(fullPath);
        _channels = _reader.Channels;
        ResolveLoopRegion();
        _sfx = new DynamicSoundEffectInstance(_reader.SampleRate,
            _channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo)
        { Volume = _volume };
        _floatBuf = new float[FramesPerChunk * _channels];
        _pcmBuf = new byte[_floatBuf.Length * sizeof(short)];
        _currentPath = fullPath;
        _sfx.BufferNeeded += OnBufferNeeded;
        for (int i = 0; i < PrefillChunks; i++) SubmitChunk();
        _sfx.Play();
    }

    /// <summary>
    /// Reads the optional loop-point Vorbis comments (sample frames) into the loop region, falling
    /// back to the whole file when they are absent or nonsensical. Loop end comes from
    /// <c>LOOPLENGTH</c> (body length) or, failing that, <c>LOOPEND</c> (absolute end frame) — both
    /// exclusive; if both are present LOOPLENGTH wins.
    /// </summary>
    private void ResolveLoopRegion()
    {
        long total = _reader!.TotalSamples;
        _loopStart = 0;
        _loopEnd = total;
        long start = ReadFrameTag("LOOPSTART");
        // LOOPSTART must land inside the track; LOOPSTART=0 is valid (loop from the very start while
        // still honoring a shorter length that trims a non-looping outro).
        if (start < 0 || start >= total) return;
        _loopStart = start;
        long len = ReadFrameTag("LOOPLENGTH");
        long end = len > 0 ? start + len : ReadFrameTag("LOOPEND");
        if (end > start) _loopEnd = Math.Min(end, total);
    }

    /// <summary>Parses a non-negative integer Vorbis comment; returns -1 if absent or malformed.</summary>
    private long ReadFrameTag(string key) =>
        long.TryParse(_reader!.Tags.GetTagSingle(key, false), out var v) && v >= 0 ? v : -1;

    private void OnBufferNeeded(object? sender, EventArgs e) => SubmitChunk();

    private void SubmitChunk()
    {
        if (_reader is null || _sfx is null) return;
        int read = FillLooping(_floatBuf);
        if (read == 0) return;
        read -= read % _channels; // keep the tail frame-aligned so L/R never desync
        int bytes = 0;
        for (int i = 0; i < read; i++)
        {
            short s = (short)Math.Clamp((int)MathF.Round(_floatBuf[i] * short.MaxValue),
                short.MinValue, short.MaxValue);
            _pcmBuf[bytes++] = (byte)s;
            _pcmBuf[bytes++] = (byte)(s >> 8);
        }
        _sfx.SubmitBuffer(_pcmBuf, 0, bytes);
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> completely, rewinding to <see cref="_loopStart"/> whenever the
    /// decoder reaches the loop end. Wrapping within a single buffer is what makes the loop seamless.
    /// </summary>
    private int FillLooping(float[] buffer)
    {
        if (_loopEnd <= _loopStart) return 0; // empty/degenerate file — nothing to play
        int total = 0;
        while (total < buffer.Length)
        {
            long framesLeft = _loopEnd - _reader!.SamplePosition;
            if (framesLeft <= 0)
            {
                _reader.SeekTo(_loopStart);
                continue;
            }
            // Read no further than the loop end; ReadSamples counts interleaved values, positions are
            // per-channel frames, hence the *_channels.
            int want = (int)Math.Min(buffer.Length - total, framesLeft * _channels);
            int got = _reader.ReadSamples(buffer, total, want);
            if (got == 0)
            {
                _reader.SeekTo(_loopStart);
                continue;
            }  // reader hit EOF early (bad tag)
            total += got;
        }
        return total;
    }

    public void Stop()
    {
        if (_sfx is not null)
        {
            _sfx.BufferNeeded -= OnBufferNeeded;
            _sfx.Stop();
            _sfx.Dispose();
            _sfx = null;
        }
        _reader?.Dispose();
        _reader = null;
        _currentPath = null;
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_sfx is not null) _sfx.Volume = _volume;
        }
    }

    public void Dispose() => Stop();
}
