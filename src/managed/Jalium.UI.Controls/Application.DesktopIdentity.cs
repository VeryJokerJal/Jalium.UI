using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Jalium.UI;

public partial class Application
{
    private string _desktopApplicationId = CreateDefaultDesktopApplicationId(Assembly.GetEntryAssembly()?.GetName().Name);
    private bool _desktopApplicationIdLocked;
    private bool _desktopApplicationIdConfigured;

    /// <summary>
    /// Gets or sets the Linux desktop identity used for Wayland app_id and X11
    /// WM_CLASS. Match the desktop file basename, without the .desktop suffix.
    /// </summary>
    /// <remarks>
    /// Set this before creating native windows. IDs contain 1–255 ASCII letters,
    /// digits, dots, underscores or hyphens. The default comes from the host or
    /// entry assembly name. Window titles remain independent of this machine ID.
    /// </remarks>
    public string DesktopApplicationId
    {
        get => _desktopApplicationId;
        set
        {
            ValidateDesktopApplicationId(value);
            if (_desktopApplicationIdLocked && value != _desktopApplicationId)
                throw new InvalidOperationException("Set DesktopApplicationId before creating native windows.");
            _desktopApplicationId = value;
            _desktopApplicationIdConfigured = true;
        }
    }

    internal void ConfigureDesktopApplicationIdFromHost(string value, bool explicitlyConfigured)
    {
        // A host default must not erase an ID supplied by the Application
        // constructor, XAML or a pre-constructed instance. Explicit builder
        // settings still override it before consumer configuration callbacks.
        if (explicitlyConfigured)
            DesktopApplicationId = value;
        else if (!_desktopApplicationIdConfigured && !_desktopApplicationIdLocked)
            _desktopApplicationId = value;
    }

    internal string LockDesktopApplicationId()
    {
        _desktopApplicationIdLocked = true;
        return _desktopApplicationId;
    }

    internal static void ValidateDesktopApplicationId(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 255 ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            throw new ArgumentException("DesktopApplicationId must contain 1–255 ASCII letters, digits, dots, underscores or hyphens.", nameof(value));
    }

    internal static string CreateDefaultDesktopApplicationId(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "jalium.ui";
        if (name.Length <= 255 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            return name;
        // Host names are display/configuration names and may contain Unicode.
        // A stable hash provides an ICCCM-compatible identity without collisions
        // caused by replacing every non-ASCII character with the same underscore.
        return "jalium.ui." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name))).ToLowerInvariant()[..24];
    }
}
