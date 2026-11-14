#include "promzona/audio.hpp"
#include <windows.h>
#include <mmsystem.h>
#include <thread>
#include <format>

namespace Promzona
{
struct AudioDevice::Impl
{
    HWAVEOUT output{};
    HANDLE ready{};
    std::jthread thread;
    struct Buffer
    {
        WAVEHDR header{};
        std::array<short, 1024> data{};
        bool prepared{};
    };
    std::array<Buffer, 4> buffers;
    std::string failure;
    explicit Impl(AudioMixer &mixer)
    {
        ready = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!ready)
        {
            failure = "Cannot create audio event";
            return;
        }
        WAVEFORMATEX format{WAVE_FORMAT_PCM, 2, AudioMixer::sampleRate, AudioMixer::sampleRate * 4, 4, 16, 0};
        auto result =
            waveOutOpen(&output, WAVE_MAPPER, &format, reinterpret_cast<DWORD_PTR>(ready), 0, CALLBACK_EVENT);
        if (result != MMSYSERR_NOERROR)
        {
            output = nullptr;
            failure = std::format("No stereo audio output: {}", result);
            return;
        }
        for (auto &b : buffers)
        {
            b.header.lpData = reinterpret_cast<char *>(b.data.data());
            b.header.dwBufferLength = DWORD(sizeof(b.data));
            if (waveOutPrepareHeader(output, &b.header, sizeof(WAVEHDR)) != MMSYSERR_NOERROR)
            {
                failure = "Cannot prepare audio buffers";
                return;
            }
            b.prepared = true;
        }
        thread = std::jthread(
            [&, this](std::stop_token stop)
            {
                std::array<float, 1024> mixed{};
                while (!stop.stop_requested())
                {
                    for (auto &b : buffers)
                        if (!(b.header.dwFlags & WHDR_INQUEUE))
                        {
                            mixer.mix(mixed);
                            for (size_t i = 0; i < mixed.size(); i++)
                                b.data[i] = short(clamp(mixed[i], -1.f, 1.f) * 32767);
                            if (waveOutWrite(output, &b.header, sizeof(WAVEHDR)) != MMSYSERR_NOERROR)
                                return;
                        }
                    WaitForSingleObject(ready, 10);
                }
            });
    }
    ~Impl()
    {
        if (thread.joinable())
        {
            thread.request_stop();
            SetEvent(ready);
            thread.join();
        }
        if (output)
        {
            waveOutReset(output);
            for (auto &b : buffers)
                if (b.prepared)
                    waveOutUnprepareHeader(output, &b.header, sizeof(WAVEHDR));
            waveOutClose(output);
        }
        if (ready)
            CloseHandle(ready);
    }
};
AudioDevice::AudioDevice(AudioMixer &mixer) : impl_(std::make_unique<Impl>(mixer)) {}
AudioDevice::~AudioDevice() = default;
const std::string &AudioDevice::error() const
{
    return impl_->failure;
}
} // namespace Promzona
