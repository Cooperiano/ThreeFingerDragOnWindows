using TouchpadControl;
using Xunit;

public class SuiteSettingsEntryTests {
    [Theory]
    [InlineData(LaunchIntent.Settings,false,true)]
    [InlineData(LaunchIntent.Settings,true,false)]
    [InlineData(LaunchIntent.Background,false,false)]
    [InlineData(LaunchIntent.Overview,false,false)]
    [InlineData(LaunchIntent.Exit,false,false)]
    public void OnlyExplicitSettingsForwardBeforeInputStartup(LaunchIntent intent,bool preview,bool expected) =>
        Assert.Equal(expected,LaunchIntentPolicy.ShouldForwardToSuite(intent,preview));
}
