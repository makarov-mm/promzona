#pragma once
#include "world.hpp"
#include <map>
#include <memory>
#include <mutex>

namespace Promzona
{
    struct AudioLevels
    {
        int master{65}, radio{20};
        static AudioLevels load(const std::filesystem::path &path);
        [[nodiscard]] bool save(const std::filesystem::path &path) const;
    };

    struct StereoGain
    {
        float left{}, right{};
    };

    StereoGain spatial_audio(Vec2 source, Vec2 listener, double heading, double gain);
    std::vector<float> read_wave(const std::filesystem::path &path);

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
        bool playing_{}, worldPaused_{};
        AudioLevels levels_;
        std::array<const std::vector<float> *, 2> tracks_{};
        size_t track_{}, track_cursor_{};
        Vec2 listener_;
        double heading_{};
        void trim();

      public:
        static constexpr int sample_rate = 22050, max_voices = 32;
        explicit AudioMixer(const std::filesystem::path &audioDirectory = {});
        void add_clip(std::string name, std::vector<float> samples);
        void update(World &world, double camera_heading);
        void play(const SoundEvent &event);
        void pause(bool paused, bool world_paused = false);
        void set_levels(AudioLevels levels);
        void next_track();
        void clear();
        void mix(std::span<float> stereo);
        size_t voice_count();
        size_t clip_count() const { return clips_.size(); }
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
}
