using SpinePet.Models;
using SpinePet.Rendering;
using SpinePet.Services;
using SpinePet.Tests.TestDoubles;

namespace SpinePet.Tests;

public sealed class BattleInteractionControllerTests
{
    [Fact]
    public async Task ModeAndBattleStateTransitionsReachRenderer()
    {
        (BattleInteractionController controller, FakeCharacterRenderHost
            renderHost, CharacterConfig character) = CreateController();

        Assert.Equal(
            CharacterDisplayModes.Normal,
            controller.GetDisplayMode(character.Id));
        Assert.Equal(
            CharacterBattleStates.Cover,
            controller.GetBattleState(character.Id));

        Assert.True(await controller.SetDisplayModeAsync(
            character,
            CharacterDisplayModes.Battle));
        Assert.Equal(
            CharacterDisplayModes.Battle,
            controller.GetDisplayMode(character.Id));
        Assert.Equal(
            CharacterBattleStates.Cover,
            renderHost.ResourceStateChanges[^1].State);

        Assert.True(await controller.SetBattleStateAsync(
            character,
            CharacterBattleStates.Aim));
        Assert.Equal(
            CharacterBattleStates.Aim,
            controller.GetBattleState(character.Id));
        Assert.Equal(
            CharacterBattleStates.Aim,
            renderHost.ResourceStateChanges[^1].State);
    }

    [Fact]
    public async Task RightHoldPlaysAimSequenceAndReleaseReloadsCover()
    {
        (BattleInteractionController controller, FakeCharacterRenderHost
            renderHost, CharacterConfig character) =
            CreateController(rightHoldThresholdMs: 30);
        await controller.SetDisplayModeAsync(
            character,
            CharacterDisplayModes.Battle);
        renderHost.ResourceStateChanges.Clear();
        renderHost.AnimationSequences.Clear();

        controller.HandleRightPressed(character.Id);
        await WaitUntilAsync(() =>
            renderHost.ResourceStateChanges.Any(change =>
                change.State == CharacterBattleStates.Aim));

        Assert.Equal(
            ["to_aim"],
            renderHost.AnimationSequences[^1].Animations);
        Assert.True(renderHost.AnimationSequences[^1].LoopLast);
        Assert.Equal(
            "aim_idle",
            renderHost.AnimationSequences[^1].RestoreAnimation);
        Assert.Equal(
            ["aim_fire", "aim_fire_hair", "aim_fire_hip"],
            renderHost.AnimationSequences[^1].BattleLayers
                .Select(effect => effect.Animation));

        controller.HandleRightReleased(character.Id);
        await WaitUntilAsync(() =>
            renderHost.ResourceStateChanges.Any(change =>
                change.State == CharacterBattleStates.Cover));

        Assert.Equal(
            ["to_cover", "cover_reload"],
            renderHost.AnimationSequences[^1].Animations);
        Assert.False(renderHost.AnimationSequences[^1].LoopLast);
        Assert.Equal(
            "cover_idle",
            renderHost.AnimationSequences[^1].RestoreAnimation);
    }

    [Fact]
    public async Task ShortRightClickInBattleRaisesPanelWithoutFire()
    {
        (BattleInteractionController controller, FakeCharacterRenderHost
            renderHost, CharacterConfig character) =
            CreateController(rightHoldThresholdMs: 500);
        await controller.SetDisplayModeAsync(
            character,
            CharacterDisplayModes.Battle);
        renderHost.ResourceStateChanges.Clear();
        renderHost.AnimationSequences.Clear();
        int rightClicks = 0;
        controller.CharacterRightClicked += _ => rightClicks++;

        controller.HandleRightPressed(character.Id);
        await Task.Delay(50);
        controller.HandleRightReleased(character.Id);
        await Task.Delay(100);

        Assert.Equal(1, rightClicks);
        Assert.DoesNotContain(
            renderHost.ResourceStateChanges,
            change => change.State == CharacterBattleStates.Aim);
        Assert.Empty(renderHost.AnimationSequences);
    }

    [Fact]
    public async Task RightClickInNormalModeRaisesPanelEvent()
    {
        (BattleInteractionController controller, _, CharacterConfig
            character) = CreateController(rightHoldThresholdMs: 500);
        int rightClicks = 0;
        controller.CharacterRightClicked += _ => rightClicks++;

        controller.HandleRightPressed(character.Id);
        controller.HandleRightReleased(character.Id);

        Assert.Equal(1, rightClicks);
    }

