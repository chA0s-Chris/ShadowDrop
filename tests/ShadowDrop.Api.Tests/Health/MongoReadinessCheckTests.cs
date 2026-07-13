// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Health;

using Chaos.Mongo;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;
using ShadowDrop.Api.Health;

[TestFixture]
public sealed class MongoReadinessCheckTests
{
    [Test]
    public async Task IsReadyAsync_ShouldReturnTrue_WhenPingSucceeds()
    {
        var readinessCheck = CreateCheck(static _ => Task.FromResult(new BsonDocument("ok", 1)));

        var isReady = await readinessCheck.IsReadyAsync(CancellationToken.None);

        isReady.Should().BeTrue();
    }

    [Test]
    public async Task IsReadyAsync_ShouldReturnFalse_WhenPingFails()
    {
        var readinessCheck = CreateCheck(static _ => throw new MongoException("no reachable primary"));

        var isReady = await readinessCheck.IsReadyAsync(CancellationToken.None);

        isReady.Should().BeFalse();
    }

    [Test]
    public async Task IsReadyAsync_ShouldReturnFalse_WhenPingExceedsCheckTimeout()
    {
        var readinessCheck = CreateCheck(static async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new();
        }, TimeSpan.FromMilliseconds(20));

        var act = async () => await readinessCheck.IsReadyAsync(CancellationToken.None);

