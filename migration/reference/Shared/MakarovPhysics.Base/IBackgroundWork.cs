namespace MakarovPhysics.Base;

/// <summary>
/// A module that runs something outside the frame loop: a worker task, a
/// cancellation token, native compute in flight.
///
/// Implemented by the few modules that need it rather than added to
/// IPhysicsModule, because the common case is a module with nothing running in
/// the background, and making eighty of them write an empty method to say so
/// would bury the handful that have something to stop.
///
/// The point of the interface is the context-loss path: when the GL context is
/// gone the host must skip glDelete, and it used to skip the entire teardown
/// with it, leaving worker threads running into process exit. Stopping work is
/// safe without a context; releasing GL objects is not, and separating them is
/// what lets the safe half always run.
/// </summary>
public interface IBackgroundWork
{
    /// <summary>
    /// Cancels and joins background work. Must be safe to call without a GL
    /// context and safe to call twice.
    /// </summary>
    void StopBackgroundWork();
}
