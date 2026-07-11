// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> whose timers fire only when the test advances time,
/// so timeout behavior can be exercised without wall-clock waits.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = utcNow;

    public void Advance(TimeSpan delta)
    {
        ManualTimer[] due;
        lock (_gate)
        {
            _utcNow += delta;
            due = _timers.Where(timer => timer.DueAt <= _utcNow).ToArray();
            foreach (var timer in due)
            {
                _timers.Remove(timer);
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, Object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var timer = new ManualTimer(this, callback, state, _utcNow + dueTime);
            _timers.Add(timer);
            return timer;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider provider, TimerCallback callback, Object? state, DateTimeOffset dueAt) : ITimer
    {
        public DateTimeOffset DueAt { get; private set; } = dueAt;

        public void Fire() => callback(state);

        public ValueTask DisposeAsync()
        {
            provider.Remove(this);
            return ValueTask.CompletedTask;
        }

        public void Dispose() => provider.Remove(this);

        public Boolean Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = provider.GetUtcNow() + dueTime;
            return true;
        }
    }
}
