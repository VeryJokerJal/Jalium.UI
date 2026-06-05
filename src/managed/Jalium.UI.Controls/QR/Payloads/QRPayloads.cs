using System.Globalization;
using System.Text;

namespace Jalium.UI.Controls.QR.Payloads;

/// <summary>WiFi Auth modes supported by WIFI: payload.</summary>
public enum WiFiAuth { None, WEP, WPA }

/// <summary>WIFI:T:...;S:...;P:...;H:...;; payload (de-facto cross-vendor format).</summary>
public static class WiFiPayload
{
    public static string For(string ssid, string? password, WiFiAuth auth = WiFiAuth.WPA, bool hidden = false)
    {
        if (string.IsNullOrEmpty(ssid)) throw new ArgumentException("SSID required.", nameof(ssid));
        var t = auth switch { WiFiAuth.None => "nopass", WiFiAuth.WEP => "WEP", _ => "WPA" };
        var sb = new StringBuilder("WIFI:T:").Append(t)
            .Append(";S:").Append(Escape(ssid));
        if (auth != WiFiAuth.None && !string.IsNullOrEmpty(password))
        {
            sb.Append(";P:").Append(Escape(password!));
        }
        sb.Append(";H:").Append(hidden ? "true" : "false").Append(";;");
        return sb.ToString();
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '\\' or ';' or ',' or ':' or '"') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Plain URL passthrough with trimming.</summary>
public static class UrlPayload
{
    public static string For(Uri uri)
    {
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        return uri.AbsoluteUri;
    }

    public static string For(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("URL required.", nameof(url));
        return url.Trim();
    }
}

/// <summary>Email payload: mailto:to?subject=...&amp;body=...</summary>
public static class MailPayload
{
    public static string For(string to, string? subject = null, string? body = null)
    {
        if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("Recipient required.", nameof(to));
        var sb = new StringBuilder("mailto:").Append(to);
        var sep = '?';
        if (!string.IsNullOrEmpty(subject))
        {
            sb.Append(sep).Append("subject=").Append(Uri.EscapeDataString(subject!));
            sep = '&';
        }
        if (!string.IsNullOrEmpty(body))
        {
            sb.Append(sep).Append("body=").Append(Uri.EscapeDataString(body!));
        }
        return sb.ToString();
    }
}

/// <summary>SMS payload: SMSTO:number:body (most-compatible) or sms:number?body= fallback.</summary>
public static class SmsPayload
{
    public enum Format { SmsTo, SmsUri }

