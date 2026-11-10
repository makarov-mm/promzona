#include "promzona/audio.hpp"
#include <fstream>
namespace pz
{
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
    clips_.emplace(std::move(name), std::move(samples));
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
    trim();
    Voice v;
    v.samples = &clip->second;
    v.ui = event.ui;
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
        auto pa = clips_.find("peace_pa");
        if (pa != clips_.end())
        {
            const bool active = world.job.kind == JobKind::broadcast && world.job.route.enabled &&
                                world.controlledCar == world.job.actor;
            std::erase_if(voices_, [&](const Voice &v) {return !active && v.samples == &pa->second;});
            if (active) for (auto &v: voices_) if(v.samples==&pa->second) v.source=world.position();
        }
        for (auto &v : voices_)
        {
            v.touched = false;
            if (!v.loop)
                v.pan = spatialAudio(v.ui ? listener_ : v.source, listener_, heading_, v.gain);
        }
        std::vector<const Car *> engines;
        for (auto &c : world.cars)
            if (!c.destroyed && !c.falling && (c.body.p - listener_).length2() < 55 * 55)
                engines.push_back(&c);
        std::ranges::sort(engines, [&](auto a, auto b)
                          { return (a->body.p - listener_).length2() < (b->body.p - listener_).length2(); });
        if (engines.size() > 10)
            engines.resize(10);
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
        for (auto c : engines)
        {
            double gain = world.controlledCar >= 0 && c == &world.cars[world.controlledCar] ? .38 : .24;
            if(c->responding && c->kind!=12 && c->kind!=13 && c->kind!=14)
                loop(0x10000+int(c->id),"siren",spatialAudio(c->body.p, listener_, heading_, .26),1);
            loop(int(c->id), "engine", spatialAudio(c->body.p, listener_, heading_, gain),
                 .75 + std::min(1.5, c->body.v.length() * .05));
        }
        if (world.rain > .01)
            loop(-2, "rain", {float(world.rain * .28), float(world.rain * .28)}, 1);
        std::erase_if(voices_, [](auto &v) { return v.loop && !v.touched; });
    }
    for (auto &event : world.sounds)
        if (event.name != "peace_pa" || (world.job.kind == JobKind::broadcast && world.job.route.enabled && world.controlledCar==world.job.actor))
            play(event);
    world.sounds.clear();
}
void AudioMixer::pause(bool paused)
{
    std::lock_guard lock(mutex_);
    playing_ = !paused;
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
    for (auto &sample : stereo)
        sample = std::tanh(sample * .65f * .8f);
}
} // namespace pz
