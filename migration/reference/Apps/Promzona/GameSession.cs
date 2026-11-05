using Promzona.Game;
using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
using System.Text.Json;
namespace Promzona;

/// <summary>Owns CPU generation separately from the GL-thread scene. Saves preserve
/// the versioned world, including responders, traffic and damage, atomically.</summary>
public sealed partial class GameSession : IModuleContext, IDisposable
{
    public AppPaths Shared { get; }
    public Localization Loc { get; }
    public UiContext Ui { get; } = new();
    public InputState Input { get; } = new();
    public CityGame City => _city ?? throw new InvalidOperationException("No active city");
    private CityGame? _city;
    public bool HasCity => _city is not null;
    public GameScreen Screen { get; private set; } = GameScreen.Menu;
    public GenerationStatus Progress => Volatile.Read(ref _progress);
    private GenerationStatus _progress = new(0, "");
    public string? Error { get; private set; }
    private readonly string _saveDirectory;
    public int SelectedSaveSlot {get;private set;}=1;
    public int ActiveSaveSlot {get;private set;}=1;
    public string SavePath=>CitySaveSlots.PathFor(_saveDirectory,ActiveSaveSlot);
    public void SelectSaveSlot(int slot){CitySaveSlots.PathFor(_saveDirectory,slot);SelectedSaveSlot=slot;}
    public int ViewportWidth { get; private set; }
    public int ViewportHeight { get; private set; }
    public bool Active { get; set; } = true;
    public event Action? ExitRequested;
    public double LastUpdateMilliseconds, LastRenderMilliseconds, LastSimulationMilliseconds, LastAudioMilliseconds;
    private bool _initialized, _disposed, _pendingGpu;
    private bool _ru;
    private GameUiDraw UiDraw=>new(Ui.Draw,Settings.Language);
    public GameSettings Settings { get; }
    private readonly Texture?[] _weaponArt = new Texture?[7];
    private Texture? _menuImage, _menuTitle;
    private CityAudio? _audio;
    private int _seenRespawn;
    private CancellationTokenSource? _cancel;
    private Task<CityModel>? _generation;
    private CitySave? _restore;
    private double _saveClock;
    private long _savedUntil;

    // The world is captured on the render thread; the ~35 MB JSON of a full
    // city is serialized and written on the thread pool so the 120 s autosave
    // and the Esc menu no longer stall a frame for the better part of a second.
    private Task? _saveTask;
    private string? _saveError;
    private int _selection;
    private bool _contractsOpen;
    private bool _shopOpen;
    private int _shopSelection; // the market list over the running game

    public GameSession(string directory, string language, string? saveDirectory = null)
    {
        Settings = new GameSettings(language);
        language = Settings.Language;

        Shared = new AppPaths
        {
            AppDirectory = directory
        };

        Loc = Localization.LoadBest(Shared.MainLanguagesDirectory, language, Shared.WindowsMainLanguagesDirectory);

        _ru = language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) 
              ||
              language.StartsWith("uk", StringComparison.OrdinalIgnoreCase);

