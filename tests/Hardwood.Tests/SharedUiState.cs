using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Tests that touch app-wide static state (the portrait cache and folder, the portrait service, app settings)
/// run one at a time in this collection, so they can't see each other's changes.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedUiState
{
    public const string Name = "Shared UI state";
}
