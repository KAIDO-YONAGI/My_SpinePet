using SpinePet.Models;
using SpinePet.Services;
using SpinePet.Tests.TestDoubles;

namespace SpinePet.Tests;

public sealed class CharacterShowCoordinatorTests
{
    [Fact]
    public async Task ConcurrentShowReusesSingleFlightAndPersistsOnce()
    {
        FakeCharacterRenderHost renderHost = new();
        TaskCompletionSource gate = new();
        int attempts = 0;
        renderHost.ShowCharacterHandler = _ =>
        {
            attempts++;
            return gate.Task;
        };
        int persistCount = 0;
        CharacterConfig character = CreateCharacter("show-1");
        List<CharacterConfig> tracked = [character];
        CharacterShowCoordinator coordinator = CreateCoordinator(
            renderHost,
            tracked,
            () => persistCount++);

        Task first = coordinator.ShowAsync(character);
        Task second = coordinator.ShowAsync(character);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, attempts);
        Assert.Equal(1, persistCount);
        Assert.True(character.Visible);
        Assert.Contains(character.Id, renderHost.VisibleCharacterIds);
    }

    [Fact]
    public async Task FailedFlightAllowsRetry()
    {
        FakeCharacterRenderHost renderHost = new();
        int attempts = 0;
        renderHost.ShowCharacterHandler = _ =>
            attempts++ == 0
                ? Task.FromException(
                    new InvalidOperationException("load failed"))
                : Task.CompletedTask;
        int persistCount = 0;
        CharacterConfig character = CreateCharacter("show-2");
        List<CharacterConfig> tracked = [character];
        CharacterShowCoordinator coordinator = CreateCoordinator(
            renderHost,
            tracked,
            () => persistCount++);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.ShowAsync(character));

        Assert.False(character.Visible);
        Assert.Equal(0, persistCount);

        await coordinator.ShowAsync(character);

        Assert.Equal(2, attempts);
        Assert.Equal(1, persistCount);
        Assert.True(character.Visible);
    }

    [Fact]
    public async Task DifferentResourceQueuesBehindActiveFlight()
    {
        FakeCharacterRenderHost renderHost = new();
        TaskCompletionSource gate = new();
        renderHost.ShowCharacterHandler = _ => gate.Task;
        int persistCount = 0;
        CharacterConfig character = CreateCharacter("show-3");
        string firstSkeleton = character.SkeletonPath;
        List<CharacterConfig> tracked = [character];
        CharacterShowCoordinator coordinator = CreateCoordinator(
            renderHost,
            tracked,
            () => persistCount++);

        Task first = coordinator.ShowAsync(character);
        character.SkeletonPath = firstSkeleton.Replace(
            "show-3",
            "show-3b");
        Task second = coordinator.ShowAsync(character);
        gate.SetResult();
        await Task.WhenAll(first, second);

        // The queued flight waits for the active load and then observes
        // the character shown, so it settles without a duplicate load.
        Assert.Equal([firstSkeleton], renderHost.ShownSkeletonPaths);
        Assert.Equal(1, persistCount);
        Assert.True(character.Visible);
    }

    [Fact]
    public async Task RemovedCharacterIsShownAgainWithNewResource()
    {
        FakeCharacterRenderHost renderHost = new();
        int persistCount = 0;
        CharacterConfig character = CreateCharacter("show-3b");
        string firstSkeleton = character.SkeletonPath;
        List<CharacterConfig> tracked = [character];
        CharacterShowCoordinator coordinator = CreateCoordinator(
            renderHost,
            tracked,
            () => persistCount++);

        await coordinator.ShowAsync(character);
        // Resource switches remove the renderer entry before re-showing.
        renderHost.RemoveCharacter(character.Id);
        character.SkeletonPath = firstSkeleton.Replace(
            "show-3b",
            "show-3c");
        await coordinator.ShowAsync(character);

        Assert.Equal(
            [firstSkeleton, character.SkeletonPath],
            renderHost.ShownSkeletonPaths);
        Assert.Equal(2, persistCount);
    }

    [Fact]
    public async Task ShownAndSettledCharacterSkipsRenderer()
    {
        FakeCharacterRenderHost renderHost = new();
        int persistCount = 0;
        CharacterConfig character = CreateCharacter("show-4");
        character.Visible = true;
        renderHost.VisibleCharacterIds.Add(character.Id);
        List<CharacterConfig> tracked = [character];
        CharacterShowCoordinator coordinator = CreateCoordinator(
            renderHost,
            tracked,
            () => persistCount++);

        await coordinator.ShowAsync(character);

        Assert.Empty(renderHost.ShownSkeletonPaths);
        Assert.Equal(0, persistCount);
    }

    [Fact]
    public async Task UntrackedCharacterIsRemovedAfterShow()
    {
        FakeCharacterRenderHost renderHost = new();
        int persistCount = 0;
        CharacterConfig character = CreateCharacter("show-5");
        List<CharacterConfig> tracked = [];
        CharacterShowCoordinator coordinator = CreateCoordinator(
            renderHost,
            tracked,
            () => persistCount++);

        await coordinator.ShowAsync(character);

        Assert.Contains(character.Id, renderHost.RemovedCharacterIds);
        Assert.Equal(0, persistCount);
    }

    [Fact]
    public async Task ClosedCoordinatorSkipsShow()
    {
        FakeCharacterRenderHost renderHost = new();
        CharacterConfig character = CreateCharacter("show-6");
        List<CharacterConfig> tracked = [character];
        CharacterShowCoordinator coordinator = new(
            renderHost,
            () => true,
            () => false,
            tracked.Contains,
            () => { });

        await coordinator.ShowAsync(character);

        Assert.Empty(renderHost.ShownSkeletonPaths);
    }

    private static CharacterShowCoordinator CreateCoordinator(
        FakeCharacterRenderHost renderHost,
        List<CharacterConfig> trackedCharacters,
        Action persist) =>
        new(
            renderHost,
            () => false,
            () => false,
            trackedCharacters.Contains,
            persist);

    private static CharacterConfig CreateCharacter(string id) => new()
    {
        Id = id,
        Name = id,
        SkeletonPath = $@"C:\res\{id}\standing.skel",
        AtlasPath = $@"C:\res\{id}\standing.atlas",
        TexturePath = $@"C:\res\{id}\standing.png"
    };
}
