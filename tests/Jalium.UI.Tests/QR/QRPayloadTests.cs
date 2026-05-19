using Jalium.UI.Controls.QR.Payloads;

namespace Jalium.UI.Tests.QR;

public class QRPayloadTests
{
    [Fact]
    public void WiFi_Wpa_FormatsExpected()
    {
        var s = WiFiPayload.For("MyAP", "p@ss", WiFiAuth.WPA, hidden: false);
        Assert.Equal("WIFI:T:WPA;S:MyAP;P:p@ss;H:false;;", s);
    }

    [Fact]
    public void WiFi_None_OmitsPassword()
    {
        var s = WiFiPayload.For("Open", null, WiFiAuth.None);
        Assert.Equal("WIFI:T:nopass;S:Open;H:false;;", s);
    }

    [Fact]
    public void WiFi_EscapesSpecials()
    {
        var s = WiFiPayload.For("net;work", @"a\b\c");
        Assert.Contains(@"S:net\;work", s);
        Assert.Contains(@"P:a\\b\\c", s);
    }

    [Fact]
    public void Mail_BuildsMailtoUri()
    {
        var s = MailPayload.For("a@b.com", "hi there", "body & stuff");
        Assert.StartsWith("mailto:a@b.com?", s);
        Assert.Contains("subject=hi%20there", s);
        Assert.Contains("body=body%20%26%20stuff", s);
    }

    [Fact]
    public void Sms_SmsToFormat()
    {
        Assert.Equal("SMSTO:+15555550100:hi", SmsPayload.For("+15555550100", "hi"));
    }

    [Fact]
    public void Sms_SmsUriFormat()
    {
        var s = SmsPayload.For("+15555550100", "x y", SmsPayload.Format.SmsUri);
        Assert.Equal("sms:+15555550100?body=x%20y", s);
    }

    [Fact]
    public void Tel_PrefixesTelScheme()
    {
        Assert.Equal("tel:+15555550100", TelPayload.For("+15555550100"));
    }

    [Fact]
    public void Geo_FormatsLatLon()
    {
        var s = GeoPayload.For(37.7749, -122.4194, "SF");
        Assert.StartsWith("geo:37.7749,-122.4194", s);
        Assert.Contains("q=SF", s);
    }

    [Fact]
    public void VCard_ContainsBeginEnd_AndName()
    {
        var s = VCardPayload.For(new ContactInfo
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            Phone = "+15555550100"
        });
        Assert.StartsWith("BEGIN:VCARD", s);
        Assert.EndsWith("END:VCARD", s);
        Assert.Contains("N:Lovelace;Ada", s);
        Assert.Contains("EMAIL:ada@example.com", s);
    }

    [Fact]
    public void MeCard_CompactFormat()
    {
        var s = MeCardPayload.For(new ContactInfo { FirstName = "Ada", LastName = "Lovelace", Phone = "+1" });
        Assert.StartsWith("MECARD:", s);
        Assert.Contains("N:Lovelace,Ada;", s);
        Assert.Contains("TEL:+1;", s);
    }

    [Fact]
    public void Bookmark_Format()
    {
        Assert.Equal("MEBKM:TITLE:T;URL:https://x;;", BookmarkPayload.For("T", "https://x"));
    }

    [Fact]
    public void Calendar_AllDay_UsesDateValue()
    {
        var s = CalendarEventPayload.For("Meeting", new DateTime(2026, 6, 1), new DateTime(2026, 6, 2), allDay: true);
        Assert.Contains("DTSTART;VALUE=DATE:20260601", s);
        Assert.Contains("DTEND;VALUE=DATE:20260602", s);
    }

    [Fact]
    public void Bitcoin_FormatsBip21()
    {
        var s = BitcoinPayload.For("1A1zP1eP", 0.5m, "Alice", "Dinner");
        Assert.StartsWith("bitcoin:1A1zP1eP?", s);
        Assert.Contains("amount=0.5", s);
        Assert.Contains("label=Alice", s);
        Assert.Contains("message=Dinner", s);
    }

    [Fact]
    public void Url_Trims()
    {
        Assert.Equal("https://x", UrlPayload.For("  https://x  "));
    }
}
