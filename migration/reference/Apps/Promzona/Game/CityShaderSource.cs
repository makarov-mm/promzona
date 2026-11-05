using MakarovPhysics.Renderer;
using System.Text;

namespace Promzona.Game;

internal static class CityShaderSource
{
    internal static string ReadGeometry(string name)
    {
        string source = Read("Geometry." + name);
        // glslang resolves sibling includes from disk. OpenGL 3.3 drivers receive
        // the same common code expanded from embedded resources before compilation.
        const string extension = "#extension GL_GOOGLE_include_directive : require";
        if (!source.Contains(extension, StringComparison.Ordinal)) return source;

        var expanded = new StringBuilder(source.Length);
        using var lines = new StringReader(source);
        while (lines.ReadLine() is { } line)
        {
            string directive = line.Trim();
            if (directive == extension) continue;
            if (directive.StartsWith("#include ", StringComparison.Ordinal))
            {
                string operand = directive["#include ".Length..].Trim();
                if (operand.Length < 3 || operand[0] != '"' || operand[^1] != '"')
                    throw new InvalidDataException($"Invalid shader include in {name}: {line}");
                // Includes are shared leaf files in the same shader directory.
                string chunk = operand[1..^1] switch
                {
                    "Renderer/Shader.PbrLightUniforms.glsl" => DefaultShaders.PbrLightUniforms,
                    "Renderer/Shader.PbrFunctions.glsl" => DefaultShaders.PbrFunctions,
                    "Renderer/OutdoorAtmosphere.Glsl.glsl" => OutdoorAtmosphere.Glsl,
                    "Renderer/WorkshopSurface.WorkshopLightingGlsl.glsl" => WorkshopSurface.WorkshopLightingGlsl,
                    var file => Read("Geometry." + file)
                };
                if (chunk.Contains("#include ", StringComparison.Ordinal))
                    throw new InvalidDataException($"Nested shader include in {operand} ({name})");
                expanded.AppendLine(chunk);
            }
            else expanded.AppendLine(line);
        }
        return expanded.ToString();
    }

    internal static string Read(string name)
    {
        string resource = "Promzona.Shaders." + name;
        using var stream = typeof(CityShaderSource).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Missing shader resource: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
