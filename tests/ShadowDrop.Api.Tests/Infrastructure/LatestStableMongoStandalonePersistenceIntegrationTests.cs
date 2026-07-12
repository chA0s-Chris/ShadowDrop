// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
namespace ShadowDrop.Tests.Infrastructure;

using NUnit.Framework;

[TestFixture]
public sealed class LatestStableMongoStandalonePersistenceIntegrationTests : MongoPersistenceIntegrationTests
{
    protected override String MongoImage => MongoDbTestImages.LatestStable;
}
