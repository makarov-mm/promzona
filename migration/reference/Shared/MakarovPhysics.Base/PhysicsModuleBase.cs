namespace MakarovPhysics.Base;

/// <summary>
/// Module base: state machine plus a fixed-step accumulator.
/// A subclass implements physics (StepFixed) and rendering; Dt is FPS-independent.
/// </summary>
public abstract class PhysicsModuleBase : IPhysicsModule
{
    public abstract ModuleInfo Info { get; }
    public SimulationState State { get; private set; } = SimulationState.Stopped;

    /// <summary>Time controls by default; static modules narrow this down.</summary>
    public virtual ModuleCapabilities Capabilities => ModuleCapabilities.All;

    /// <summary>Neutral laboratory backdrop by default; space modules override.</summary>
    public virtual SceneBackground Background => SceneBackground.Laboratory;

    /// <summary>No module-owned left strip by default; see IPhysicsModule.</summary>
    public virtual float OverlayLeftInset => 0f;

    /// <summary>Integration step, seconds (fixed, must be positive).</summary>
    public double Dt
    {
        get;

        protected set
        {
            if (!(value > 0) || !double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(Dt), "Dt must be a positive finite number.");
            }

            field = value;
        }
    } = 1e-3;

    /// <summary>Time-speed multiplier (1 = real time). NaN/∞/negatives are
    /// silently ignored (the previous valid value stays) - sliders may feed
    /// transient garbage and must not throw.</summary>
    public double TimeScale
    {
        get;

        set
        {
            if (double.IsFinite(value) && value >= 0)
            {
                field = value;
            }
        }
    } = 1.0;

    /// <summary>Simulation time since Start, seconds.</summary>
    public double Time { get; protected set; }

    // The default used to be 5000, which let one frame of an
    // accidentally heavy new module block the UI thread for seconds before
    // the guard fired. 1000 covers every current module at its full TimeScale
    // range in the steady state (worst case: Dt = 2e-4 at TimeScale 5 and
    // 30 fps needs ~833). Ultra-cheap fine-step ODE modules (Kepler,
    // Brachistochrone, DoublePendulum) raise it explicitly.
    private int _maxSubSteps = 1000;

    /// <summary>Maximum sub-steps per frame - protection against the death
    /// spiral. Clamped to at least 1 so the accumulator loop always runs.</summary>
    protected int MaxSubSteps
    {
        get => _maxSubSteps;
        set => _maxSubSteps = Math.Max(1, value);
    }

    protected IModuleContext? Context { get; private set; }

    /// <summary>Translation by key (no context → the key itself).</summary>
    protected string Tr(string key) => Context?.Loc[key] ?? key;

    private double _acc;

    public virtual void Initialize(IModuleContext context)
    {
        Context = context;
        ResetCore();
    }

    public virtual void Resize(int width, int height) { }

    public void Update(double realDt, InputState input)
    {
        OnInput(input);

        if (State == SimulationState.Running)
        {
            if (!double.IsFinite(realDt) || realDt < 0) realDt = 0;
            _acc += Math.Min(realDt, 0.05) * TimeScale; // clamp at 50 ms
            int guard = 0, steps = 0;

            // StepFixed may call Pause()/Stop() (e.g. a projectile landed) -
            // the state check stops the loop right there, on the final step
            while (State == SimulationState.Running && _acc >= Dt && guard++ < MaxSubSteps)
            {
                _acc -= Dt;
                StepFixed(Dt);
                steps++;
                // Stop() inside StepFixed resets Time and the accumulator -
                // they must not be touched afterwards
                if (State == SimulationState.Stopped) break;
                Time += Dt;
            }

            // if we hit the sub-step guard, drop the backlog - trying to catch
            // up with old time for many frames only makes the stall worse
            if (guard >= MaxSubSteps) _acc = Math.Min(_acc, Dt);

            // charts sample once per frame, and only when physics advanced -
            // otherwise low TimeScale floods series with duplicate points
            if (steps > 0 && State != SimulationState.Stopped)
            {
                OnAfterSteps(steps, steps * Dt);
            }
        }

        // visual-only animation gets the REAL frame time (clamped, unscaled by
        // TimeScale): auto-orbit cameras, effects and fades must run at the
        // same speed on a 30 Hz laptop and a 144 Hz monitor
        double visualDt = double.IsFinite(realDt) && realDt > 0 ? Math.Min(realDt, 0.05) : 0;
        VisualDt = visualDt;
        OnVisualFrame(visualDt);
        OnUiFrame();
    }

    /// <summary>
    /// The real frame time of the last Update, clamped, unscaled by TimeScale.
    ///
    /// The same number <see cref="OnVisualFrame"/> is handed, kept where
    /// Render() can reach it. Auto-exposure, easing cameras and follow filters
    /// are written in the render pass because that is where the value they
    /// follow is computed, and without this they had nothing to be
    /// frame-rate-independent AGAINST - so they were all written as a fixed
    /// fraction per frame and all ran at the speed of the monitor.
    /// </summary>
    protected double VisualDt { get; private set; }

    /// <summary>One physics step of fixed duration.</summary>
    protected abstract void StepFixed(double dt);

    /// <summary>React to input (scene mouse, keys) - before the physics steps.</summary>
    protected virtual void OnInput(InputState input) { }

    /// <summary>After a batch of sub-steps (update charts once per frame, not per sub-step).</summary>
    protected virtual void OnAfterSteps() { }

    /// <summary>
    /// Same moment, richer contract: how many fixed steps ran and how much
    /// simulated time they covered. Chart/sampling clocks must add
    /// <paramref name="simulatedDt"/>, not a single Dt - adding one Dt after a
    /// batch of ten made every such clock run ten times slower than the model
    /// (the audited cross-module defect). The default forwards to the
    /// parameterless overload so existing modules keep compiling.
    /// </summary>
    protected virtual void OnAfterSteps(int steps, double simulatedDt) => OnAfterSteps();

    /// <summary>Every frame, in any state, with the REAL (clamped) frame time -
    /// for visual-only animation: auto-orbit cameras, particle effects, fades.
    /// Frame-rate independent by construction; never use a fixed 1/60 here.</summary>
    protected virtual void OnVisualFrame(double realDt) { }

    /// <summary>Every frame, in any state - for live labels in the UI.</summary>
    protected virtual void OnUiFrame() { }

    /// <summary>Return the model to its initial conditions.</summary>
    protected abstract void ResetCore();

    public abstract void Render();
    public abstract void BuildUi(object uiRoot);

    /// <summary>Resume (or begin) running. Does NOT rebuild the scene: after
    /// Initialize or Stop the model is already at its initial conditions, so a
    /// second ResetCore here would only repeat heavy scene construction (the
    /// old Stop→ResetCore→Start→ResetCore double-reset the infrastructure
    /// repeated across some 50 call sites).</summary>
    public void Start()
    {
        // Resuming keeps the accumulator: at a pause it holds the part of a
        // fixed step that had not been taken yet (0 <= acc < Dt), and throwing
        // that away shifts the phase and quietly loses up to one step of
        // simulated time on EVERY pause. Only a fresh run starts from zero.
        if (State == SimulationState.Stopped)
        {
            _acc = 0;

            // A FRESH RUN REBUILDS THE SCENE. Stop() already rebuilt it once,
            // but a reader stops the module precisely in order to change
            // something before running again - the star count, the tooth
            // count, the release angle - and those sliders are read by
            // ResetCore. Without this, the new value sits in the panel and the
            // old scene starts anyway.
            //
            // Resuming from a PAUSE does not rebuild: that would throw away
            // the run the reader was watching, which is the opposite of what
            // pause means.
            ResetCore();
            Time = 0;
        }

        State = SimulationState.Running;
    }

    public void Pause()
    {
        if (State == SimulationState.Running)
        {
            State = SimulationState.Paused;
        }
    }

    /// <summary>Stop AND return to the initial scene (the toolbar contract:
    /// Stop shows the freshly built experiment, ready for the next run).</summary>
    public void Stop()
    {
        State = SimulationState.Stopped;
        ResetCore();
        Time = 0;
        _acc = 0;
    }

    /// <summary>Rebuild the initial scene and run - with exactly ONE ResetCore.
    /// This is what parameter callbacks should call instead of the old
    /// Stop(); Start(); pair, which reset the model twice.</summary>
    public void Restart()
    {
        State = SimulationState.Stopped; // so StepFixed's Stop() checks stay consistent
        ResetCore();
        Time = 0;
        _acc = 0;
        State = SimulationState.Running;
    }

    /// <summary>Begin a NEW EXPERIMENT with the current parameters: one
    /// ResetCore, Time and the accumulator back to zero, and the
    /// run/pause/stop state KEPT as it is - unlike Restart(), which always
    /// ends Running and so cannot serve a slider dragged on a paused scene.
    ///
    /// This is the call for "new seed" buttons and parameter callbacks. A
    /// bare ResetCore() there zeroes the module's own progress but leaves the
    /// base Time running, so the next StepFixed recomputes the reveal from
    /// the old clock and the "fresh" experiment opens already finished -
    /// after thirty seconds of watching, "new samples" showed all hundred
    /// intervals in one frame (audit 09.09, F06).</summary>
    protected void NewExperiment()
    {
        SimulationState keep = State;
        State = SimulationState.Stopped; // so ResetCore-adjacent state checks stay consistent
        ResetCore();
        Time = 0;
        _acc = 0;
        State = keep;
    }

    public void StepOnce()
    {
        if (State == SimulationState.Running) return;
        State = SimulationState.Paused;
        StepFixed(Dt);

        // A module may finish and call Stop() from inside its step. Stop()
        // already reset the model, time and accumulator; do not sample that
        // reset state or advance the clock afterwards.
        if (State == SimulationState.Stopped)
            return;

        Time += Dt;
        // The RICH overload, with steps = 1: ten modules override only
        // OnAfterSteps(int, double) for their charts, probes and derived
        // state, and the old parameterless call here silently skipped all of
        // it - the physics advanced one step while the accompanying state
        // stood still (audit 29.07, P0). The default rich overload chains to
        // the parameterless one, so legacy modules are unaffected.
        OnAfterSteps(1, Dt);
    }

    public void Reset() => Stop();
    public virtual void Dispose() { }
}