    public static string For(string number, string? body = null, Format format = Format.SmsTo)
    {
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("Number required.", nameof(number));
        return format switch
        {
            Format.SmsTo => $"SMSTO:{number}:{body ?? string.Empty}",
            _ => string.IsNullOrEmpty(body) ? $"sms:{number}" : $"sms:{number}?body={Uri.EscapeDataString(body!)}"
        };
    }
}

/// <summary>tel:+number</summary>
public static class TelPayload
{
    public static string For(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("Number required.", nameof(number));
        return "tel:" + number.Trim();
    }
}

/// <summary>geo: location URI per RFC 5870 with optional ?q= label.</summary>
public static class GeoPayload
{
    public static string For(double latitude, double longitude, string? query = null)
    {
        var sb = new StringBuilder("geo:");
        sb.Append(latitude.ToString("R", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(longitude.ToString("R", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(query))
        {
            sb.Append("?q=").Append(Uri.EscapeDataString(query!));
        }
        return sb.ToString();
    }
}

/// <summary>Contact fields shared between vCard and MeCard payloads.</summary>
public sealed class ContactInfo
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Organization { get; init; }
    public string? Title { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Url { get; init; }
    public string? Note { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

/// <summary>vCard 3.0 payload.</summary>
public static class VCardPayload
{
    public static string For(ContactInfo info)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCARD");
        sb.AppendLine("VERSION:3.0");
        var last = info.LastName ?? string.Empty;
        var first = info.FirstName ?? string.Empty;
        sb.Append("N:").Append(last).Append(';').AppendLine(first);
        sb.Append("FN:").AppendLine($"{first} {last}".Trim());
        if (!string.IsNullOrEmpty(info.Organization)) sb.Append("ORG:").AppendLine(info.Organization);
        if (!string.IsNullOrEmpty(info.Title)) sb.Append("TITLE:").AppendLine(info.Title);
        if (!string.IsNullOrEmpty(info.Phone)) sb.Append("TEL:").AppendLine(info.Phone);
        if (!string.IsNullOrEmpty(info.Email)) sb.Append("EMAIL:").AppendLine(info.Email);
        if (!string.IsNullOrEmpty(info.Url)) sb.Append("URL:").AppendLine(info.Url);
        if (!string.IsNullOrEmpty(info.Note)) sb.Append("NOTE:").AppendLine(info.Note);
        if (HasAddress(info))
        {
            sb.Append("ADR:;;")
              .Append(info.Street).Append(';')
              .Append(info.City).Append(';')
              .Append(info.Region).Append(';')
              .Append(info.PostalCode).Append(';')
              .AppendLine(info.Country);
        }
        sb.Append("END:VCARD");
        return sb.ToString();
    }

    private static bool HasAddress(ContactInfo info)
        => !string.IsNullOrEmpty(info.Street) || !string.IsNullOrEmpty(info.City) ||
           !string.IsNullOrEmpty(info.Region) || !string.IsNullOrEmpty(info.PostalCode) ||
           !string.IsNullOrEmpty(info.Country);
}

/// <summary>MeCard payload (compact alternative to vCard, common on Asian phones).</summary>
public static class MeCardPayload
{
    public static string For(ContactInfo info)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        var sb = new StringBuilder("MECARD:");
        var name = $"{info.LastName ?? string.Empty},{info.FirstName ?? string.Empty}".TrimEnd(',');
        if (!string.IsNullOrEmpty(name)) sb.Append("N:").Append(name).Append(';');
        if (!string.IsNullOrEmpty(info.Phone)) sb.Append("TEL:").Append(info.Phone).Append(';');
        if (!string.IsNullOrEmpty(info.Email)) sb.Append("EMAIL:").Append(info.Email).Append(';');
        if (!string.IsNullOrEmpty(info.Url)) sb.Append("URL:").Append(info.Url).Append(';');
        if (!string.IsNullOrEmpty(info.Organization)) sb.Append("ORG:").Append(info.Organization).Append(';');
        if (!string.IsNullOrEmpty(info.Note)) sb.Append("NOTE:").Append(info.Note).Append(';');
        sb.Append(';');
        return sb.ToString();
    }
}

/// <summary>Bookmark payload (MEBKM).</summary>
public static class BookmarkPayload
{
    public static string For(string title, string url)
    {
        if (string.IsNullOrEmpty(title)) throw new ArgumentException("Title required.", nameof(title));
        if (string.IsNullOrEmpty(url)) throw new ArgumentException("URL required.", nameof(url));
        return $"MEBKM:TITLE:{title};URL:{url};;";
    }
}

/// <summary>iCalendar VEVENT payload.</summary>
public static class CalendarEventPayload
{
    public static string For(string summary, DateTime start, DateTime end, string? location = null, string? description = null, bool allDay = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("BEGIN:VEVENT");
        sb.Append("SUMMARY:").AppendLine(summary);
        if (allDay)
        {
            sb.Append("DTSTART;VALUE=DATE:").AppendLine(start.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            sb.Append("DTEND;VALUE=DATE:").AppendLine(end.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        }
        else
        {
            sb.Append("DTSTART:").AppendLine(start.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture));
            sb.Append("DTEND:").AppendLine(end.ToUniversalTime().ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrEmpty(location)) sb.Append("LOCATION:").AppendLine(location);
        if (!string.IsNullOrEmpty(description)) sb.Append("DESCRIPTION:").AppendLine(description);
        sb.AppendLine("END:VEVENT");
        sb.Append("END:VCALENDAR");
        return sb.ToString();
    }
}

/// <summary>Bitcoin URI (BIP21).</summary>
public static class BitcoinPayload
{
    public static string For(string address, decimal? amount = null, string? label = null, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("Address required.", nameof(address));
        var sb = new StringBuilder("bitcoin:").Append(address);
        var sep = '?';
        void Add(string key, string? val)
        {
            if (string.IsNullOrEmpty(val)) return;
            sb.Append(sep).Append(key).Append('=').Append(Uri.EscapeDataString(val!));
            sep = '&';
        }
        if (amount.HasValue) Add("amount", amount.Value.ToString(CultureInfo.InvariantCulture));
        Add("label", label);
        Add("message", message);
        return sb.ToString();
    }
}