    [Fact]
    public async Task ReleaseAfterSwitchToNormalDoesNotQueueReload()
    {
        (BattleInteractionController controller, FakeCharacterRenderHost
            renderHost, CharacterConfig character) =
            CreateController(rightHoldThresholdMs: 30);
        await controller.SetDisplayModeAsync(
            character,
            CharacterDisplayModes.Battle);
        renderHost.ResourceStateChanges.Clear();
        renderHost.AnimationSequences.Clear();

        controller.HandleRightPressed(character.Id);
        await WaitUntilAsync(() =>
            renderHost.ResourceStateChanges.Any(change =>
                change.State == CharacterBattleStates.Aim));

        Assert.True(await controller.SetDisplayModeAsync(
            character,
            CharacterDisplayModes.Normal));
        int stateChangesAfterNormal = renderHost
            .ResourceStateChanges.Count;

        controller.HandleRightReleased(character.Id);
        await Task.Delay(150);

        Assert.Equal(
            CharacterDisplayModes.Normal,
            controller.GetDisplayMode(character.Id));
        Assert.Equal(
            stateChangesAfterNormal,
            renderHost.ResourceStateChanges.Count);
        Assert.DoesNotContain(
            renderHost.AnimationSequences,
            sequence => sequence.Animations.Contains("cover_reload"));
    }

    [Fact]
    public async Task HiddenCharacterIsMadeVisibleBeforeBattlePreload()
    {
        FakeCharacterRenderHost renderHost = new();
        CharacterConfig character = CreateBattleCharacter("battle-hidden");
        character.Visible = false;
        int ensureCalls = 0;
        BattleInteractionController controller = new(
            renderHost,
            _ =>
            {
                ensureCalls++;
                character.Visible = true;
                return Task.CompletedTask;
            },
            () => new BattleRulesConfig(),
            id => id == character.Id ? character : null);

        Assert.True(await controller.SetDisplayModeAsync(
            character,
            CharacterDisplayModes.Battle));

        Assert.Equal(1, ensureCalls);
        Assert.Equal(1, renderHost.BattlePreloadCount);
    }

    [Fact]
    public async Task CloseCancelsPendingHoldBeforeThreshold()
    {
        (BattleInteractionController controller, FakeCharacterRenderHost
            renderHost, CharacterConfig character) =
            CreateController(rightHoldThresholdMs: 60);
        await controller.SetDisplayModeAsync(
            character,
            CharacterDisplayModes.Battle);
        renderHost.ResourceStateChanges.Clear();

        controller.HandleRightPressed(character.Id);
        controller.Close();
        await Task.Delay(200);

        Assert.DoesNotContain(
            renderHost.ResourceStateChanges,
            change => change.State == CharacterBattleStates.Aim);
    }

    private static (
        BattleInteractionController Controller,
        FakeCharacterRenderHost RenderHost,
        CharacterConfig Character) CreateController(
            int rightHoldThresholdMs =
                BattleRulesConfig.DefaultRightHoldThresholdMs)
    {
        FakeCharacterRenderHost renderHost = new();
        CharacterConfig character = CreateBattleCharacter("battle-direct");
        character.Visible = true;
        BattleRulesConfig rules = new()
        {
            RightHoldThresholdMs = rightHoldThresholdMs
        };
        BattleInteractionController controller = new(
            renderHost,
            _ => Task.CompletedTask,
            () => rules,
            id => id == character.Id ? character : null);
        return (controller, renderHost, character);
    }

    internal static CharacterConfig CreateBattleCharacter(string id)
    {
        return new CharacterConfig
        {
            Id = id,
            Name = "Rapi",
            SkeletonPath = $@"C:\res\{id}\standing.skel",
            AtlasPath = $@"C:\res\{id}\standing.atlas",
            TexturePath = $@"C:\res\{id}\standing.png",
            Visible = false,
            Battle = new CharacterBattleConfig
            {
                Aim = new CharacterBattleResourceConfig
                {
                    SkeletonPath = $@"C:\res\{id}\aim.skel",
                    AtlasPath = $@"C:\res\{id}\aim.atlas",
                    TexturePath = $@"C:\res\{id}\aim.png"
                },
                Cover = new CharacterBattleResourceConfig
                {
                    SkeletonPath = $@"C:\res\{id}\cover.skel",
                    AtlasPath = $@"C:\res\{id}\cover.atlas",
                    TexturePath = $@"C:\res\{id}\cover.png"
                },
                Animations = new CharacterBattleAnimationsConfig
                {
                    AimIdle = "aim_idle",
                    ToAim = "to_aim",
                    AimFireLayers =
                    [
                        new CharacterBattleLayerConfig
                        {
                            Animation = "aim_fire"
                        },
                        new CharacterBattleLayerConfig
                        {
                            Animation = "aim_fire_hair"
                        },
                        new CharacterBattleLayerConfig
                        {
                            Animation = "aim_fire_hip"
                        }
                    ],
                    CoverIdle = "cover_idle",
                    ToCover = "to_cover",
                    ReloadSequence = ["cover_reload"]
                }
            }
        };
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        int timeoutMilliseconds = 2000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(
            timeoutMilliseconds);
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    "The expected asynchronous state was not reached.");
            }

            await Task.Delay(10);
        }
    }
}
