using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Promzona.Game;
namespace Promzona;

/// <summary>Game-only, bounded stereo mixer. No sound objects retain actors.</summary>
public sealed class CityAudio : ISampleProvider, IDisposable
{
    private sealed class Voice
    {
        public float[] Samples = [];
        public double Cursor, Rate = 1;
        public float Left, Right;
        public bool Loop, Ui;
        public int Id, SpeakerId = -1, Revision;
        public CityPoint Source;
        public double Gain;
    }

    private readonly Dictionary<string, float[]> _clips = [];
    private readonly List<Voice> _voices = [];
    private readonly Dictionary<int, (CityPoint Position, int Revision)> _speakers = [];
    private CityTraffic? _world;
    private int _respawn = -1;
    private readonly object _gate = new();
    private WasapiOut? _output;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(22050, 2);
    public float Volume = .65f, MusicVolume = .20f;
    public bool Playing, WorldPlaying = true;
    private int _track;
    private double _musicCursor;

    public void NextTrack()
    {
        lock (_gate)
        {
            _track = (_track + 1) % 2;
            _musicCursor = 0;
        }
    }

    public string? Error { get; private set; }

    public int VoiceCount
    {
        get
        {
            lock (_gate) return _voices.Count;
        }
    }

    public CityAudio(string directory, bool device = true)
    {
        foreach (string file in Directory.Exists(Path.Combine(directory, "Audio")) ? Directory.GetFiles(Path.Combine(directory, "Audio"), "*.wav") : [])
        {
            // One unreadable or empty file must not take the whole game down: the
            // first frame constructs this mixer on the render thread, and an
            // exception there ends in the fault screen. An empty clip would also
            // divide by zero in Read (cursor % length) on the WASAPI thread.
            try
            {
                using var r = new WaveFileReader(file);
                if (r.WaveFormat.Encoding != WaveFormatEncoding.Pcm || r.WaveFormat.Channels != 1 || r.WaveFormat.BitsPerSample != 16) continue;
                byte[] b = new byte[r.Length];
                r.ReadExactly(b);
                float[] data = new float[b.Length / 2];

                for (int k = 0; k < data.Length; k++)
                {
                    data[k] = BitConverter.ToInt16(b, k * 2) / 32768f;
                }

                if (r.WaveFormat.SampleRate != 22050)
                {
                    int count = (int)(data.Length * 22050d / r.WaveFormat.SampleRate);
                    float[] resampled = new float[count];

                    for (int i = 0; i < count; i++)
                    {
                        resampled[i] = data[Math.Min(data.Length - 1, (int)(i * r.WaveFormat.SampleRate / 22050d))];
                    }

                    data = resampled;
                }

                if (data.Length == 0) continue;
                _clips[Path.GetFileNameWithoutExtension(file)] = data;
            }
            catch (Exception ex)
            {
                Error = $"{Path.GetFileName(file)}: {ex.Message}";
                System.Diagnostics.Trace.WriteLine($"Audio clip {file}: {ex.Message}");
            }
        }

        if (device)
        {
            try
            {
                _output = new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, false, 80);
                _output.Init(new SampleToWaveProvider(this));
                _output.Play();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _output?.Dispose();
                _output = null;
            }
        }
    }

    // Right on screen is forward cross world-up: (-cos heading, sin heading).
    // Listener stays at the player, orientation follows the smoothed camera.
    public static (float Left, float Right) Spatial(CityPoint source, CityPoint listener, double heading, double gain)
    {
        CityPoint d = source - listener;
        double distance = d.Length, pan = Math.Clamp((-d.X * Math.Cos(heading) + d.Z * Math.Sin(heading)) / Math.Max(3, distance), -1, 1);
        double level = gain / (1 + distance * .10 + distance * distance * .0015);
        return ((float)(level * Math.Sqrt((1 - pan) * .5)), (float)(level * Math.Sqrt((1 + pan) * .5)));
    }

    public void Update(CityTraffic t, double cameraHeading, double rain)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_world, t) || _respawn != t.Respawns)
            {
                _voices.Clear();
                _speakers.Clear();
                _world = t;
                _respawn = t.Respawns;
            }

            CityPoint listener = t.PlayerPosition;

            // Only live, upright nearby speakers may start or continue a phrase.
            // Revision also invalidates queued speech after a nonfatal impact/revival.
            _speakers.Clear();

            foreach (CityPedestrian p in t.Pedestrians.Where(p => p.CanSpeak && (p.Position - listener).Length < 100))
            {
                _speakers[p.Id] = (p.Position, p.VoiceRevision);
            }

            if(t.BroadcastAudible && t.BroadcastCar is {} broadcast)
                _speakers[CityTraffic.BroadcastSpeakerId]=(broadcast.Position,0);

            _voices.RemoveAll(v => v.SpeakerId >= 0 && (!_speakers.TryGetValue(v.SpeakerId, out var state) || state.Revision != v.Revision));

            foreach (Voice v in _voices.Where(v => v.SpeakerId >= 0))
            {
                v.Source = _speakers[v.SpeakerId].Position;
            }

            foreach (var v in _voices)
            {
                if (!v.Loop)
                {
                    (float Left, float Right) gains = Spatial(v.Ui ? listener : v.Source, listener, cameraHeading, v.Gain);
                    v.Left = gains.Left;
                    v.Right = gains.Right;
                }
            }

            CityCar[] engines =
            [
                .. t.Cars
                    .Where(c => c is { Destroyed: false, Falling: false } && (c.Position - listener).Length < 55)
                    .OrderBy(c => (c.Position - listener).Length).Take(10)
            ];

            _voices.RemoveAll(v => v is { Loop: true, Id: >= 0 } && engines.All(c => c.Id != v.Id));

            foreach (CityCar car in engines)
            {
                Voice? v = _voices.Find(v => v.Loop && v.Id == car.Id);

                if (v is null && _clips.TryGetValue("engine", out float[]? clip))
                {
                    v = new Voice
                    {
                        Samples = clip, 
                        Loop = true, 
                        Id = car.Id
                    };

                    _voices.Add(v);
                }

                if (v is null) continue;
                var gains = Spatial(car.Position, listener, cameraHeading, t.PlayerControls(car) ? .38 : .24);
                v.Left = gains.Left;
                v.Right = gains.Right;
                v.Rate = .75 + Math.Min(1.5, Math.Abs(car.Speed) * .05);
            }

            CityCar?[] responders =
            [
                .. t.Services
                    .Select(m => t.Cars.Find(c => c.Id == m.CarId))
                    .Where(c => c is not null && !c.Destroyed && (c.Position - listener).Length < 100)
            ];

            _voices.RemoveAll(v => v is { Loop: true, Id: <= -1000 } && responders.All(c => -1000 - c!.Id != v.Id));

            foreach (var c in responders)
            {
                int id = -1000 - c!.Id;
                Voice? v = _voices.Find(v => v.Loop && v.Id == id);

                if (v is null && _clips.TryGetValue("siren", out var clip))
                {
                    v = new Voice
                    {
                        Samples = clip, 
                        Loop = true, 
                        Id = id
                    };

                    _voices.Add(v);
                }

                if (v is null) continue;
                (float Left, float Right) pan = Spatial(c.Position, listener, cameraHeading, .32);
                v.Left = pan.Left;
                v.Right = pan.Right;
            }

            Voice? weather = _voices.Find(v => v is { Loop: true, Id: -2 });

            if (rain > .01 && weather is null && _clips.TryGetValue("rain", out var r))
            {
                weather = new Voice
                {
                    Samples = r, 
                    Loop = true, 
                    Id = -2
                };

                _voices.Add(weather);
            }

            weather?.Left = weather.Right = (float)rain * .28f;

            while (_voices.Count > 32)
            {
                int remove = _voices.FindIndex(v => !v.Loop);
                if (remove < 0) break;
                _voices.RemoveAt(remove);
            }

            while (t.Sounds.Count > 0)
            {
                CitySound e = t.Sounds.Dequeue();

                if (e.SpeakerId >= 0)
                {
                    if (!_speakers.TryGetValue(e.SpeakerId, out var speaker) || speaker.Revision != e.Revision)
                    {
                        continue;
                    }

                    _voices.RemoveAll(v => v.SpeakerId == e.SpeakerId);
                }

                string key = e.Name;
                string key1 = key;
                string[] variants =
                    [.. _clips.Keys.Where(k => k.StartsWith(key1 + "_", StringComparison.Ordinal)).OrderBy(k => k)];

                if (variants.Length > 0) key = variants[Math.Abs(e.Variant) % variants.Length];
                if (!_clips.TryGetValue(key, out var samples)) continue;

                if (_voices.Count >= 32)
                {
                    int i = _voices.FindIndex(v => !v.Loop);
                    if (i < 0) continue;
                    _voices.RemoveAt(i);
                }

                (float Left, float Right) pan = Spatial(e.Ui ? listener : e.Position, listener, cameraHeading, e.Gain);

                _voices.Add(new Voice
                {
                    Samples = samples,
                    Left = pan.Left,
                    Right = pan.Right,
                    Source = e.Position,
                    Gain = e.Gain,
                    SpeakerId = e.SpeakerId,
                    Revision = e.Revision,
                    Ui = e.Ui
                });
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        lock (_gate)
        {
            if (!Playing) return count;

            if (WorldPlaying)
            {
                for (int n = _voices.Count - 1; n >= 0; n--)
                {
                    Voice v = _voices[n];

                    if (v.Samples.Length == 0)
                    {
                        _voices.RemoveAt(n);
                        continue;
                    }

                    for (int k = 0; k + 1 < count; k += 2)
                    {
                        int i = (int)v.Cursor;

                        if (i >= v.Samples.Length)
                        {
                            if (!v.Loop)
                            {
                                _voices.RemoveAt(n);
                                break;
                            }

                            v.Cursor %= v.Samples.Length;
                            i = (int)v.Cursor;
                        }

                        float s = v.Samples[i];
                        buffer[offset + k] += s * v.Left;
                        buffer[offset + k + 1] += s * v.Right;
                        v.Cursor += v.Rate;
                    }
                }
            }

            if (MusicVolume > 0 && _clips.TryGetValue("radio" + _track, out float[]? music) && music.Length > 0)
            {
                for (int k = 0; k + 1 < count; k += 2)
                {
                    int at = (int)_musicCursor % music.Length;
                    float v = music[at] * MusicVolume;
                    buffer[offset + k] += v;
                    buffer[offset + k + 1] += v;
                    _musicCursor++;

                    if (_musicCursor >= music.Length)
                    {
                        _musicCursor = 0;
                        _track = (_track + 1) % 2;
                        break;
                    }
                }
            }

            for (int k = 0; k < count; k++)
            {
                buffer[offset + k] = MathF.Tanh(buffer[offset + k] * Volume * .8f);
            }
        }

        return count;
    }

    public void Dispose()
    {
        Playing = false;
        _output?.Stop();
        _output?.Dispose();
        _output = null;

        lock (_gate)
        {
            _voices.Clear();
            _speakers.Clear();
            _world = null;
        }
    }
}
