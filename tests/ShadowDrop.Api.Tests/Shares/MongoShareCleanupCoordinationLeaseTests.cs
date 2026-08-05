// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Shares;

using Chaos.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using ShadowDrop.Api.Shares;

public sealed class MongoShareCleanupCoordinationLeaseTests
{
    [Test]
    public async Task Lease_ShouldExtendThroughoutLongRunningCleanup()
    {
        var mongoLock = new ScriptedMongoLock([true, true, true]);
        await using var lease = CreateLease(mongoLock);

        await WaitForCallsAsync(mongoLock, 2);

        lease.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task Lease_ShouldRetryAfterTransientExtensionException()
    {
        var mongoLock = new ScriptedMongoLock([new TimeoutException("transient"), true]);
        await using var lease = CreateLease(mongoLock);

        await WaitForCallsAsync(mongoLock, 2);

        lease.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task Lease_ShouldStopRetryingWhenLockExpiresAfterExtensionException()
    {
        var mongoLock = new ScriptedMongoLock([ScriptedMongoLock.ExpireWithException]);
        await using var lease = CreateLease(mongoLock);

        await WaitForCallsAsync(mongoLock, 1);
        await Task.Delay(TimeSpan.FromMilliseconds(30));

        lease.IsValid.Should().BeFalse();
        mongoLock.ExtensionCalls.Should().Be(1);
    }

    [Test]
    public async Task Lease_ShouldTreatFalseExtensionAsDefinitiveOwnershipLoss()
    {
        var mongoLock = new ScriptedMongoLock([false]);
        await using var lease = CreateLease(mongoLock);

        await WaitForCallsAsync(mongoLock, 1);

        lease.IsValid.Should().BeFalse();
    }

    private static MongoShareCleanupCoordinationLease CreateLease(ScriptedMongoLock mongoLock) =>
        new(mongoLock,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(5),
            static () => { },
            NullLogger.Instance);

    private static async Task WaitForCallsAsync(ScriptedMongoLock mongoLock, Int32 calls)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (mongoLock.ExtensionCalls < calls)
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                Assert.Fail($"Expected {calls} extension calls, but observed {mongoLock.ExtensionCalls}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(5));
        }
    }

    private sealed class ScriptedMongoLock(IReadOnlyCollection<Object> outcomes) : IMongoLock
    {
        public static readonly Object ExpireWithException = new();
        private readonly Queue<Object> _outcomes = new(outcomes);

        public Int32 ExtensionCalls { get; private set; }

        public String Id { get; } = Guid.NewGuid().ToString("N");

        public Boolean IsValid { get; private set; } = true;

        public DateTime ValidUntilUtc { get; private set; } = DateTime.UtcNow.AddMinutes(1);

        public ValueTask DisposeAsync()
        {
            IsValid = false;
            return ValueTask.CompletedTask;
        }

        public Task<Boolean> TryExtendAsync(TimeSpan? leaseTime = null, CancellationToken cancellationToken = default)
        {
            ExtensionCalls++;
            var outcome = _outcomes.Count == 0 ? true : _outcomes.Dequeue();
            if (ReferenceEquals(outcome, ExpireWithException))
            {
                IsValid = false;
                ValidUntilUtc = DateTime.UtcNow;
                throw new TimeoutException("lease expired");
            }

            if (outcome is Exception exception)
            {
                throw exception;
            }

            var extended = (Boolean)outcome;
            if (!extended)
            {
                IsValid = false;
                return Task.FromResult(false);
            }

            ValidUntilUtc = DateTime.UtcNow.Add(leaseTime ?? TimeSpan.FromMinutes(1));
            return Task.FromResult(true);
        }
    }
}
