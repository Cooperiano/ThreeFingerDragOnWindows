using TouchpadControl;
using Xunit;

namespace ThreeFingerDragEngine.Tests;

public sealed class StartupRegistrationPolicyTests
{
    private const string Alias = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\ThreeFingerDragOnWindows.exe";
    [Fact] public void RegisteredAliasWatchdogIsValid() =>
        Assert.True(StartupRegistrationPolicy.IsActionValid(Alias, "--watchdog", Alias, 1, true));
    [Theory]
    [InlineData("", "--watchdog", 1, true)]
    [InlineData("old-version.exe", "--watchdog", 1, true)]
    [InlineData(Alias, "", 1, true)]
    [InlineData(Alias, "--watchdog --extra", 1, true)]
    [InlineData(Alias, "--watchdog", 2, true)]
    [InlineData(Alias, "--watchdog", 1, false)]
    public void StaleOrExtraActionsAreNotAccepted(string executable, string arguments, int count, bool logon) =>
        Assert.False(StartupRegistrationPolicy.IsActionValid(executable, arguments, Alias, count, logon));
    [Fact] public void QuotedCaseInsensitiveAliasIsAccepted() =>
        Assert.True(StartupRegistrationPolicy.IsActionValid('"' + Alias.ToUpperInvariant() + '"', " --WATCHDOG ", Alias, 1, true));
}
