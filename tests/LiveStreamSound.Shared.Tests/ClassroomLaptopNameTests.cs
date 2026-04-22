using LiveStreamSound.Shared.Session;

namespace LiveStreamSound.Shared.Tests;

public class ClassroomLaptopNameTests
{
    [Theory]
    [InlineData("HP-KB-017", LaptopCategory.Klassenraum, "017", null)]
    [InlineData("HP-KB-018-2", LaptopCategory.Klassenraum, "018", 2)]
    [InlineData("LEN-KB-065-2", LaptopCategory.Klassenraum, "065", 2)]
    [InlineData("HP-WERK-038", LaptopCategory.Werkstatt, "038", null)]
    [InlineData("R072", LaptopCategory.Raum, "072", null)]
    [InlineData("DESKTOP-A1B2C3", LaptopCategory.Sonstige, "", null)]
    [InlineData("PC-UNKNOWN", LaptopCategory.Sonstige, "", null)]
    [InlineData("", LaptopCategory.Sonstige, "", null)]
    public void Classify_MatchesExpectedPattern(
        string hostname, LaptopCategory cat, string room, int? device)
    {
        var p = ClassroomLaptopName.Classify(hostname);
        Assert.Equal(cat, p.Category);
        Assert.Equal(room, p.Room);
        Assert.Equal(device, p.DeviceIndex);
    }

    [Theory]
    [InlineData("HP-KB-017", "Raum 017")]
    [InlineData("HP-KB-018-2", "Raum 018 (Gerät 2)")]
    [InlineData("HP-WERK-038", "Werkstatt 038")]
    [InlineData("R072", "Raum 072")]
    [InlineData("DESKTOP-A1B2C3", "DESKTOP-A1B2C3")]
    public void FriendlyName_GermanRendering(string hostname, string expected) =>
        Assert.Equal(expected, ClassroomLaptopName.FriendlyName(hostname));

    [Theory]
    [InlineData("HP-KB-017", "Room 017")]
    [InlineData("HP-KB-018-2", "Room 018 (device 2)")]
    [InlineData("HP-WERK-038", "Workshop 038")]
    [InlineData("R072", "Room 072")]
    public void FriendlyNameEnglish_Rendering(string hostname, string expected) =>
        Assert.Equal(expected, ClassroomLaptopName.FriendlyNameEnglish(hostname));

    [Fact]
    public void Classify_NullHostname_IsSonstige()
    {
        var p = ClassroomLaptopName.Classify(null!);
        Assert.Equal(LaptopCategory.Sonstige, p.Category);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Classify_WhitespaceOnlyHostname_IsSonstige(string hostname)
    {
        var p = ClassroomLaptopName.Classify(hostname);
        Assert.Equal(LaptopCategory.Sonstige, p.Category);
    }

    [Theory]
    [InlineData("hp-kb-017")]  // lowercase
    [InlineData("Hp-Kb-017")]  // mixed case
    public void Classify_CategoryToken_IsCaseInsensitive(string hostname)
    {
        var p = ClassroomLaptopName.Classify(hostname);
        Assert.Equal(LaptopCategory.Klassenraum, p.Category);
        Assert.Equal("017", p.Room);
    }

    [Theory]
    [InlineData("r072")]
    [InlineData("R072")]
    [InlineData("r1234")]
    public void Classify_ShortPattern_CaseInsensitive(string hostname)
    {
        var p = ClassroomLaptopName.Classify(hostname);
        Assert.Equal(LaptopCategory.Raum, p.Category);
    }

    [Theory]
    [InlineData("HP-KB-1")]      // room too short (needs 2-4 digits)
    [InlineData("HP-KB-12345")]  // room too long (>4 digits)
    [InlineData("HP-KB-01A")]    // non-numeric room
    [InlineData("HP-KB--2")]     // missing room
    [InlineData("R7")]           // short-pattern too short
    [InlineData("R12345")]       // short-pattern too long
    [InlineData("room072")]      // wrong prefix (shorthand form expects uppercase R followed by 2-4 digits, case-insensitive)
    public void Classify_MalformedInputs_FallToSonstige(string hostname)
    {
        var p = ClassroomLaptopName.Classify(hostname);
        Assert.Equal(LaptopCategory.Sonstige, p.Category);
    }

    [Fact]
    public void Classify_PreservesOriginalHostname()
    {
        var p = ClassroomLaptopName.Classify("HP-KB-017");
        Assert.Equal("HP-KB-017", p.OriginalHostName);
    }

    [Fact]
    public void Classify_CapturesVendorInFullPattern()
    {
        Assert.Equal("HP", ClassroomLaptopName.Classify("HP-KB-017").Vendor);
        Assert.Equal("LEN", ClassroomLaptopName.Classify("LEN-KB-065-2").Vendor);
    }

    [Fact]
    public void TryParse_ReturnsTrueForRecognized()
    {
        Assert.True(ClassroomLaptopName.TryParse("HP-KB-017", out var p));
        Assert.Equal(LaptopCategory.Klassenraum, p.Category);
        Assert.Equal("017", p.Room);
    }

    [Fact]
    public void TryParse_ReturnsFalseForUnknown()
    {
        Assert.False(ClassroomLaptopName.TryParse("SOMETHING-RANDOM", out _));
    }

    [Theory]
    [InlineData(LaptopCategory.Klassenraum, true, "Klassenräume")]
    [InlineData(LaptopCategory.Klassenraum, false, "Classrooms")]
    [InlineData(LaptopCategory.Werkstatt, true, "Werkstatt")]
    [InlineData(LaptopCategory.Werkstatt, false, "Workshop")]
    [InlineData(LaptopCategory.Raum, true, "Räume")]
    [InlineData(LaptopCategory.Raum, false, "Rooms")]
    [InlineData(LaptopCategory.Sonstige, true, "Sonstige Geräte")]
    [InlineData(LaptopCategory.Sonstige, false, "Other devices")]
    public void CategoryLabel_Localizes(LaptopCategory cat, bool german, string expected) =>
        Assert.Equal(expected, ClassroomLaptopName.CategoryLabel(cat, german));

    [Fact]
    public void Classify_WerkstatLongForm_AlsoMatches()
    {
        // The parser accepts both WERK and WERKSTATT as category tokens.
        var p = ClassroomLaptopName.Classify("HP-WERKSTATT-042");
        Assert.Equal(LaptopCategory.Werkstatt, p.Category);
        Assert.Equal("042", p.Room);
    }

    [Fact]
    public void Classify_DeviceIndexParsed_AsInt()
    {
        var p = ClassroomLaptopName.Classify("HP-KB-017-42");
        Assert.Equal(42, p.DeviceIndex);
    }

    [Fact]
    public void FriendlyName_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", ClassroomLaptopName.FriendlyName(""));
    }
}
