#include "promzona/audio.hpp"
#include <fstream>
namespace pz
{
AudioLevels AudioLevels::load(const std::filesystem::path &path)
{
    AudioLevels levels;
    std::ifstream file(path);
    if (!(file >> levels.master >> levels.radio) || levels.master < 0 || levels.master > 100 ||
        levels.radio < 0 || levels.radio > 50)
        return {};
    return levels;
}
bool AudioLevels::save(const std::filesystem::path &path) const
{
    std::ofstream file(path);
    return bool(file << master << ' ' << radio << '\n' << std::flush);
}
// Port of CityAudio.Spatial: right on screen is forward cross world-up.
StereoGain spatialAudio(Vec2 source, Vec2 listener, double heading, double gain)
{
    auto d = source - listener;
    double distance = d.length();
    double pan =
        clamp((-d.x * std::cos(heading) + d.z * std::sin(heading)) / std::max(3., distance), -1., 1.);
    double level = gain / (1 + distance * .10 + distance * distance * .0015);
    return {float(level * std::sqrt((1 - pan) * .5)), float(level * std::sqrt((1 + pan) * .5))};
}
std::vector<float> readWave(const std::filesystem::path &path)
{
    auto length = std::filesystem::file_size(path);
    if (length < 44 || length > 64 * 1024 * 1024)
        throw std::runtime_error("WAV size limit");
    std::vector<unsigned char> bytes(static_cast<size_t>(length));
    std::ifstream f(path, std::ios::binary);
    if (!f.read(reinterpret_cast<char *>(bytes.data()), std::streamsize(length)))
        throw std::runtime_error("WAV read failed");
    auto u16 = [&](size_t i)
    {
        if (i + 2 > bytes.size())
            throw std::runtime_error("Truncated WAV");
        return unsigned(bytes[i]) | unsigned(bytes[i + 1]) << 8;
    };
    auto u32 = [&](size_t i) { return std::uint32_t(u16(i)) | std::uint32_t(u16(i + 2)) << 16; };
    auto tag = [&](size_t i, const char *s)
    { return i + 4 <= bytes.size() && std::equal(bytes.begin() + i, bytes.begin() + i + 4, s); };
    if (!tag(0, "RIFF") || !tag(8, "WAVE") || u32(4) + 8ull > length)
        throw std::runtime_error("Invalid WAV header");
    unsigned rate = 0;
    size_t data = 0, size = 0;
    for (size_t i = 12; i + 8 <= bytes.size();)
    {
        size_t n = u32(i + 4), p = i + 8;
        if (n > bytes.size() - p)
            throw std::runtime_error("Truncated WAV chunk");
        if (tag(i, "fmt "))
        {
            if (n < 16 || u16(p) != 1 || u16(p + 2) != 1 || u16(p + 14) != 16 || u16(p + 12) != 2)
                throw std::runtime_error("Expected mono PCM16 WAV");
            rate = u32(p + 4);
        }
        if (tag(i, "data"))
        {
            data = p;
            size = n;
        }
        i = p + n + (n & 1);
    }
    if (rate < 8000 || rate > 192000 || size < 2 || size % 2)
        throw std::runtime_error("Invalid WAV format");
    size_t count = size / 2, output = count * AudioMixer::sampleRate / rate;
    std::vector<float> samples(output);
    for (size_t i = 0; i < output; i++)
    {
        size_t at = std::min(count - 1, i * rate / AudioMixer::sampleRate);
        samples[i] = static_cast<std::int16_t>(u16(data + at * 2)) / 32768.f;
    }
    return samples;
}
AudioMixer::AudioMixer(const std::filesystem::path &directory)
{
    voices_.reserve(maxVoices);
    if (!directory.empty() && std::filesystem::is_directory(directory))
        for (auto &entry : std::filesystem::directory_iterator(directory))
            if (entry.path().extension() == ".wav")
                addClip(entry.path().stem().string(), readWave(entry.path()));
}
void AudioMixer::addClip(std::string name, std::vector<float> samples)
{
    if (samples.empty() || std::ranges::any_of(samples, [](float f) { return !std::isfinite(f); }))
        throw std::invalid_argument("Empty or invalid audio clip");
    std::lock_guard lock(mutex_);
    if (clips_.contains(name))
        throw std::invalid_argument("Cannot replace an active audio clip");
    auto it = clips_.emplace(std::move(name), std::move(samples)).first;
    if (it->first == "radio0")
        tracks_[0] = &it->second;
    if (it->first == "radio1")
        tracks_[1] = &it->second;
}
void AudioMixer::trim()
{
    while (voices_.size() >= maxVoices)
    {
        auto it = std::find_if(voices_.begin(), voices_.end(), [](auto &v) { return !v.loop; });
        if (it == voices_.end())
            it = voices_.begin();
        voices_.erase(it);
    }
}
void AudioMixer::play(const SoundEvent &event)
{
    std::lock_guard lock(mutex_);
    auto clip = clips_.find(event.name);
    if (clip == clips_.end())
        return;
    if (event.speaker >= 0)
        std::erase_if(voices_, [&](const Voice &v) { return v.speaker == event.speaker; });
    trim();
    Voice v;
    v.samples = &clip->second;
    v.ui = event.ui;
    v.speaker = event.speaker;
    v.source = event.source;
    v.gain = event.gain;
    v.pan = spatialAudio(v.ui ? listener_ : v.source, listener_, heading_, v.gain);
    voices_.push_back(v);
}
void AudioMixer::update(World &world, double cameraHeading)
{
    {
        std::lock_guard lock(mutex_);
        listener_ = world.position();
        heading_ = cameraHeading;
        std::erase_if(voices_,
                      [&](Voice &voice)
                      {
                          if (voice.speaker < 0)
                              return false;
                          if (size_t(voice.speaker) >= world.people.size() ||
                              world.people[voice.speaker].dead)
                              return true;
                          voice.source = world.people[voice.speaker].body.p;
                          return false;
                      });
        auto pa = clips_.find("peace_pa");
        if (pa != clips_.end())
        {
            const bool active = world.job.kind == JobKind::broadcast && world.job.route.enabled &&
                                world.controlledCar == world.job.actor;
            std::erase_if(voices_, [&](const Voice &v) { return !active && v.samples == &pa->second; });
            if (active)
                for (auto &v : voices_)
                    if (v.samples == &pa->second)
                        v.source = world.position();
        }
        for (auto &v : voices_)
        {
            v.touched = false;
            if (!v.loop)
                v.pan = spatialAudio(v.ui ? listener_ : v.source, listener_, heading_, v.gain);
        }
        auto loop = [&](int id, std::string_view name, StereoGain pan, double rate)
        {
            auto clip = clips_.find(name);
            if (clip == clips_.end())
                return;
            auto voice =
                std::find_if(voices_.begin(), voices_.end(), [&](auto &v) { return v.loop && v.id == id; });
            if (voice == voices_.end())
            {
                trim();
                Voice v;
                v.samples = &clip->second;
                v.id = id;
                v.loop = true;
                voices_.push_back(v);
                voice = voices_.end() - 1;
            }
            voice->pan = pan;
            voice->rate = rate;
            voice->touched = true;
        };
        // Keep the nearest motors, but an emergency vehicle can be heard from the next street.
        std::array<const Car *, 10> engines{};
        std::array<double, 10> distances{};
        size_t count = 0;
        for (const auto &c : world.cars)
        {
            if (c.destroyed || c.falling)
                continue;
            double distance = (c.body.p - listener_).length2();
            if (distance >= 100 * 100)
                continue;
            const auto *medical = c.kind == 4 ? world.ambulanceCall(c.id) : nullptr;
            if ((c.responding && c.kind != 12 && c.kind != 13 && c.kind != 14) ||
                (medical && medical->stage == World::RescueStage::driving && !medical->braking))
                loop(0x10000 + int(c.id), "siren", spatialAudio(c.body.p, listener_, heading_, .26), 1);
            if (distance >= 55 * 55 || (count == engines.size() && distance >= distances.back()))
                continue;
            if (count < engines.size())
                ++count;
            size_t at = count - 1;
            while (at && distance < distances[at - 1])
            {
                distances[at] = distances[at - 1];
                engines[at] = engines[at - 1];
                --at;
            }
            distances[at] = distance;
            engines[at] = &c;
        }
        for (size_t i = 0; i < count; ++i)
        {
            const auto &c = *engines[i];
            double gain = int(c.id) == world.controlledCar ? .38 : .24;
            loop(int(c.id), "engine", spatialAudio(c.body.p, listener_, heading_, gain),
                 .75 + std::min(1.5, c.body.v.length() * .05));
        }
        if (world.rain > .01)
            loop(-2, "rain", {float(world.rain * .28), float(world.rain * .28)}, 1);
        std::erase_if(voices_, [](auto &v) { return v.loop && !v.touched; });
    }
    for (auto &event : world.sounds)
        if ((event.speaker < 0 ||
             (size_t(event.speaker) < world.people.size() && !world.people[event.speaker].dead)) &&
            (event.name != "peace_pa" || (world.job.kind == JobKind::broadcast && world.job.route.enabled &&
                                          world.controlledCar == world.job.actor)))
            play(event);
    world.sounds.clear();
}
void AudioMixer::setLevels(AudioLevels levels)
{
    std::lock_guard lock(mutex_);
    levels_.master = std::clamp(levels.master, 0, 100);
    levels_.radio = std::clamp(levels.radio, 0, 50);
}
void AudioMixer::nextTrack()
{
    std::lock_guard lock(mutex_);
    track_ = (track_ + 1) % tracks_.size();
    trackCursor_ = 0;
}
void AudioMixer::pause(bool paused, bool worldPaused)
{
    std::lock_guard lock(mutex_);
    playing_ = !paused;
    worldPaused_ = worldPaused;
}
void AudioMixer::clear()
{
    std::lock_guard lock(mutex_);
    voices_.clear();
}
size_t AudioMixer::voiceCount()
{
    std::lock_guard lock(mutex_);
    return voices_.size();
}
void AudioMixer::mix(std::span<float> stereo)
{
    if (stereo.size() % 2)
        throw std::invalid_argument("Stereo buffer must contain pairs");
    std::lock_guard lock(mutex_);
    std::fill(stereo.begin(), stereo.end(), 0.f);
    if (!playing_)
        return;
    if (!worldPaused_)
    {
        for (auto &v : voices_)
            for (size_t k = 0; k < stereo.size(); k += 2)
            {
                if (v.cursor >= v.samples->size())
                {
                    if (!v.loop)
                        break;
                    v.cursor = std::fmod(v.cursor, double(v.samples->size()));
                }
                auto at = static_cast<size_t>(v.cursor);
                float s = (*v.samples)[at];
                stereo[k] += s * v.pan.left;
                stereo[k + 1] += s * v.pan.right;
                v.cursor += v.rate;
            }
        std::erase_if(voices_, [](auto &v) { return !v.loop && v.cursor >= v.samples->size(); });
    }
    if (levels_.radio && (tracks_[0] || tracks_[1]))
    {
        size_t frame = 0;
        while (frame < stereo.size() / 2)
        {
            if (!tracks_[track_])
                track_ = 1 - track_;
            const auto &music = *tracks_[track_];
            size_t n = std::min(music.size() - trackCursor_, stereo.size() / 2 - frame);
            for (size_t i = 0; i < n; ++i)
            {
                float value = music[trackCursor_ + i] * (levels_.radio * .01f);
                stereo[2 * (frame + i)] += value;
                stereo[2 * (frame + i) + 1] += value;
            }
            frame += n;
            trackCursor_ += n;
            if (trackCursor_ == music.size())
            {
                track_ = 1 - track_;
                trackCursor_ = 0;
            }
        }
    }
    for (auto &sample : stereo)
        sample = std::tanh(sample * (levels_.master * .008f));
}
} // namespace pz
