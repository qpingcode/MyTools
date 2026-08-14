namespace MyTools.Host.Core.Sessions;

/// <summary>
/// A monotonically-increasing generation counter for a plugin session. Each new run attempt bumps
/// the generation; async callbacks capture the generation at issue time and, upon completion, are
/// discarded if the generation has advanced (the design's "旧回调由 generation 拒绝" rule). This
/// prevents a stale process's exit/handshake/health callbacks from mutating a new session.
/// </summary>
public sealed class GenerationGuard
{
    private int _generation;
    public int Generation => _generation;

    /// <summary>The current generation token (immutable snapshot).</summary>
    public GenerationToken Current => new(_generation);

    /// <summary>Advances the generation, invalidating all previously-issued tokens.</summary>
    public void Bump() => System.Threading.Interlocked.Increment(ref _generation);

    /// <summary>True iff <paramref name="token"/> matches the current generation.</summary>
    public bool IsCurrent(GenerationToken token) => token.Value == _generation;
}

/// <summary>Immutable snapshot of a generation value captured at issue time.</summary>
public readonly record struct GenerationToken(int Value);
