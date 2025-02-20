﻿using FluentAssertions;
using ModularPipelines.Context;
using ModularPipelines.Extensions;
using ModularPipelines.Git.Extensions;

namespace TUnit.Pipeline.Modules.Tests;

public class TestDiscoveryBeforeHookTests : TestModule
{
    protected override async Task<TestResult?> ExecuteAsync(IPipelineContext context, CancellationToken cancellationToken)
    {
        return await RunTestsWithFilter(context,
            "/*/*/TestDiscoveryBeforeTests/*",
            [
                result => result.Successful.Should().BeTrue(),
                result => result.Total.Should().Be(1),
                result => result.Passed.Should().Be(1),
                result => result.Failed.Should().Be(0),
                result => result.Skipped.Should().Be(0),
                _ => context.Git().RootDirectory.FindFile(x => x.Name.StartsWith("TestDiscoveryBeforeTests") && x.Extension == ".txt").AssertExists()
            ], cancellationToken);
    }
}
