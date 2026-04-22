using LiveStreamSound.Shared.Localization;

namespace LiveStreamSound.Shared.Tests;

/// <summary>
/// Localization dictionary: guards against missing keys, ensures DE/EN parity
/// for the user-facing strings, and verifies the live toggle fires the
/// PropertyChanged event WPF data-binding relies on.
/// </summary>
public class LocTests
{
    [Fact]
    public void Instance_Singleton_Consistent()
    {
        Assert.Same(Loc.Instance, Loc.Instance);
    }

    [Fact]
    public void Get_KnownKey_ReturnsLocalizedString()
    {
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            Assert.Equal("Verbinden", Loc.Instance.Get("Connect"));
            Loc.Instance.Language = Loc.Lang.English;
            Assert.Equal("Connect", Loc.Instance.Get("Connect"));
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Fact]
    public void Get_UnknownKey_ReturnsRawKey_AsFallback()
    {
        // By design: unknown keys return themselves so developers can see the
        // miss in the UI. Useful during development — not pretty in production.
        Assert.Equal("this.key.does.not.exist", Loc.Instance.Get("this.key.does.not.exist"));
    }

    [Fact]
    public void Indexer_WorksLikeGet()
    {
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            Assert.Equal(Loc.Instance.Get("Disconnect"), Loc.Instance["Disconnect"]);
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Fact]
    public void Toggle_FlipsGermanAndEnglish()
    {
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            Assert.True(Loc.Instance.IsGerman);
            Loc.Instance.Toggle();
            Assert.True(Loc.Instance.IsEnglish);
            Loc.Instance.Toggle();
            Assert.True(Loc.Instance.IsGerman);
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Fact]
    public void LanguageChange_FiresPropertyChanged_ForBindings()
    {
        var prev = Loc.Instance.Language;
        try
        {
            var fired = new List<string>();
            Loc.Instance.PropertyChanged += (_, e) => fired.Add(e.PropertyName!);

            Loc.Instance.Language = prev == Loc.Lang.German ? Loc.Lang.English : Loc.Lang.German;

            // "Item[]" is the wildcard signal for XAML bindings that use the indexer.
            Assert.Contains("Item[]", fired);
            Assert.Contains("Language", fired);
            Assert.Contains("IsGerman", fired);
            Assert.Contains("IsEnglish", fired);
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Fact]
    public void LanguageChange_ToSameValue_DoesNotFire()
    {
        var fired = 0;
        Loc.Instance.PropertyChanged += (_, _) => fired++;
        var current = Loc.Instance.Language;
        Loc.Instance.Language = current; // no-op
        Assert.Equal(0, fired);
    }

    [Theory]
    // Pick a handful of load-bearing keys that MUST exist — regressions here
    // immediately break UI strings.
    [InlineData("AppName.Host")]
    [InlineData("AppName.Client")]
    [InlineData("Connect")]
    [InlineData("Disconnect")]
    [InlineData("Host.StartSession")]
    [InlineData("Host.StopSession")]
    [InlineData("Host.SessionCode")]
    [InlineData("Client.Connected")]
    [InlineData("Client.Connecting")]
    [InlineData("Client.Reconnecting.Title")]
    [InlineData("Client.Reconnecting.Body")]
    [InlineData("Client.Reconnecting.Hint")]
    [InlineData("Host.ClientReconnecting")]
    [InlineData("Invite.OpenInviteDialog")]
    [InlineData("Incoming.Title")]
    [InlineData("App.Role.SendTitle")]
    [InlineData("App.Role.ReceiveTitle")]
    [InlineData("A11y.QualityGood")]
    [InlineData("A11y.QualityDisconnected")]
    public void CriticalKey_ExistsAndIsNotRawKeyFallback(string key)
    {
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            var de = Loc.Instance.Get(key);
            Loc.Instance.Language = Loc.Lang.English;
            var en = Loc.Instance.Get(key);

            Assert.False(string.IsNullOrWhiteSpace(de));
            Assert.False(string.IsNullOrWhiteSpace(en));
            // "Missing key" fallback returns the key as-is from both langs.
            // A real entry must at least have one translation that differs from
            // the raw key (or both translations be distinct from the key so we
            // catch key-only fallbacks). Legitimate case: some English words are
            // identical to the key string (e.g. key "Connect" → EN "Connect"),
            // so we only require that *both* don't simultaneously equal the key.
            Assert.False(de == key && en == key,
                $"Key '{key}' appears to hit the fallback — both DE and EN returned the raw key");
        }
        finally { Loc.Instance.Language = prev; }
    }

    [Fact]
    public void Help_Pages_NotMissing()
    {
        // Teacher manual lives in the Loc keys; if HelpHost/HelpClient go
        // missing the in-app help panel renders empty.
        var prev = Loc.Instance.Language;
        try
        {
            Loc.Instance.Language = Loc.Lang.German;
            foreach (var key in new[]
            {
                "HelpHost.Title", "HelpHost.Step1", "HelpHost.Step2", "HelpHost.Step3",
                "HelpHost.Step4", "HelpHost.Step5",
                "HelpClient.Title", "HelpClient.Step1", "HelpClient.Step2",
                "HelpClient.Step3", "HelpClient.Step4",
            })
            {
                Assert.NotEqual(key, Loc.Instance.Get(key));
            }
        }
        finally { Loc.Instance.Language = prev; }
    }
}
