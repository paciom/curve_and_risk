using System.Security.Cryptography;

namespace CurveRisk.Workflows;

/// <summary>
/// Holds what a paused run needs in order to continue later, in another call. A checkpoint can be
/// taken once: taking it removes it, so the same pause cannot be resumed twice.
/// </summary>
public interface ICheckpointStore<TCheckpoint>
    where TCheckpoint : class
{
    /// <summary>Stores the checkpoint and returns the id it can be taken with.</summary>
    string Save(TCheckpoint checkpoint);

    /// <summary>Removes and returns the checkpoint, or null when the id is unknown, already taken or expired.</summary>
    TCheckpoint? Take(string id);
}

/// <param name="Capacity">Only this many of the most recent checkpoints are kept.</param>
/// <param name="Lifetime">A checkpoint older than this can no longer be taken.</param>
public sealed record CheckpointLimits(int Capacity, TimeSpan Lifetime)
{
    public static CheckpointLimits Default { get; } = new(Capacity: 100, Lifetime: TimeSpan.FromMinutes(15));
}

/// <summary>
/// Checkpoints in process memory: they do not survive a restart and are not shared between instances.
/// The store is bounded in number and in time, so paused runs that nobody resumes cannot grow it
/// without limit, and a decision cannot be made on a run long after it was shown.
/// </summary>
public sealed class InMemoryCheckpointStore<TCheckpoint> : ICheckpointStore<TCheckpoint>
    where TCheckpoint : class
{
    private const int IdBytes = 16;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Saved> _checkpoints = new(StringComparer.Ordinal);
    private readonly Queue<string> _oldestFirst = new();
    private readonly CheckpointLimits _limits;
    private readonly TimeProvider _time;

    public InMemoryCheckpointStore(CheckpointLimits? limits = null, TimeProvider? timeProvider = null)
    {
        _limits = limits ?? CheckpointLimits.Default;
        _time = timeProvider ?? TimeProvider.System;
        if (_limits.Capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), _limits.Capacity, "The store must hold at least one checkpoint.");
        }
    }

    public string Save(TCheckpoint checkpoint)
    {
        // The id is the only thing a caller needs in order to resume, so it must not be guessable.
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(IdBytes));
        lock (_gate)
        {
            _checkpoints[id] = new Saved(checkpoint, _time.GetUtcNow());
            _oldestFirst.Enqueue(id);
            while (_oldestFirst.Count > _limits.Capacity)
            {
                _checkpoints.Remove(_oldestFirst.Dequeue());
            }
        }

        return id;
    }

    public TCheckpoint? Take(string id)
    {
        lock (_gate)
        {
            if (!_checkpoints.Remove(id, out var saved))
            {
                return null;
            }

            return _time.GetUtcNow() - saved.SavedUtc <= _limits.Lifetime ? saved.Checkpoint : null;
        }
    }

    private sealed record Saved(TCheckpoint Checkpoint, DateTimeOffset SavedUtc);
}