        (await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5))).Which.Should().BeFalse();
    }

    [Test]
    public async Task IsReadyAsync_ShouldPropagateCallerCancellation()
    {
        using var callerCancellation = new CancellationTokenSource();
        await callerCancellation.CancelAsync();
        var readinessCheck = CreateCheck(static async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        });

        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await readinessCheck.IsReadyAsync(callerCancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static MongoReadinessCheck CreateCheck(Func<CancellationToken, Task<BsonDocument>> ping, TimeSpan? checkTimeout = null) =>
        new(new StubMongoHelper(new StubMongoDatabase(ping)))
        {
            CheckTimeout = checkTimeout ?? MongoReadinessCheck.DefaultCheckTimeout
        };

    private sealed class StubMongoHelper(IMongoDatabase database) : IMongoHelper
    {
        public IMongoClient Client => throw new NotImplementedException();

        public IMongoDatabase Database => database;

        public IMongoCollection<TDocument> GetCollection<TDocument>(MongoCollectionSettings? settings = null) => throw new NotImplementedException();

        public Task<IMongoLock?> TryAcquireLockAsync(String lockName, TimeSpan? leaseTime = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    /// <summary>
    /// Manual test double for the wide MongoDB driver interface: only the parameterless-session
    /// <see cref="RunCommandAsync{TResult}(Command{TResult}, ReadPreference?, CancellationToken)"/> overload used by
    /// <see cref="MongoReadinessCheck"/> is functional; every other member throws.
    /// </summary>
    private sealed class StubMongoDatabase(Func<CancellationToken, Task<BsonDocument>> ping) : IMongoDatabase
    {
        public IMongoClient Client => throw new NotImplementedException();

        public DatabaseNamespace DatabaseNamespace => throw new NotImplementedException();

        public MongoDatabaseSettings Settings => throw new NotImplementedException();

        public IAsyncCursor<TResult> Aggregate<TResult>(PipelineDefinition<NoPipelineInput, TResult> pipeline, AggregateOptions? options = null,
                                                        CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncCursor<TResult> Aggregate<TResult>(IClientSessionHandle session, PipelineDefinition<NoPipelineInput, TResult> pipeline,
                                                        AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(PipelineDefinition<NoPipelineInput, TResult> pipeline, AggregateOptions? options = null,
                                                                   CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IAsyncCursor<TResult>> AggregateAsync<TResult>(IClientSessionHandle session, PipelineDefinition<NoPipelineInput, TResult> pipeline,
                                                                   AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void AggregateToCollection<TResult>(PipelineDefinition<NoPipelineInput, TResult> pipeline, AggregateOptions? options = null,
                                                   CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public void AggregateToCollection<TResult>(IClientSessionHandle session, PipelineDefinition<NoPipelineInput, TResult> pipeline,
                                                   AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task AggregateToCollectionAsync<TResult>(PipelineDefinition<NoPipelineInput, TResult> pipeline, AggregateOptions? options = null,
                                                        CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task AggregateToCollectionAsync<TResult>(IClientSessionHandle session, PipelineDefinition<NoPipelineInput, TResult> pipeline,
                                                        AggregateOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void CreateCollection(String name, CreateCollectionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void CreateCollection(IClientSessionHandle session, String name, CreateCollectionOptions? options = null,
                                     CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task CreateCollectionAsync(String name, CreateCollectionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task CreateCollectionAsync(IClientSessionHandle session, String name, CreateCollectionOptions? options = null,
                                          CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public void CreateView<TDocument, TResult>(String viewName, String viewOn, PipelineDefinition<TDocument, TResult> pipeline,
                                                   CreateViewOptions<TDocument>? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void CreateView<TDocument, TResult>(IClientSessionHandle session, String viewName, String viewOn,
                                                   PipelineDefinition<TDocument, TResult> pipeline, CreateViewOptions<TDocument>? options = null,
                                                   CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task CreateViewAsync<TDocument, TResult>(String viewName, String viewOn, PipelineDefinition<TDocument, TResult> pipeline,
                                                        CreateViewOptions<TDocument>? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task CreateViewAsync<TDocument, TResult>(IClientSessionHandle session, String viewName, String viewOn,
                                                        PipelineDefinition<TDocument, TResult> pipeline, CreateViewOptions<TDocument>? options = null,
                                                        CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public void DropCollection(String name, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public void DropCollection(String name, DropCollectionOptions options, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void DropCollection(IClientSessionHandle session, String name, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void DropCollection(IClientSessionHandle session, String name, DropCollectionOptions options, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DropCollectionAsync(String name, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task DropCollectionAsync(String name, DropCollectionOptions options, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DropCollectionAsync(IClientSessionHandle session, String name, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DropCollectionAsync(IClientSessionHandle session, String name, DropCollectionOptions options,
                                        CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IMongoCollection<TDocument> GetCollection<TDocument>(String name, MongoCollectionSettings? settings = null) =>
            throw new NotImplementedException();

        public IAsyncCursor<String> ListCollectionNames(ListCollectionNamesOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncCursor<String> ListCollectionNames(IClientSessionHandle session, ListCollectionNamesOptions? options = null,
                                                        CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IAsyncCursor<String>> ListCollectionNamesAsync(ListCollectionNamesOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IAsyncCursor<String>> ListCollectionNamesAsync(IClientSessionHandle session, ListCollectionNamesOptions? options = null,
                                                                   CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IAsyncCursor<BsonDocument> ListCollections(ListCollectionsOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncCursor<BsonDocument> ListCollections(IClientSessionHandle session, ListCollectionsOptions? options = null,
                                                          CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IAsyncCursor<BsonDocument>> ListCollectionsAsync(ListCollectionsOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IAsyncCursor<BsonDocument>> ListCollectionsAsync(IClientSessionHandle session, ListCollectionsOptions? options = null,
                                                                     CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public void RenameCollection(String oldName, String newName, RenameCollectionOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void RenameCollection(IClientSessionHandle session, String oldName, String newName, RenameCollectionOptions? options = null,
                                     CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task RenameCollectionAsync(String oldName, String newName, RenameCollectionOptions? options = null,
                                          CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task RenameCollectionAsync(IClientSessionHandle session, String oldName, String newName, RenameCollectionOptions? options = null,
                                          CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public TResult RunCommand<TResult>(Command<TResult> command, ReadPreference? readPreference = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public TResult RunCommand<TResult>(IClientSessionHandle session, Command<TResult> command, ReadPreference? readPreference = null,
                                           CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<TResult> RunCommandAsync<TResult>(Command<TResult> command, ReadPreference? readPreference = null,
                                                      CancellationToken cancellationToken = default) =>
            (Task<TResult>)(Object)ping(cancellationToken);

        public Task<TResult> RunCommandAsync<TResult>(IClientSessionHandle session, Command<TResult> command, ReadPreference? readPreference = null,
                                                      CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public IChangeStreamCursor<TResult> Watch<TResult>(PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
                                                           ChangeStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IChangeStreamCursor<TResult> Watch<TResult>(IClientSessionHandle session,
                                                           PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
                                                           ChangeStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
                                                                      ChangeStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IChangeStreamCursor<TResult>> WatchAsync<TResult>(IClientSessionHandle session,
                                                                      PipelineDefinition<ChangeStreamDocument<BsonDocument>, TResult> pipeline,
                                                                      ChangeStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IMongoDatabase WithReadConcern(ReadConcern readConcern) => throw new NotImplementedException();

        public IMongoDatabase WithReadPreference(ReadPreference readPreference) => throw new NotImplementedException();

        public IMongoDatabase WithWriteConcern(WriteConcern writeConcern) => throw new NotImplementedException();
    }
}
