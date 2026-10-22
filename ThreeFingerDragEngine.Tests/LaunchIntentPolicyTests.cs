using TouchpadControl;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public sealed class LaunchIntentPolicyTests
{
    [Theory]
    [InlineData("--exit")]
    [InlineData("--EXIT")]
    public void ExplicitExitWinsOverBackgroundAndSettings(string argument) =>
        Assert.Equal("Exit", LaunchIntentPolicy.Resolve(new[] { "--background", "--preview", argument }, true).ToString());
    [Fact] public void ExplicitFirstLaunchShowsSettings() =>
        Assert.Equal(LaunchIntent.Settings, LaunchIntentPolicy.Resolve(Array.Empty<string>()));
    [Theory]
    [InlineData("--background")]
    [InlineData("--BACKGROUND")]
    [InlineData("--watchdog")]
    public void RecoveryAndLoginRemainQuiet(string argument) =>
        Assert.Equal(LaunchIntent.Background, LaunchIntentPolicy.Resolve(new[] { argument }));
    [Fact] public void LegacyStartupActivationRemainsQuiet() =>
        Assert.Equal(LaunchIntent.Background, LaunchIntentPolicy.Resolve(Array.Empty<string>(), true));
    [Fact] public void ElevatedBackgroundRestartRemainsQuiet() =>
        Assert.Equal(LaunchIntent.Background, LaunchIntentPolicy.Resolve(new[] { "FromElevator", "--background" }));
    [Fact] public void ExplicitOverviewDoesNotShowSettings() =>
        Assert.Equal(LaunchIntent.Overview, LaunchIntentPolicy.Resolve(new[] { "--mission-control" }));
    [Fact] public void PreviewCanExplicitlyShowSettingsDuringStartup() =>
        Assert.Equal(LaunchIntent.Settings, LaunchIntentPolicy.Resolve(new[] { "--preview" }, true));
}
