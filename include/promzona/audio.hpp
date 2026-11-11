#pragma once
#include "world.hpp"
#include <map>
#include <memory>
#include <mutex>
namespace pz
{
struct StereoGain
{
    float left{}, right{};
};
StereoGain spatialAudio(Vec2 source, Vec2 listener, double heading, double gain);
std::vector<float> readWave(const std::filesystem::path &path);
// All clips are immutable once voices refer to them. Audio callbacks own no game objects.
class AudioMixer
{
    struct Voice
    {
        const std::vector<float> *samples{};
        double cursor{}, rate{1};
        StereoGain pan;
        int id{}, speaker{-1};
        bool loop{}, touched{}, ui{};
        Vec2 source;
        double gain{1};
    };
    std::map<std::string, std::vector<float>, std::less<>> clips_;
    std::vector<Voice> voices_;
    std::mutex mutex_;
    bool playing_{};
    Vec2 listener_;
    double heading_{};
    void trim();

  public:
    static constexpr int sampleRate = 22050, maxVoices = 32;
    explicit AudioMixer(const std::filesystem::path &audioDirectory = {});
    void addClip(std::string name, std::vector<float> samples);
    void update(World &world, double cameraHeading);
    void play(const SoundEvent &event);
    void pause(bool paused);
    void clear();
    void mix(std::span<float> stereo);
    size_t voiceCount();
    size_t clipCount() const { return clips_.size(); }
};
class AudioDevice
{
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit AudioDevice(AudioMixer &mixer);
    ~AudioDevice();
    const std::string &error() const;
};
} // namespace pz
