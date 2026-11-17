#include "promzona/assets.hpp"
#include "promzona/audio.hpp"
#include <fstream>
#include <iostream>
#include <exception>
#include <stdexcept>

// Use the production readers. This test does not create an OpenGL context.
int main(int argc, char **argv)
{
    if (argc != 2)
        return 2;
    unsigned meshes = 0, skins = 0, images = 0, failures = 0, embedded = 0, waves = 0;
    try
    {
        auto root = std::filesystem::path(argv[1]);
        for (const auto &entry : std::filesystem::recursive_directory_iterator(root))
        {
            if (!entry.is_regular_file())
                continue;
            auto extension = entry.path().extension().string();
            try
            {
                if (extension == ".glb")
                {
                    auto mesh = Promzona::load_glb(entry.path());
                    ++meshes;
                    if (!mesh.embedded_color.empty())
                    {
                        Promzona::decode_image(mesh.embedded_color);
                        ++embedded;
                    }
                }
                else if (extension == ".cityskin")
                {
                    Promzona::load_skin(entry.path());
                    ++skins;
                }
                else if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
                {
                    const auto image = Promzona::load_image(entry.path());
                    if (entry.path().filename() == "far-color.png")
                    {
                        // The old top-down atlases painted the sides of distant objects white.
                        const auto tile = image.width / 3;
                        if (!tile || image.width != tile * 3 || image.height != tile * 2)
                            throw std::runtime_error("Expected six directional views in the far atlas");
                        for (unsigned face = 0; face < 6; ++face)
                        {
                            int low = 765, high = 0;
                            for (unsigned y = tile / 4; y < tile * 3 / 4; ++y)
                                for (unsigned x = tile / 4; x < tile * 3 / 4; ++x)
                                {
                                    const auto px = x + face % 3 * tile, py = y + face / 3 * tile;
                                    const auto *rgb = &image.rgba[4 * (size_t(py) * image.width + px)];
                                    int sum = rgb[0] + rgb[1] + rgb[2];
                                    low = std::min(low, sum);
                                    high = std::max(high, sum);
                                }
                            if (low == 765 && high == 765)
                                throw std::runtime_error("Empty white view in far atlas");
                        }
                    }
                    ++images;
                }
                else if (extension == ".wav")
                {
                    auto samples = Promzona::read_wave(entry.path());
                    if (samples.empty())
                        throw std::runtime_error("Empty WAV");
                    ++waves;
                }
            }
            catch (const std::exception &e)
            {
                ++failures;
                std::cerr << entry.path().lexically_relative(root).string() << ": " << e.what() << '\n';
            }
        }
        if (meshes == 0 || skins == 0 || images == 0)
            ++failures;
        // A malformed import must fail explicitly, never read beyond its data.
        auto bad = std::filesystem::current_path() / "invalid-test.glb";
        {
            std::ofstream out(bad, std::ios::binary);
            out << "glTF";
        }
        bool rejected = false;
        try
        {
            Promzona::load_glb(bad);
        }
        catch (...)
        {
            rejected = true;
        }
        if (!rejected)
            ++failures;
        std::filesystem::remove(bad);
    }
    catch (const std::exception &e)
    {
        ++failures;
        std::cerr << e.what() << '\n';
    }
    std::cout << meshes << " GLB, " << skins << " skins, " << images << " images, " << embedded
              << " embedded textures, " << waves << " WAV; " << failures << " failures\n";
    return failures ? 1 : 0;
}