        _saveDirectory=Path.GetDirectoryName(DefaultSavePath(saveDirectory))!;
    }

    public static string DefaultSavePath(string? directory = null) => Path.Combine(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Promzona"), "save.json");

    /// <summary>Explicit import of a previous save. The source stays untouched,
    /// and an existing destination is never silently overwritten.</summary>
    public static void ImportSave(string source, string? directory = null)
    {
        string destination = DefaultSavePath(directory);

        if (File.Exists(destination))
        {
            throw new IOException("A Promzona save already exists. Back it up and move it before importing another save.");
        }

        ReadSave(source);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: false);
    }

    private static CitySave ReadSave(string path)
    {
        if (new FileInfo(path).Length > 90_000_000) throw new InvalidDataException("Save file is too large");
        string text = File.ReadAllText(path);
        using (var document = JsonDocument.Parse(text)) CityState.ValidateJson(document.RootElement);
        CitySave? save = JsonSerializer.Deserialize<CitySave>(text, CityState.Json);

        if (save is null || save.Format is < 1 or > 3 || !double.IsFinite(save.X) || !double.IsFinite(save.Z) ||
            !double.IsFinite(save.Heading) || !double.IsFinite(save.Hour)
            || Math.Abs(save.X) > 960 || Math.Abs(save.Z) > 960 ||
            save.Money < 0)
        {
            throw new InvalidDataException("Unsupported or invalid save file");
        }

        if (save.Format >= 2)
        {
            if (save.World is null)
            {
                throw new InvalidDataException("Missing world");
            }

            CityTraffic.ValidateWorld(save.World);
        }

        return save;
    }

    public T? GetService<T>() where T : class => null;

    public void Initialize(int width, int height, float scale)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        SetScale(scale);
        Ui.InitGL();
        _initialized = true;
        string menu = Path.Combine(Shared.AppDirectory, "Textures", "menu-city.png");

        if (File.Exists(menu))
        {
            using var bmp = new Bitmap(menu);
            _menuImage = new Texture(bmp, false);
        }

        string[] badges = ["fist", "pistol", "rifle", "launcher", "flame", "cannon", "mg"];
        for (int i = 0; i < badges.Length; i++)
        {
            using var image = new Bitmap(Path.Combine(Shared.AppDirectory, "Textures", "Hud", badges[i]+".png"));
            _weaponArt[i] = new Texture(image, false);
        }
        // The large wordmark uses its own high-resolution glyphs; enlarging the
        // small UI atlas made the title visibly blurry at desktop resolutions.
        using var title = new Bitmap(1024, 170);
        using (var graphics = Graphics.FromImage(title))
        {
            graphics.Clear(Color.Transparent);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Bahnschrift", 128, FontStyle.Bold, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(Color.FromArgb(228, 212, 174));
            graphics.DrawString("PROMZONA", font, ink, new RectangleF(0, 0, 1024, 170), new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            });
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            using var cut = new SolidBrush(Color.Transparent);
            graphics.FillRectangle(cut, 0, 88, 1024, 3);
        }

        _menuTitle = new Texture(title, false);
    }

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        ViewportWidth = width;
        ViewportHeight = height;
        SetScale(_displayScale);
        _city?.Resize(width, height);
    }

    private float _displayScale = 1;
    public void SetScale(float scale)
    {
        _displayScale = scale;
        // Keep the whole settings panel and both HUD corners inside small windows.
        float fit = Math.Min(ViewportWidth / 940f, ViewportHeight / 545f);
        // Average the two effective sizes so the change is visible on smaller displays too.
        float limit = Math.Max(.5f, fit);
        Ui.SetScale((Math.Min(scale, limit) + Math.Min(2 * scale, limit)) * .5f);
    }
    public bool CanLoad => File.Exists(CitySaveSlots.PathFor(_saveDirectory,SelectedSaveSlot)) || SelectedSaveSlot==ActiveSaveSlot && _saveTask is { IsCompleted: false };
    private void Report(double value, string stage) => Volatile.Write(ref _progress, new GenerationStatus(Math.Clamp(value, 0, 1), stage));
    public void NewGame(int? seed = null, int roadRevision = 4) => BeginGeneration(seed ?? System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue), null, roadRevision);

    public void LoadGame()
    {
        try
        {
            WaitForSave();
            CitySave save = ReadSave(CitySaveSlots.PathFor(_saveDirectory,SelectedSaveSlot));
            BeginGeneration(save.Seed, save);
        }
        catch (Exception ex)
        {
            Error = (_ru ? "Не удалось загрузить: " : "Could not load: ") + ex.Message;
        }
    }

    private void BeginGeneration(int seed, CitySave? restore, int roadRevision = 0)
    {
        if (Screen == GameScreen.Generating || _disposed) return;
        if (restore is null) SaveGame(wait:true);
        ActiveSaveSlot=SelectedSaveSlot;

        _city?.Dispose();
        _city = null;
        _shopOpen = false;
        _contractsOpen = false;
        Input.ResetAll();
        Error = null;
        _restore = restore;
        _cancel?.Dispose();
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        Screen = GameScreen.Generating;
        _pendingGpu = false;
        Report(0, "Preparing city");

        _generation = Task.Run(() =>
        {
            CityLayout layout = CityLayout.Generate(seed, Report, token,
                restore is null ? 49 : restore.World is null ? 33 : CityTraffic.SavedGridSize(restore.World),
                restore is null || restore.World?.WideRoads == true,
                restore is null ? roadRevision : restore.World?.RoadRevision ?? 0);

            token.ThrowIfCancellationRequested();
            CityLayout.Install(layout);
            return new CityModel(Report, token);
        }, token);
    }

    public void ShowMenu()
    {
        if (_contractsOpen)
        {
            _contractsOpen = false;
            Input.ResetAll();
            return;
        }

        if (_shopOpen)
        {
            _shopOpen = false;
            Input.ResetAll();
            return;
        }

        if (Screen == GameScreen.Settings)
        {
            Screen = GameScreen.Menu;
            _selection = 0;
            return;
        }

        if (Screen == GameScreen.Generating)
        {
            _cancel?.Cancel();
            return;
        }

        if (Screen == GameScreen.Playing)
        {
            SaveGame();
            Screen = GameScreen.Menu;
            Input.ResetAll();
            City.Model.Input = default;
            City.Model.Traffic.ClearPlayerInput();
            _selection = 0;
        }
        else if (HasCity)
        {
            Screen = GameScreen.Playing;
            Input.ResetAll();
        }
    }

    public void SaveSelectedSlot(){SaveGame(wait:true,destinationSlot:SelectedSaveSlot);}

    public void SaveGame(bool wait = false,int? destinationSlot=null)
    {
        if (_city is null || Screen == GameScreen.Generating) return;
        try
        {
            // One writer at a time: a second F5 within the same second waits for
            // the first file to land rather than racing it for the same name.
            WaitForSave();
            // Format 3 (29.10.2026): compact actor states and Bezier handles instead of sampled routes; formats 1 and 2 still load.
            CityModel m = City.Model;
            var save = new CitySave(3, m.Seed, m.X, m.Z, m.Heading, m.Hour, m.Traffic.Money, m.RainTarget, m.Traffic.CaptureWorld(m));
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            _saveClock = 0;
            string path = CitySaveSlots.PathFor(_saveDirectory,destinationSlot??ActiveSaveSlot);
            bool ru = _ru;

            // The capture holds only copies (cloned paths, JsonElements, new
            // records), so the live world is free to move on while it is written.
            _saveTask = Task.Run(() =>
            {
                try
                {
                    string temp = path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(save, CityState.Json));
                    if (File.Exists(path)) File.Copy(path, path + ".bak", true);
                    File.Move(temp, path, true);
                    Volatile.Write(ref _savedUntil, Environment.TickCount64 + 3000);
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref _saveError, (ru ? "Ошибка сохранения: " : "Save failed: ") + ex.Message);
                }
            });

            if (wait) WaitForSave();
        }
        catch (Exception ex)
        {
            Error = (_ru ? "Ошибка сохранения: " : "Save failed: ") + ex.Message;
        }
    }

    private void WaitForSave()
    {
        if (_saveTask is { } task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
            }

            _saveTask = null;
        }

        if (Volatile.Read(ref _saveError) is { } error)
        {
            Error = error;
            _saveError = null;
        }
    }

    private void AdvanceGeneration()
    {
        if (_generation is { IsCompleted: true } task)
        {
            _generation = null;

            try
            {
                var model = task.GetAwaiter().GetResult();
                _cancel!.Token.ThrowIfCancellationRequested();
                Report(.83, "Uploading models and shaders");
                _city = new(model) { DrawDistance = Settings.DrawDistance, Quality = Settings.Quality };
                _city.Initialize(this);
                _city.Resize(ViewportWidth, ViewportHeight);
                _city.Start();

                if (_restore is { } save)
                {
                    if (save.World is { } world) model.Traffic.RestoreWorld(world, model);
                    CityCar h = model.Traffic.Hero;

                    if (save.World is null)
                    {
                        h.Body.X = save.X;
                        h.Body.Y = save.Z;
                        h.Body.Angle = -save.Heading;
                        h.Position = new CityPoint(save.X, save.Z);
                        h.Heading = save.Heading;
                        model.Traffic.UpdateTerrainSupport(h);
                    }

                    model.SetHour(save.Hour);
                    if (save.World is null) model.SetRain(save.Raining);
                    model.Traffic.SetMoney(save.Money);
                    _city.Chase.Reset(model.Traffic);
                }

                _pendingGpu = true;
                Report(.90, "Streaming nearby districts");
            }
            catch (OperationCanceledException)
            {
                _city?.Dispose();
                _city = null;
                Screen = GameScreen.Menu;
            }
            catch (Exception ex)
            {
                _city?.Dispose();
                _city = null;
                Screen = GameScreen.Menu;
                Error = ex.Message;
            }
        }
        else if (_pendingGpu && _city is { } city)
        {
            if (_cancel!.IsCancellationRequested)
            {
                city.Dispose();
                _city = null;
                _pendingGpu = false;
                Screen = GameScreen.Menu;
                return;
            }

            double ready = city.PrepareView();
            Report(.90 + .10 * ready, "Streaming nearby districts");
            if (ready >= 1)
            {
                _pendingGpu = false;
                Screen = GameScreen.Playing;
                Input.ResetAll();
                SaveGame();
            }
        }
    }

    private readonly CityFrameRate _fps = new();

    public void Frame(double dt)
    {
        if (!_initialized || _disposed) return;
        _fps.Sample(dt);

        if (Volatile.Read(ref _saveError) is { } saveError)
        {
            Error = saveError;
            _saveError = null;
        }

        if (_saveTask is { IsCompleted: true })
        {
            _saveTask = null;
        }

        if (_audio is null)
        {
            _audio = new CityAudio(Shared.AppDirectory);
            // A missing output device or an unreadable clip used to be logged and
            // nothing else; the menu now says why the game is silent.
            if (_audio.Error is { } audioError) Error ??= (_ru ? "Звук: " : "Audio: ") + audioError;
        }

        _audio.Volume = Settings.Volume;
        _audio.MusicVolume = Settings.MusicVolume;
        _audio.Playing = Active;
        _audio.WorldPlaying = Screen == GameScreen.Playing;

        if (!Console.Open && Input.WasKeyPressed(0x4d))
        {
            _audio.NextTrack();
        }

        try
        {
            if (Screen == GameScreen.Playing && Active && !Console.Open && !Input.ImeOwnsKeyboard && !_shopOpen && !City.Model.Traffic.DroneActive && Input.WasKeyPressed(0x4A))
            {
                _contractsOpen = !_contractsOpen;
                Input.ResetAll();
            }

            if (_contractsOpen && Screen == GameScreen.Playing)
            {
                if ((Input.WasKeyPressed(0x31) && City.Model.Traffic.StartContract(JobKind.VehicleDelivery)) 
                    ||
                    (Input.WasKeyPressed(0x32) && City.Model.Traffic.StartContract(JobKind.Elimination)) ||
                    (Input.WasKeyPressed(0x33) && City.Model.Traffic.StartChainContract()) ||
                    (Input.WasKeyPressed(0x34) && City.Model.Traffic.StartPeaceJob(JobKind.Leaflets)) ||
                    (Input.WasKeyPressed(0x35) && City.Model.Traffic.StartPeaceJob(JobKind.PeaceBroadcast)))
                {
                    _contractsOpen = false;
                    Input.ResetAll();
                }
                else if (Input.WasKeyPressed(0x43))
                {
                    City.Model.Traffic.CancelContract();
                    _contractsOpen = false;
                    Input.ResetAll();
                }
            }

            if(Screen==GameScreen.Playing&&Active&&!_contractsOpen&&!_shopOpen&&!Input.ImeOwnsKeyboard&&!City.Model.Traffic.DroneActive&&Input.WasKeyPressed(0x47))City.Model.Traffic.UseOwnedGarage();

            if(Screen==GameScreen.Playing&&Active&&!_contractsOpen&&!_shopOpen&&!Input.ImeOwnsKeyboard&&!City.Model.Traffic.DroneActive&&Input.WasKeyPressed(0x42))City.Model.Traffic.RideTransit();

            Input.ModalOpen = Console.Open || !Active || Input.ImeOwnsKeyboard || Screen != GameScreen.Playing || _shopOpen || _contractsOpen;

            // E (29.10.2026): the market opens its list here; jobs and the garage are handled by the model.
            if (Screen == GameScreen.Playing && Active && !Input.ModalOpen && !City.Model.Traffic.DroneActive && Input.WasKeyPressed(0x45) && _city is { } interacting)
            {
                CityTraffic traffic = interacting.Model.Traffic;

                if (traffic.PeaceCanInteract) traffic.InteractPeaceJob();
                else if (traffic.NearbyMarket() is not null)
                {
                    _shopOpen = true;
                    _shopSelection = 0;
                    Input.ResetAll();
                    Input.ModalOpen = true;
                }
                else traffic.Interact();
            }

            GL.ResetStateDefaults();
            GL.Viewport(0, 0, ViewportWidth, ViewportHeight);
            GL.ClearColor(0, 0, 0, 1);
            GL.Clear(GL.COLOR_BUFFER_BIT | GL.DEPTH_BUFFER_BIT);
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();

            if (Screen == GameScreen.Playing && _city is { } city)
            {
                double step = Active && !Console.Open && !_shopOpen && !_contractsOpen && double.IsFinite(dt)
                    ? Math.Clamp(dt, 0, .05)
                    : 0;

                city.Quality = Settings.Quality;
                city.DrawDistance = Settings.DrawDistance;
                city.EffectsEnabled = Settings.Effects;
                city.GlowEnabled = Settings.Bloom;
                city.Model.Traffic.SpawnDistance = CityLod.SpawnRange(Settings.DrawDistance);
                city.Update(step, Input);

                if (_seenRespawn != city.Model.Traffic.Respawns)
                {
                    _seenRespawn = city.Model.Traffic.Respawns;
                    city.Chase.Reset(city.Model.Traffic);
                }

                LastSimulationMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                long audioStamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _audio.Update(city.Model.Traffic, city.Chase.Heading, city.Model.Rain);
                LastAudioMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(audioStamp).TotalMilliseconds;
                LastUpdateMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                stamp = System.Diagnostics.Stopwatch.GetTimestamp();
                city.Render();
                _saveClock += step;
                if (_saveClock > 120) SaveGame();
            }

            if (Screen == GameScreen.Playing && Input.WasKeyPressed(0x74)) SaveGame();
            if (Screen != GameScreen.Generating && Input.WasKeyPressed(0x78) && CanLoad) LoadGame();
            Ui.Draw.BeginFrame(ViewportWidth / Ui.Scale, ViewportHeight / Ui.Scale, ViewportWidth, ViewportHeight, Ui.Scale);
            float width = ViewportWidth / Ui.Scale, height = ViewportHeight / Ui.Scale;

            if (Screen == GameScreen.Playing)
            {
                CityMinimap.Draw(Ui.Draw, City.Model, width, height, _ru, Settings.Language=="uk");
                DrawWallet();
                UiDraw.TextScaled(width - 164, 103, $"{_fps.FramesPerSecond:0} FPS  {_fps.Milliseconds:0.0} ms", new UiColor(.85f, .9f, .78f), .82f);
                if (_shopOpen) DrawShop(width, height);
                if (_contractsOpen) DrawContracts(width, height);
            }
            else if (Screen == GameScreen.Menu)
            {
                MenuBackdrop.Draw(Ui.Draw, width, height, (float)(Environment.TickCount64 * .001), _menuImage);
                DrawMenu(width, height);
            }
            else if (Screen == GameScreen.Settings)
            {
                DrawSettings(width, height);
            }
            else
            {
                DrawProgress(width, height);
            }

            DrawConsole(width,height,dt);
            Ui.Draw.Flush();
            LastRenderMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;

            // Loading work follows drawing, so the last presented frame stays black
            // with the actual progress bar during expensive GL initialization.
            if (Screen == GameScreen.Generating) AdvanceGeneration();
        }
        finally
        {
            Input.EndFrame();
        }
    }

    private void DrawWallet()
    {
        var d=UiDraw;
        CityTraffic t = City.Model.Traffic;
        var paper = new UiColor(.96f,.89f,.74f);
        var backing = new UiColor(.052f,.075f,.073f,.94f);
        var brass = new UiColor(.89f,.65f,.32f);
        d.Rect(22,22,180,46,backing);
        d.Rect(22,22,4,46,new UiColor(.43f,.69f,.49f));
        d.TextScaled(33,28,"$",new(.62f,.84f,.62f),1.45f);
        d.TextScaled(58,29,t.Money.ToString("N0"),paper,1.4f);

        bool mounted = !t.OnFoot && !t.PassengerMode && t.Hero.Kind is 11 or 14;
        var weapon = t.Inventory.Active;
        if (!t.OnScooter && !t.DroneActive && !t.PassengerMode && (mounted || t.OnFoot || weapon is not null))
        {
            int icon = mounted ? (t.Hero.Kind==14 ? 6 : 5) : (int)(weapon?.Id ?? CityWeaponId.None);
            string name = mounted ? (icon==6 ? (_ru?"ПУЛЕМЁТ":"MOUNTED MG") : (_ru?"ПУШКА":"CANNON")) :
                weapon is null ? (_ru?"КУЛАК":"FIST") : (_ru?weapon.Russian:weapon.English);
            d.Rect(211,22,190,63,backing);
            d.Rect(211,22,190,2,brass);
            d.DrawIcon(214,29,88,44,_weaponArt[icon]);
            d.TextScaled(302,30,name,paper,.82f);
            string rounds = mounted ? "∞" : weapon is null ? "—" : t.Inventory.Ammo(weapon.Id).ToString();
            d.TextScaled(302,49,rounds,brass,1.30f);
        }

        if (t.WantedLevel > 0)
        {
            float x = ViewportWidth / Ui.Scale - 260;
            d.Rect(x, 22, 238, 69, new UiColor(.035f, .025f, .02f, .87f));
            d.TextScaled(x + 14, 32, _ru ? "В РОЗЫСКЕ" : "WANTED", new UiColor(.95f, .63f, .32f), 1.1f);

            for (int n = 0; n < CityTraffic.MaxWantedLevel; n++)
            {
                d.Rect(x + 14 + n * 35, 65, 29, 10, new(.21f, .20f, .17f));
                d.Rect(x + 14 + n * 35, 65, 29 * (float)Math.Clamp(t.Wanted - n, 0, 1), 10, new UiColor(.87f, .42f, .16f));
            }
        }

        if (Volatile.Read(ref _savedUntil) > Environment.TickCount64)
        {
            d.TextScaled(22, 315, _ru ? "Сохранено" : "Saved", new UiColor(.66f, .82f, .58f), .8f);
        }

        if (!t.OnScooter && !t.PassengerMode && !t.PlayerUnavailable && !t.DroneActive && (t.OnFoot || t.Hero.Kind==14))
        {
            (float X, float Y, bool Visible) aim = City.Reticle(ViewportWidth / Ui.Scale, ViewportHeight / Ui.Scale);

            if (aim.Visible)
            {
                var color = new UiColor(.93f, .91f, .72f);
                d.Line(aim.X - 10, aim.Y, aim.X - 4, aim.Y, color, 2);
                d.Line(aim.X + 4, aim.Y, aim.X + 10, aim.Y, color, 2);
                d.Line(aim.X, aim.Y - 10, aim.X, aim.Y - 4, color, 2);
                d.Line(aim.X, aim.Y + 4, aim.X, aim.Y + 10, color, 2);
                d.Circle(aim.X, aim.Y, 1.3f, color);
            }
        }

        DrawDroneHud();
        DrawEnergy();
        DrawPlayerControls();
        DrawJob();
        if(CityLayout.Current.RoadRevision>=4)UiDraw.TextScaled(22,344,CityNeighbourhoods.Name(CityNeighbourhoods.At(t.PlayerPosition),_ru),new(.75f,.78f,.7f),.8f);
        if(t.PassengerMode)UiDraw.TextScaled(22,294,_ru?"ПАССАЖИР · B / F — выйти на остановке":"PASSENGER · B / F — leave at a stop",new(.7f,.85f,.95f),.85f);
        else if(t.OnFoot&&t.NearbyEnterableCar() is {TransitRoute:>0,Driverless:false})UiDraw.TextScaled(22,294,_ru?"B — ехать пассажиром на остановке · F — занять водительское место":"B — ride at a stop · F — take the driver seat",new(.7f,.85f,.95f),.8f);
        if(t.OwnershipGarage() is {} garage){
            string text=t.OwnedGarageId==0?(_ru?"G — купить гараж ($400)":"G — buy garage ($400)"):
                t.OwnedGarageId==garage.Id?(t.OnFoot?(_ru?$"Ваш гараж · {t.OwnedVehicleCount}/3 машин":$"Your garage · {t.OwnedVehicleCount}/3 cars"):
                (_ru?$"G — оставить легковую машину · {t.OwnedVehicleCount}/3":$"G — keep this car parked · {t.OwnedVehicleCount}/3")):(_ru?"Сервисный гараж":"Service garage");
            UiDraw.TextScaled(22,270,text,new(.72f,.9f,.75f),.85f);
        }
        var hero = City.Model.Traffic.Hero;
        d.Rect(22, 73, 180, 6, new UiColor(.12f, .14f, .12f));
        d.Rect(22, 73, 180 * (float)t.PlayerHealth, 6, new UiColor(.38f, .65f, .35f));

        if (!t.OnFoot)
        {
            d.Rect(22, 106, 180, 6, new UiColor(.12f, .14f, .12f));
            d.Rect(22, 106, 180 * (float)t.VehicleHealth, 6, hero.Burning ? new UiColor(.9f, .2f, .05f) : new UiColor(.32f, .57f, .79f));
            d.TextScaled(22, 116, _ru ? "МАШИНА" : "VEHICLE", new(.65f, .76f, .85f), .8f);
        }
        if (t.PlayerUnavailable)
        {
            d.TextScaled(22, 142, _ru ? "Возвращаемся к клинике..." : "Returning to a clinic...", new UiColor(.94f, .72f, .4f), 1);
        }
        else if (!t.OnFoot && hero.Burning)
        {
            d.TextScaled(22, 142, _ru ? "Машина горит!" : "Vehicle on fire!", new UiColor(1, .34f, .1f), 1);
        }

        if (!t.OnFoot && City.Model.Traffic.Hero.Recovering)
        {
            d.TextScaled(22, 165, _ru ? "Возвращаем машину на колёса..." : "Recovering vehicle...", new UiColor(.92f, .74f, .36f), 1);
        }
    }

    private void DrawPlayerControls()
    {
        var t = City.Model.Traffic;
        var d=UiDraw;
        float width = ViewportWidth / Ui.Scale, height = ViewportHeight / Ui.Scale;
        string hint = "";
        if (t.OnFoot && t.EnteringCarId < 0 && t.NearbyEnterableCar() is { } c)
            hint = _ru
                ? (c.Driverless ? "F — сесть в машину" : "F — вытащить водителя и сесть")
                : (c.Driverless ? "F — enter vehicle" : "F — pull driver out and enter");
        if(t.OnFoot && t.NearbyEnterableCar() is {} transit && transit.TransitRoute>0 && !transit.Driverless && Math.Abs(transit.Speed)<.8 && transit.TransitStopUntil>t.Time)
            hint=_ru?"B — ехать пассажиром · F — занять место водителя":"B — ride as passenger · F — take driver's seat";
        // A kiosk can offer a snack on F and a parcel on E at the same time; show both.
        string? jobHint = t.NearbyJob() is { } job
            ? JobPrompt(job)
            :
            t.JobKind is JobKind.Taxi or JobKind.Patient && t.JobStage == 1 && !t.OnFoot &&
            Math.Abs(t.Hero.Speed) < .8 && t.JobDistance > 14
                ?
                (_ru ? "E — высадить пассажира (без оплаты)" : "E — drop the passenger off (no fare)")
                : null;
        // E serves the garage forecourt first (Interact), then jobs; the market stall opens its own list.
        if (t.NearbyGarage() is not null && t.GarageCost > 0)
            jobHint = (_ru ? $"E — ремонт ${t.RepairCost}" : $"E — repair ${t.RepairCost}") + (t.ResprayOffered
                ? (_ru ? $" + перекраска ${CityTraffic.ResprayPrice}" : $" + respray ${CityTraffic.ResprayPrice}")
                : "");
        else if (t.NearbyGarage() is not null && jobHint is null)
            jobHint = _ru ? "Гараж: машина в порядке" : "Garage: the car is fine";
        if (t.NearbyMarket() is not null) jobHint = _ru ? "E — рынок" : "E — market";
        if(t.PeaceCanInteract) jobHint = PeacePrompt(t);
        if (t.NearbyFood() is { } food)
        {
            hint = _ru
                ? $"F — {(food.Shawarma ? "шаверма" : "перекус")} · ${food.Price} · здоровье +{food.Healing * 100:0}%"
                : $"F — {(food.Shawarma ? "shawarma" : "snack")} · ${food.Price} · health +{food.Healing * 100:0}%";
            if (jobHint is not null) hint += " · " + jobHint;
        }
        else if (jobHint is not null) hint = jobHint;

        if(t.OnScooter)hint=_ru?"F — сойти с самоката":"F — leave scooter";
        else if(t.OnFoot&&t.NearbyScooter() is not null)hint=_ru?"F — встать на самокат":"F — ride scooter";
        if (t.EnteringCarId >= 0) hint = _ru ? "Садимся...  F / WASD — отмена" : "Entering...  F / WASD cancels";
        if (t.MessageUntil > t.Time)
            hint = (t.InteractionMessage, _ru) switch
            {
                ("full", true) => "Вы уже здоровы", ("poor", true) => "Недостаточно денег",
                ("food", true) => "Приятного аппетита!", ("full", false) => "Health is already full",
                ("poor", false) => "Not enough cash", ("food", false) => "Enjoy your snack!",
                ("stop", true) => "Сначала остановите машину",
                ("blocked", true) => "Дверь заблокирована. R — эвакуация машины",
                ("recover", true) => "Сначала нужно встать", ("cancel", true) => "Посадка прервана",
                ("near", true) => "Подойдите к двери стоящей машины", ("stop", false) => "Stop the vehicle first",
                ("blocked", false) => "Door blocked. R — recover vehicle",
                ("recover", false) => "Get back on your feet first", ("cancel", false) => "Entry cancelled",
                ("job_wave", true) => "Пассажир идёт к машине", ("job_taxi", true) => "Поехали!",
                ("job_patient", true) => "«Держись, довезу»", ("job_parcel", true) => "Посылка у вас — везите",
                ("job_paid", true) => $"Получено ${t.LastJobPay}",
                ("job_late", true) => "Опаздываем — заплатят половину",
                ("job_scared", true) => "Пассажир напуган — заплатит меньше",
                ("job_left", true) => "Пассажир не дождался и ушёл", ("job_lost", true) => "Заказ сорван",
                ("job_cancel", true) => "Пассажир не дошёл до машины", ("job_dropped", true) => "Высадили без оплаты",
                ("job_wave", false) => "The passenger is walking to the car", ("job_taxi", false) => "Let's go!",
                ("job_patient", false) => "\"Hold on, I'll get you there\"",
                ("job_parcel", false) => "You have the parcel — deliver it",
                ("job_paid", false) => $"Received ${t.LastJobPay}",
                ("job_late", false) => "Running late — half the fare",
                ("job_scared", false) => "The passenger is scared — less pay",
                ("job_left", false) => "The passenger gave up and left", ("job_lost", false) => "Job lost",
                ("job_cancel", false) => "The passenger never reached the car",
                ("job_dropped", false) => "Dropped off without pay",
                ("rescued", true) => "Машина возвращена на свободную дорогу",
                ("rescued", false) => "Vehicle recovered onto a clear road",
                ("rescue_blocked", true) => "Нет свободного места. Попробуйте выйти: F",
                ("rescue_blocked", false) => "No clear recovery spot. Try F to escape",
                ("peace_need_car", true) => "Нужна стоящая легковая машина или фургон; сядьте за руль",
                ("peace_need_car", false) => "Park a car or van and take the driver's seat",
                ("peace_leaflets", true) => "Листовки: четыре места на карте. В круге нажмите E и стойте 3 секунды",
                ("peace_leaflets", false) => "Leaflets: four sites. Press E in the circle and stand still for 3 seconds",
                ("peace_broadcast", true) => "Громкоговоритель: E — включить. Проедьте через три отмеченных района",
                ("peace_broadcast", false) => "Loudspeaker: E to switch on. Drive through three marked districts",
                ("peace_escape", true) => "Маршрут завершён. Уйдите от розыска, чтобы получить награду",
                ("peace_escape", false) => "Route complete. Lose the wanted level to receive your reward",
                ("peace_next", true) => "Готово — следующая точка отмечена на карте",
                ("peace_next", false) => "Done — next site marked on the map",
                ("contract_start", true) => "Заказ принят — цель отмечена на карте",
                ("contract_start", false) => "Contract accepted — target marked on map",
                ("contract_unavailable", true) => "Подходящих заказов пока нет",
                ("contract_unavailable", false) => "No suitable contract available",
                ("garage_fine", true) => "Машина в порядке", ("garage", true) => "Готово — как новая",
                ("bought", true) => "Куплено",
                ("garage_fine", false) => "The car is fine", ("garage", false) => "Done — good as new",
                ("bought", false) => "Bought",
                ("arrest", true) => $"Задержан. Штраф ${t.LastFine}, оружие изъято, несколько часов в отделении",
                ("bill", true) => $"Клиника выставила счёт ${t.LastBill}",
                ("clinic_poor", true) => "Лечение платное — без денег лечат медленно",
                ("arrest", false) => $"Arrested. Fine ${t.LastFine}, weapons seized, a few hours in the cell",
                ("bill", false) => $"The clinic billed ${t.LastBill}",
                ("clinic_poor", false) => "Treatment costs money — the poor queue heals slowly",
                _ => "Approach a stopped vehicle door"
            };
        if(t.OnFoot && t.NearbyTalker() is not null && t.NearbyEnterableCar() is null && t.NearbyFood() is null && hint.Length==0)
            hint=_ru?"F — заговорить":"F — talk";
        // The live interaction must not be hidden by the six-second mission announcement.
        if(t.PeaceCanInteract) hint=PeacePrompt(t);
        if(t.OwnershipGarage() is not null)
            hint += (hint.Length>0?" · ":"") + (_ru?"G — гараж":"G — owned garage");
        if(!Console.Open && !_contractsOpen && !_shopOpen)
            DrawControlStrip(width,height,hint,t.SpeechUntil>t.Time?SpeechLine(t):null);

        d.TextScaled(22, 85, _ru ? "ЗДОРОВЬЕ" : "HEALTH",
            new(.65f, .76f, .65f), .58f);
    }

    // What the person the player addressed says; {street} is the place the line points to.
    private string SpeechLine(CityTraffic t)
    {
        string street = t.SpeechHasPlace ? CityNames.StreetAt(t.SpeechPlace, _ru) : "";
        return (t.Speech, _ru) switch
        {
            ("officer_wanted", true) => "Стоять! Руки за голову!",
            ("officer", true) => "Проходите, гражданин, не задерживайтесь.",
            ("tough_night", true) => "Чё надо? Иди отсюда, пока цел.", ("tough", true) => "Не мешай, братан.",
            ("elder_clinic", true) => $"Поликлиника — на {street}, милок.",
            ("elder", true) => "Ох, молодёжь... Раньше тут автобус ходил каждые десять минут.",
            ("drunk_ask", true) => "Братан, выручи пятёркой на пиво? (F — дать $5)",
            ("homeless_ask", true) => "Мелочи не найдётся? (F — дать $5)",
            ("thanks", true) => "Спасибо, брат! Дай бог здоровья.", ("nocash", true) => "У тебя у самого пусто.",
            ("night", true) => "Не сегодня, милый.", ("medic", true) => "Не мешайте работать.",
            ("beach", true) => "Водичка — класс! Иди окунись.",
            ("market", true) => $"Рынок? На {street}, там всё есть.",
            ("rumour", true) => "Слышал, на промзоне тупик — вагоны загнали и бросили.",
            ("cabbie", true) => "Бомбилы совсем обнаглели — по двадцатке за квартал.",
            ("ride", true) => $"Таксуешь? Подбрось до {street}!",
            ("injured", true) => $"Брат, отвези в клинику на {street}, сам не дойду...",
            ("hailer", true) => $"Такси? Мне на {street}.",
            ("scared", true) => "Не подходи!", (_, true) => "Нормально всё. Живём.",
            ("officer_wanted", false) => "Freeze! Hands behind your head!",
            ("officer", false) => "Move along, citizen.",
            ("tough_night", false) => "What do you want? Get lost while you can.",
            ("tough", false) => "Don't bother me, pal.",
            ("elder_clinic", false) => $"The clinic is on {street}, dear.",
            ("elder", false) => "Youth these days... The bus used to come every ten minutes here.",
            ("drunk_ask", false) => "Pal, spare five for a beer? (F — give $5)",
            ("homeless_ask", false) => "Got any change? (F — give $5)",
            ("thanks", false) => "Thanks, brother! Bless you.", ("nocash", false) => "You're broke yourself.",
            ("night", false) => "Not tonight, honey.", ("medic", false) => "Let us work.",
            ("beach", false) => "The water's great! Go for a dip.",
            ("market", false) => $"The market? On {street}, they have everything.",
            ("rumour", false) => "They say there's a dead end at the industrial zone — wagons shunted in and left.",
            ("cabbie", false) => "Gypsy cabs got brazen — twenty a block.",
            ("ride", false) => $"You a cab? Give me a lift to {street}!",
            ("injured", false) => $"Brother, get me to the clinic on {street}, I can't walk...",
            ("hailer", false) => $"Taxi? I'm going to {street}.",
            ("scared", false) => "Stay away!", _ => "All fine. Getting by."
        };
    }

    private string JobPrompt(JobOffer job)
    {
        string name = CityNames.StreetAt(job.Target, _ru);
        return (job.Kind, job.Deliver, _ru) switch
        {
            (JobKind.Taxi, _, true) => $"E — подобрать пассажира · ${job.Fare} → {name}",
            (JobKind.Taxi, _, false) => $"E — pick up the fare · ${job.Fare} → {name}",
            (JobKind.Patient, _, true) => $"E — отвезти пострадавшего в клинику · ${job.Fare}",
            (JobKind.Patient, _, false) => $"E — take the injured to a clinic · ${job.Fare}",
            (_, false, true) => $"E — взять посылку · ${job.Fare} → {name}",
            (_, false, false) => $"E — take the parcel · ${job.Fare} → {name}",
            (_, true, true) => $"E — отдать посылку · ${job.Fare}", _ => $"E — hand over the parcel · ${job.Fare}"
        };
    }

    // The active job: kind, destination street and distance, fare and the time left.
    private void DrawJob()
    {
        var t = City.Model.Traffic;
        if (t.JobKind == JobKind.None) return;
        if (t.PeaceJobActive) { DrawPeaceJob(); return; }
        var d=UiDraw;
        float x = 22, y = 190;
        d.Rect(x, y, 300, 62, new(.02f, .03f, .025f, .88f));
        string title = (t.JobKind, _ru) switch
        {
            (JobKind.VehicleDelivery, true) => "ПЕРЕГОН АВТО",
            (JobKind.VehicleDelivery, false) => "VEHICLE HANDOVER", (JobKind.Elimination, true) => "УСТРАНИТЬ ЦЕЛЬ",
            (JobKind.Elimination, false) => "ELIMINATION", (JobKind.Taxi, true) => "ТАКСИ",
            (JobKind.Patient, true) => "ПОСТРАДАВШИЙ", (JobKind.Courier, true) => "ДОСТАВКА",
            (JobKind.Taxi, false) => "TAXI", (JobKind.Patient, false) => "INJURED", _ => "DELIVERY"
        };
        d.TextScaled(x + 12, y + 8, title, new(.95f, .8f, .35f), .8f);
        string metres = _ru ? "м" : "m",
            where = t.ContractActive
                ?
                ($"#{t.ContractActorId} · {t.JobDistance:0} {metres} · " + (t.JobKind == JobKind.VehicleDelivery
                    ? (_ru
                        ? $"авто {t.ContractVehicleHealth:P0} / минимум {t.ContractMinimumHealth:P0}"
                        : $"car {t.ContractVehicleHealth:P0} / min {t.ContractMinimumHealth:P0}")
                    : (_ru ? "цель на карте" : "target on map")))
                : t.JobStage == 0
                    ? (_ru ? "пассажир идёт к машине..." : "passenger walking to the car...")
                    : $"→ {t.JobTargetName(_ru)} · {t.JobDistance:0} {metres}";
        d.TextScaled(x + 12, y + 26, where, new(.88f, .9f, .75f), .74f);
        if (t.JobStage == 1)
        {
            double left = Math.Max(0, t.JobTimeLeft);
            string timer = t.JobLate ? (_ru ? "опоздание" : "late") : $"{(int)(left / 60)}:{(int)(left % 60):00}";
            d.TextScaled(x + 12, y + 44, $"${t.JobFare} · {timer}",
                t.JobLate ? new(.95f, .5f, .3f) : new(.8f, .92f, .68f), .74f);
        }
    }

    // The market list: four wares and a close line, arrows or mouse to choose, Enter or click to buy, Esc to leave.
    private void DrawContracts(float w, float h)
    {
        var d=UiDraw;
        float x = w * .5f - 290, y = h * .5f - 195;
        d.Rect(x - 20, y - 32, 620, 440, new(.025f, .035f, .03f, .96f));
        d.TextScaled(x, y - 12, _ru ? "ЗАКАЗЫ" : "CONTRACTS", new(.95f, .8f, .35f), 1.3f);
        string[] lines = _ru
            ?
            [
                "1 — Перегнать указанную машину · $250", "Доставить к гаражу: здоровье авто не ниже 50%.",
                "2 — Устранить указанную цель · $350", "Цель должна погибнуть от ваших действий.",
                "3 — Цепочка заказов · этап " + (City.Model.Traffic.ChainStage+1) + "/3",
                "Награды: $300 / $500 / $700 + $500 за финал.",
                "4 — Листовки за мир · $300", "Четыре точки пешком, затем уйти от розыска.",
                "5 — Громкоговоритель · $500", "30 секунд в каждом из трёх районов, затем скрыться.",
                "C — отменить текущий заказ · J / Esc — закрыть"
            ]
            :
            [
                "1 — Deliver the marked vehicle · $250", "Reach the garage with at least 50% vehicle health.",
                "2 — Eliminate the marked target · $350", "Only a player-caused death completes this contract.",
                "3 — Contract chain · stage " + (City.Model.Traffic.ChainStage+1) + "/3",
                "Rewards: $300 / $500 / $700 + $500 finale bonus.",
                "4 — Leaflets for peace · $300", "Four sites on foot, then lose the wanted level.",
                "5 — Loudspeaker run · $500", "30 seconds in each of three districts, then escape.",
                "C — cancel current contract · J / Esc — close"
            ];
        for (int i = 0; i < lines.Length; i++) d.TextScaled(x, y + 28 + i * 30, lines[i], new(.86f, .89f, .78f), .82f);
        if (City.Model.Traffic.JobKind != JobKind.None)
            d.TextScaled(x, y + 376,
                _ru ? "Сначала завершите или отмените текущий заказ." : "Finish or cancel your current job first.",
                new(.95f, .65f, .3f), .76f);
    }

    private void DrawShop(float w, float h)
    {
        var t = City.Model.Traffic;
        var d=UiDraw;
        float x = w * .5f - 230, y = Math.Max(90, h * .5f - 150);
        d.Rect(x - 16, y - 70, 492, 330, new(.03f, .04f, .035f, .93f));
        d.TextScaled(x, y - 56, _ru ? "РЫНОК" : "MARKET", new(.95f, .8f, .35f), 1.4f);
        d.TextScaled(x + 300, y - 48, $"${t.Money:N0}", new(.8f, .92f, .68f), 1);
        string[] labels =
        [
            (_ru
                ? (t.Inventory.Owns(CityWeaponId.Pistol) ? "Патроны к пистолету ×30" : "Пистолет (30 патронов)")
                : (t.Inventory.Owns(CityWeaponId.Pistol) ? "Pistol rounds ×30" : "Pistol (30 rounds)")) +
            $" — ${t.Price(ShopItem.Pistol)}",
            (_ru
                ? (t.Inventory.Owns(CityWeaponId.Automatic) ? "Патроны к автомату ×30" : "Автомат (30 патронов)")
                : (t.Inventory.Owns(CityWeaponId.Automatic) ? "Rifle rounds ×30" : "Automatic rifle (30 rounds)")) +
            $" — ${t.Price(ShopItem.Automatic)}",
            (_ru ? "Таблетки — здоровье +35%" : "Pills — health +35%") + $" — ${t.Price(ShopItem.Pills)}",
            (_ru ? "Энергетик — бодрость 25 с" : "Energy drink — 25 s of energy") +
            $" — ${t.Price(ShopItem.EnergyDrink)}",
            _ru ? "Уйти" : "Leave"
        ];
        if (Input.WasKeyPressed(0x26) || Input.WasKeyPressed(0x57))
            _shopSelection = (_shopSelection + labels.Length - 1) % labels.Length;
        if (Input.WasKeyPressed(0x28) || Input.WasKeyPressed(0x53))
            _shopSelection = (_shopSelection + 1) % labels.Length;
        for (int i = 0; i < labels.Length; i++)
        {
            float by = y + i * 48;
            bool hover = Input.MouseX / Ui.Scale >= x && Input.MouseX / Ui.Scale < x + 460 &&
                         Input.MouseY / Ui.Scale >= by && Input.MouseY / Ui.Scale < by + 40;
            if (hover && (Input.MouseDx != 0 || Input.MouseDy != 0)) _shopSelection = i;
            d.Rect(x, by, 460, 40, _shopSelection == i ? new(.18f, .20f, .18f) : new(.055f, .065f, .06f));
            d.TextScaled(x + 14, by + 10, labels[i], new(.87f, .87f, .77f), .95f);
            bool activate = (hover && (Input.Pressed & MouseButton.Left) != 0) ||
                            _shopSelection == i && (Input.WasKeyPressed(0x0d) || Input.WasKeyPressed(0x45));
            if (!activate) continue;
            if (i == 4)
            {
                _shopOpen = false;
                Input.ResetAll();
                break;
            }

            t.Buy((ShopItem)i);
            break;
        }

        string note = t.MessageUntil > t.Time
            ? (t.InteractionMessage, _ru) switch
            {
                ("poor", true) => "Недостаточно денег", ("full", true) => "Это сейчас не нужно",
                ("bought", true) => "Куплено", ("poor", false) => "Not enough cash",
                ("full", false) => "Not needed right now", ("bought", false) => "Bought", _ => ""
            }
            : "";
        d.TextScaled(x, y + labels.Length * 48 + 10,
            note.Length > 0 ? note : (_ru ? "Enter — купить. Esc — уйти." : "Enter buys. Esc leaves."),
            new(.6f, .66f, .58f), .8f);
    }

    private void DrawEnergy()
    {
        var t = City.Model.Traffic;
        if (t.EnergySeconds <= 0) return;
        var d=UiDraw;
        float w = ViewportWidth / Ui.Scale, h = ViewportHeight / Ui.Scale;
        float strength = (float)Math.Min(1, t.EnergySeconds / 3) * (.85f + .15f * MathF.Sin((float)t.Time * 2));
        for (int n = 0; n < 18; n++)
        {
            float a = .025f * strength * (1 - n / 18f);
            var c = new UiColor(.55f, .95f, .25f, a);
            float q = n * 3;
            d.Rect(q, q, w - 2 * q, 3, c);
            d.Rect(q, h - q - 3, w - 2 * q, 3, c);
            d.Rect(q, q, 3, h - 2 * q, c);
            d.Rect(w - q - 3, q, 3, h - 2 * q, c);
        }

        d.TextScaled(22, 128, (_ru ? "БОДРОСТЬ " : "ENERGY ") + $"{t.EnergySeconds:0}s", new(.65f, .95f, .3f), .8f);
    }

    private void DrawMenu(float w, float h)
    {
        var d=UiDraw;
        float x = w * .5f - 190, y = h * .37f;
        float titleScale = Math.Min(4.6f, w / 250f);
        float titleX = w * .5f - 205;
        d.Rect(titleX - 5, y - 153, 414, 7, new(.67f, .25f, .12f));
        if (_menuTitle is not null) d.DrawIcon(titleX - 15, y - 146, 440, 73, _menuTitle, new UiColor(1, 1, 1, 1));
        else d.TextScaled(titleX, y - 134, "PROMZONA", new(.88f, .81f, .62f), titleScale);
        d.Rect(titleX, y - 76, 410, 2, new(.58f, .51f, .37f));
        d.TextScaled(x, y - 64,
            _ru ? "Провинция. Девяностые. Своя история." : "A provincial town. The nineties. Your story.",
            new(.5f, .54f, .52f), .9f);
        if(Input.WasKeyPressed(0x25))SelectSaveSlot(SelectedSaveSlot==1?3:SelectedSaveSlot-1);
        if(Input.WasKeyPressed(0x27))SelectSaveSlot(SelectedSaveSlot==3?1:SelectedSaveSlot+1);
        for(int slot=1;slot<=3;slot++){
            float sx=x+(slot-1)*128,sy=y-35;
            bool hover=Input.MouseX/Ui.Scale>=sx&&Input.MouseX/Ui.Scale<sx+122&&Input.MouseY/Ui.Scale>=sy&&Input.MouseY/Ui.Scale<sy+27;
            if(Active&&hover&&(Input.Pressed&MouseButton.Left)!=0)SelectSaveSlot(slot);
            d.Rect(sx,sy,122,27,SelectedSaveSlot==slot?new(.25f,.29f,.22f):new(.06f,.07f,.06f));
            bool used=File.Exists(CitySaveSlots.PathFor(_saveDirectory,slot));
            d.TextScaled(sx+8,sy+6,(_ru?"Слот ":"Slot ")+slot+(used?" *":""),new(.85f,.85f,.73f),.8f);
        }
        string[] labels = HasCity
            ? (_ru
                ? ["Продолжить", "Новая игра", "Сохранить", "Загрузить", "Настройки", "Выход"]
                : ["Continue", "New game", "Save game", "Load game", "Settings", "Exit"])
            : (_ru ? ["Новая игра", "Загрузить", "Настройки", "Выход"] : ["New game", "Load game", "Settings", "Exit"]);
        if (Input.WasKeyPressed(0x26) || Input.WasKeyPressed(0x57))
            _selection = (_selection + labels.Length - 1) % labels.Length;
        if (Input.WasKeyPressed(0x28) || Input.WasKeyPressed(0x53)) _selection = (_selection + 1) % labels.Length;
        float spacing = Math.Min(57, (h - y - 110) / labels.Length), buttonHeight = Math.Min(46, spacing - 7);
        for (int i = 0; i < labels.Length; i++)
        {
            float by = y + i * spacing;
            bool hover = Input.MouseX / Ui.Scale >= x && Input.MouseX / Ui.Scale < x + 380 &&
                         Input.MouseY / Ui.Scale >= by && Input.MouseY / Ui.Scale < by + buttonHeight;
            if (hover && (Input.MouseDx != 0 || Input.MouseDy != 0)) _selection = i;
            int action = HasCity ? (i == 0 ? -1 : i == 1 ? 0 : i == 2 ? 4 : i - 2) : i;
            bool enabled = action != 1 || CanLoad;
            d.Rect(x, by, 380, buttonHeight, _selection == i ? new(.18f, .20f, .18f) : new(.055f, .065f, .06f));
            d.TextScaled(x + 18, by + (buttonHeight - 20) / 2, labels[i],
                enabled ? new(.87f, .87f, .77f) : new(.3f, .33f, .3f), 1.1f);
            bool clicked = hover && (Input.Pressed & MouseButton.Left) != 0;
            if (Active && enabled && (clicked || (_selection == i && Input.WasKeyPressed(0x0d))))
            {
                if (action < 0)
                {
                    Screen = GameScreen.Playing;
                    Input.ResetAll();
                }
                else if (action == 4) SaveSelectedSlot();
                else if (action == 0) NewGame();
                else if (action == 1) LoadGame();
                else if (action == 2)
                {
                    Screen = GameScreen.Settings;
                    _selection = 0;
                }
                else ExitRequested?.Invoke();

                break;
            }
        }

        if(HasCity)d.TextScaled(x,h-101,(_ru?"Автосохранение: слот ":"Autosave: slot ")+ActiveSaveSlot+(_ru?" · выбран: ":" · selected: ")+SelectedSaveSlot,new(.55f,.62f,.58f),.8f);
        if (Volatile.Read(ref _savedUntil) > Environment.TickCount64)
            d.TextScaled(x, h - 78, _ru ? "Игра сохранена" : "Game saved", new(.65f, .80f, .57f), .9f);
        if (Error is { } error)
            d.TextScaled(30, h - 55, error[..Math.Min(error.Length, 120)], new(.88f, .47f, .32f), .8f);
        else
            d.TextScaled(30, h - 38,
                _ru
                    ? "WASD — движение   F — машина   V — камера   Shift — бег   Пробел — ручник   ЛКМ — огонь   F5/F9 — сохранение"
                    : "WASD move   F vehicle   V camera   Shift run   Space handbrake   LMB fire   F5/F9 save/load",
                new(.42f, .48f, .44f), .8f);
    }

    private void DrawSettings(float w, float h)
    {
        var d=UiDraw;
        float x = w * .5f - 240, y = Math.Max(90, h * .22f);
        d.TextScaled(x, y - 60, _ru ? "НАСТРОЙКИ" : "SETTINGS", new(.83f, .76f, .56f), 1.6f);
        string yes = _ru ? "Вкл" : "On", no = _ru ? "Выкл" : "Off";
        string[] labels =
        [
            (_ru ? "Язык: " : "Language: ") + (Settings.Language switch {"ru"=>"Русский","uk"=>"Українська",_=>"English"}),
            (_ru ? "Детализация: " : "Detail quality: ") +
            (_ru ? new[] { "Низкая", "Средняя", "Высокая" } : new[] { "Low", "Medium", "High" })[Settings.Quality],
            (_ru ? "Дальность: " : "Draw distance: ") + Settings.DrawDistance + " m",
            (_ru ? "Частицы и дождь: " : "Particles and rain: ") + (Settings.Effects ? yes : no),
            (_ru ? "Свечение: " : "Bloom: ") + (Settings.Bloom ? yes : no),
            (_ru ? "Громкость: " : "Volume: ") + $"{Settings.Volume * 100:0}%",
            (_ru ? "Радио Пустырь: " : "Vacant Lot Radio: ") + $"{Settings.MusicVolume * 100:0}%",
            _ru ? "Назад" : "Back"
        ];
        if (Input.WasKeyPressed(0x26)) _selection = (_selection + labels.Length - 1) % labels.Length;
        if (Input.WasKeyPressed(0x28)) _selection = (_selection + 1) % labels.Length;
        for (int i = 0; i < labels.Length; i++)
        {
            float by = y + i * 48;
            bool hover = Input.MouseX / Ui.Scale >= x && Input.MouseX / Ui.Scale < x + 480 &&
                         Input.MouseY / Ui.Scale >= by && Input.MouseY / Ui.Scale < by + 40;
            if (hover && (Input.MouseDx != 0 || Input.MouseDy != 0)) _selection = i;
            d.Rect(x, by, 480, 40, _selection == i ? new(.18f, .20f, .18f) : new(.055f, .065f, .06f));
            d.TextScaled(x + 14, by + 10, labels[i], new(.87f, .87f, .77f), 1);
            bool left = _selection == i && Input.WasKeyPressed(0x25);
            bool activate = (hover && (Input.Pressed & MouseButton.Left) != 0) ||
                            _selection == i && (Input.WasKeyPressed(0x0d) || Input.WasKeyPressed(0x27) || left);
            if (!activate) continue;
            int delta = left ? -1 : 1;
            switch (i)
            {
                case 0:
                    Settings.Language = Ukrainian.Cycle(Settings.Language,delta);
                    _ru = Settings.Language is "ru" or "uk";
                    break;
                case 1: Settings.Quality = (Settings.Quality + delta + 3) % 3; break;
                case 2: Settings.DrawDistance = CityLod.ClampDistance(Settings.DrawDistance + delta * 50); break;
                case 3: Settings.Effects = !Settings.Effects; break;
                case 4: Settings.Bloom = !Settings.Bloom; break;
                case 5: Settings.Volume = Math.Clamp(Settings.Volume + delta * .1f, 0, 1); break;
                case 6: Settings.MusicVolume = Math.Clamp(Settings.MusicVolume + delta * .05f, 0, .5f); break;
                case 7:
                    Screen = GameScreen.Menu;
                    _selection = 0;
                    break;
            }

            Settings.Save();
            break;
        }

        d.TextScaled(x, y + labels.Length * 48 + 12,
            _ru 
                ? "Стрелки меняют значение. Esc — назад." 
                : "Arrow keys change values. Esc returns.",
            new UiColor(.5f, .55f, .5f), .8f);
    }

    private void DrawProgress(float w, float h)
    {
        var d=UiDraw;
        GenerationStatus p = Progress;

        string stage = _ru
            ? p.Stage switch
            {
                "Preparing city" => "Подготовка города", "Connecting streets" => "Соединяем улицы",
                "Opening avenues" => "Прокладываем проспекты",
                "Building districts and parks" => "Создаём кварталы и парки",
                "Shaping hills and coastline" => "Создаём холмы и берег",
                "Terrain and foundations ready" => "Рельеф и основания зданий готовы",
                "Placing street furniture" => "Расставляем уличные объекты",
                "Building collision world" => "Создаём физический мир",
                "Creating traffic" => "Создаём транспорт и пешеходов", "Preparing the view" => "Подготовка сцены",
                "Uploading models and shaders" => "Загрузка моделей и материалов",
                "Streaming nearby districts" => "Загрузка ближайших кварталов",
                _ => p.Stage
            }
            : p.Stage;

        float bw = Math.Min(w - 80, 800), x = (w - bw) / 2, y = h * .53f;
        d.TextScaled(x, y - 76, _ru ? "СОЗДАЁМ ГОРОД" : "BUILDING YOUR CITY", new UiColor(.84f, .80f, .66f), 1.5f);
        d.Rect(x, y, bw, 38, new UiColor(.12f, .14f, .12f));
        d.Rect(x + 3, y + 3, (bw - 6) * (float)p.Fraction, 32, new UiColor(.60f, .66f, .38f));
        d.TextScaled(x, y + 60, $"{p.Fraction * 100:0}%  {stage}", new UiColor(.65f, .71f, .64f), 1);
        d.TextScaled(x, y + 100, _ru ? "Esc — отменить" : "Esc to cancel", new UiColor(.35f, .40f, .36f), .8f);
    }

    public void LoseFocus()
    {
        Active = false;
        Input.ResetAll();
        Ui.ResetInteraction();

        if (_city is { } c)
        {
            c.Model.Input = default;
            c.Model.Traffic.ClearPlayerInput();
        }
    }

    public void StopBackgroundWork()
    {
        _cancel?.Cancel();
    }

    public void Dispose() => Dispose(true);

    /// <summary>graphics=false: the GL context is gone (lost, or never made current),
    /// so the save, the generation task and the audio device are still released
    /// but no GL object is touched - those die with the process.</summary>
    public void Dispose(bool graphics)
    {
        if (_disposed) return;
        SaveGame(wait: true);
        _disposed = true;
        StopBackgroundWork();
        // Observe cancellation/failure without retaining the session in a continuation.
        if (_generation is { } task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
            }
        }

        try
        {
            _audio?.Dispose();
            _audio = null;
            if (graphics)
            {
                _menuImage?.Dispose();
                _menuTitle?.Dispose();
                foreach (var badge in _weaponArt) badge?.Dispose();
                _city?.Dispose();
            }

            _menuImage = null;
            _menuTitle = null;
            _city = null;
        }
        finally
        {
            _cancel?.Dispose();
            if (graphics) Ui.Dispose();
        }
    }
}
