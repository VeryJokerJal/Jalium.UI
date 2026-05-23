using System.Reflection;

namespace Jalium.Extensions.Configuration;

/// <summary>
/// User-secrets config source. Reads <c>~/<see cref="UserSecretsLocation"/>/<paramref name="UserSecretsId"/>/secrets.json</c>
/// (Windows: <c>%APPDATA%\Microsoft\UserSecrets</c>; Unix: <c>$HOME/.microsoft/usersecrets</c>) the same way
/// the official Microsoft.Extensions.Configuration.UserSecrets package does.
/// </summary>
public static class UserSecretsConfigurationExtensions
{
    public const string Secrets_File_Name = "secrets.json";

    public static IConfigurationBuilder AddUserSecrets<T>(this IConfigurationBuilder builder) where T : class
        => builder.AddUserSecrets(typeof(T).Assembly, optional: false, reloadOnChange: false);

    public static IConfigurationBuilder AddUserSecrets<T>(this IConfigurationBuilder builder, bool optional) where T : class
        => builder.AddUserSecrets(typeof(T).Assembly, optional, reloadOnChange: false);

    public static IConfigurationBuilder AddUserSecrets<T>(this IConfigurationBuilder builder, bool optional, bool reloadOnChange) where T : class
        => builder.AddUserSecrets(typeof(T).Assembly, optional, reloadOnChange);

    public static IConfigurationBuilder AddUserSecrets(this IConfigurationBuilder builder, Assembly assembly)
        => builder.AddUserSecrets(assembly, optional: false, reloadOnChange: false);

    public static IConfigurationBuilder AddUserSecrets(this IConfigurationBuilder builder, Assembly assembly, bool optional)
        => builder.AddUserSecrets(assembly, optional, reloadOnChange: false);

    public static IConfigurationBuilder AddUserSecrets(this IConfigurationBuilder builder, Assembly assembly, bool optional, bool reloadOnChange)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assembly);

        var attr = assembly.GetCustomAttribute<UserSecretsIdAttribute>();
        if (attr == null || string.IsNullOrEmpty(attr.UserSecretsId))
        {
            if (optional) return builder;
            throw new InvalidOperationException(
                $"Could not find UserSecretsIdAttribute on assembly '{assembly.GetName().Name}'. " +
                "Add [assembly: UserSecretsId(\"<id>\")] or call AddUserSecrets(string userSecretsId).");
        }
        return builder.AddUserSecrets(attr.UserSecretsId, optional, reloadOnChange);
    }

    public static IConfigurationBuilder AddUserSecrets(this IConfigurationBuilder builder, string userSecretsId)
        => builder.AddUserSecrets(userSecretsId, optional: false, reloadOnChange: false);

    public static IConfigurationBuilder AddUserSecrets(this IConfigurationBuilder builder, string userSecretsId, bool optional)
        => builder.AddUserSecrets(userSecretsId, optional, reloadOnChange: false);

    public static IConfigurationBuilder AddUserSecrets(this IConfigurationBuilder builder, string userSecretsId, bool optional, bool reloadOnChange)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(userSecretsId);
        var path = ResolveSecretsFilePath(userSecretsId);
        if (path == null)
        {
            if (optional) return builder;
            throw new InvalidOperationException("Could not determine the secrets-storage directory; %APPDATA% / $HOME unavailable.");
        }
        return builder.AddJsonFile(path, optional: optional, reloadOnChange: reloadOnChange);
    }

    private static string? ResolveSecretsFilePath(string userSecretsId)
    {
        string root;
        if (OperatingSystem.IsWindows())
        {
            var appData = System.Environment.GetEnvironmentVariable("APPDATA");
            if (string.IsNullOrEmpty(appData)) return null;
            root = Path.Combine(appData, "Microsoft", "UserSecrets");
        }
        else
        {
            var home = System.Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home)) return null;
            root = Path.Combine(home, ".microsoft", "usersecrets");
        }
        return Path.Combine(root, userSecretsId, Secrets_File_Name);
    }
}

/// <summary>Marks an assembly with the user-secrets id (mirrors <c>Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute</c>).</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class UserSecretsIdAttribute : Attribute
{
    public UserSecretsIdAttribute(string userSecretsId)
    {
        ArgumentException.ThrowIfNullOrEmpty(userSecretsId);
        UserSecretsId = userSecretsId;
    }
    public string UserSecretsId { get; }
}
