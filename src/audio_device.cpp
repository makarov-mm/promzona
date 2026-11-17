#include "promzona/audio.hpp"
#include <SDL3/SDL.h>
#include <array>
#include <format>

namespace Promzona
{
struct AudioDevice::Impl
{
    AudioMixer &mixer;
    SDL_AudioStream *stream{};
    bool initialized{};
    std::array<float, 1024> mixed{};
    std::string failure;

    explicit Impl(AudioMixer &m) : mixer(m)
    {
        if (!SDL_InitSubSystem(SDL_INIT_AUDIO))
        {
            failure = std::format("No audio: {}", SDL_GetError());
            return;
        }
        initialized = true;
        const SDL_AudioSpec spec{SDL_AUDIO_F32, 2, AudioMixer::sample_rate};
        stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, &Impl::feed, this);
        if (!stream)
        {
            failure = std::format("No stereo audio output: {}", SDL_GetError());
            return;
        }
        SDL_ResumeAudioStreamDevice(stream);
    }

    ~Impl()
    {
        // Destroying the stream stops the callback before the mixer goes away.
        if (stream)
            SDL_DestroyAudioStream(stream);
        if (initialized)
            SDL_QuitSubSystem(SDL_INIT_AUDIO);
    }

    // Runs on the audio thread whenever the device needs more interleaved stereo samples.
    static void SDLCALL feed(void *self, SDL_AudioStream *stream, int needed, int)
    {
        auto &impl = *static_cast<Impl *>(self);
        while (needed > 0)
        {
            impl.mixer.mix(impl.mixed);
            for (float &sample : impl.mixed)
                sample = clamp(sample, -1.f, 1.f);
            const int bytes = int(sizeof(impl.mixed));
            SDL_PutAudioStreamData(stream, impl.mixed.data(), bytes);
            needed -= bytes;
        }
    }
};

AudioDevice::AudioDevice(AudioMixer &mixer) : impl_(std::make_unique<Impl>(mixer)) {}
AudioDevice::~AudioDevice() = default;
const std::string &AudioDevice::error() const
{
    return impl_->failure;
}
} // namespace Promzona
