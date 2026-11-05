namespace MakarovPhysics.Base;

public sealed class AppPaths
{
    public required string AppDirectory { get; init; }

    /// <summary>
    /// A path from a catalogue file, confined to a root.
    ///
    /// Carousel.xml and Product.xml name DLLs, textures and folders, and those
    /// values went straight into Path.Combine. Path.Combine drops the root the
    /// moment the second part is rooted - "C:\x.dll", "\\server\share\x.dll" -
    /// and "..\..\x.dll" walks out of it, after which ModuleLoader would
    /// Assembly.LoadFrom whatever the file said. The catalogue is a trusted
    /// install today, so nothing exploited it; the day a mod pack or a manifest
    /// from elsewhere arrives, this is the line that holds (external audit
    /// R17-08, 01.09).
    ///
    /// Returns the full path, or null when the value is rooted, UNC, or resolves
    /// outside <paramref name="root"/>. A null is a catalogue error to report by
    /// name, never a file to open.
    /// </summary>
    public static string? Under(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        string value = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(value) || value.StartsWith("\\\\") || value.StartsWith("//")) return null;
        if (value.Length > 1 && value[1] == ':') return null; // "C:x.dll" - drive-relative, rooted on Windows only
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full = Path.GetFullPath(Path.Combine(fullRoot, value));
        return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? full : null;
    }

    /// <summary>The same, throwing with the offending value named.</summary>
    public static string UnderOrThrow(string root, string relative, string what) => Under(root, relative) ?? throw new InvalidDataException($"{what}: path '{relative}' is not inside the application directory");
    public string DataDirectory => Path.Combine(AppDirectory, "Data");
    public string LanguagesDirectory => Path.Combine(AppDirectory, "Languages");

    /// <summary>Texture root: module previews, tool icons, sandbox materials.</summary>
    public string TexturesDirectory => Path.Combine(AppDirectory, "Textures");

    /// <summary>Loaded 3D models (.glb) shipped beside the executable.</summary>
    public string ModelsDirectory => Path.Combine(AppDirectory, "Models");

    /// <summary>Fonts shipped beside the executable: the Noto Sans SC/JP/KR faces
    /// the Chinese, Japanese and Korean UI draws with, plus their licence. The
    /// UI loads them privately; nothing is installed on the machine.</summary>
    public string FontsDirectory => Path.Combine(AppDirectory, "Fonts");

    /// <summary>Tool and action icons, 64x64 RGBA PNG.</summary>
    public string IconsDirectory => Path.Combine(TexturesDirectory, "Icons");

    /// <summary>Material maps for the sandbox bodies, albedo and bump pairs.</summary>
    public string SandboxTexturesDirectory => Path.Combine(TexturesDirectory, "Sandbox");

    /// <summary>The bench wood: one seamless albedo/bump pair shared by every
    /// module that stands its apparatus on a board. Its own folder rather than
    /// a corner of Sandbox, because the sandbox set is keyed by the physics
    /// materials of that module and this is not one of them.</summary>
    public string WoodTexturesDirectory => Path.Combine(TexturesDirectory, "Wood");

    /// <summary>Plaster for the wall behind a bench.</summary>
    public string WallTexturesDirectory => Path.Combine(TexturesDirectory, "Wall");

    /// <summary>Plank floor for scenes that need a room rather than a void.</summary>
    public string FloorTexturesDirectory => Path.Combine(TexturesDirectory, "Floor");

    /// <summary>Woven fabric, for the cloth sheet itself.</summary>
    public string ClothTexturesDirectory => Path.Combine(TexturesDirectory, "Cloth");

    /// <summary>Steel plate for structural parts - frames, rails, base plates.</summary>
    public string MetalTexturesDirectory => Path.Combine(TexturesDirectory, "Metal");

    /// <summary>Quarried stone: the albedo/bump pair worn by masses that are
    /// meant to read as HEAVY. A painted box says nothing about its weight;
    /// a block of rock says it before the label does.</summary>
    public string StoneTexturesDirectory => Path.Combine(TexturesDirectory, "Stone");

    /// <summary>Fired-clay maps for the Demolition bricks (Textures/Brick).</summary>
    public string BrickTexturesDirectory => Path.Combine(TexturesDirectory, "Brick");
    public string MainLanguagesDirectory => ShellStringsOverride.Length > 0 ? ShellStringsOverride : Path.Combine(LanguagesDirectory, "Main");
    public string ModuleLanguagesDirectory(string moduleId) => Path.Combine(LanguagesDirectory, "Modules", moduleId);

    /// <summary>Windows pilot translations, separate from the XML used by macOS.</summary>
    public string WindowsLanguagesDirectory
    {
        get
        {
            // Development shells point into the repository's Languages tree;
            // published shells use the product-tailored Main beside the exe.
            string? sourceRoot = ShellStringsOverride.Length > 0
                ? Directory.GetParent(Path.TrimEndingDirectorySeparator(ShellStringsOverride))?.Parent?.FullName : null;
            return Path.Combine(sourceRoot ?? AppDirectory, "WindowsLanguages");
        }
    }
    public string WindowsMainLanguagesDirectory => Path.Combine(WindowsLanguagesDirectory,
        ShellStringsOverride.Length > 0 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(ShellStringsOverride)) : "Main");
    public string WindowsModuleLanguagesDirectory(string moduleId) => Path.Combine(WindowsLanguagesDirectory, "Modules", moduleId);
    /// <summary>
    /// Set by the application when it has a catalogue of its own; empty means
    /// the packaged one beside the binary.
    ///
    /// A plain path and nothing more. What decides it lives in the app layer -
    /// this assembly does not know that catalogues can belong to anything.
    /// </summary>
    public string CatalogueOverride { get; init; } = "";

    /// <summary>
    /// Same idea for the shell text (window title, splash, carousel chrome):
    /// set when the application has its own strings, empty for the packaged
    /// Languages/Main.
    ///
    /// The pair exists because the catalogue and the shell text travel
    /// together and break together. A published package carries exactly one
    /// Languages\Main - publish put the right one there - but the three shells
    /// share one Build\ folder, so a run from the tree needs to be told which
    /// set is its own. The catalogue got that treatment on 30.08 and the
    /// strings did not, which is why MakarovMath.exe from Build\ was titled
    /// "Physics Programs Suite 2" (31.08).
    /// </summary>
    public string ShellStringsOverride { get; init; } = string.Empty;

    public string CarouselConfig => CatalogueOverride.Length > 0 ? CatalogueOverride : Path.Combine(DataDirectory, "Carousel.xml");

    /// <summary>The read-only default settings shipped with the app.</summary>
    public string DefaultSettingsFile => Path.Combine(DataDirectory, "Settings.xml");
}
