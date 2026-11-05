using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MakarovPhysics.Base;

/// <summary>
/// ALL user settings live in the Windows registry (PLAN §7). This class is the
/// single proxy for that storage: every read and write goes through it into one
/// branch, <c>HKEY_CURRENT_USER\Software\MakarovPhysics</c> by default.
///
/// The registry is user-writable but can still fail (locked-down accounts,
/// mandatory profiles, a corrupted hive) - settings must never be a reason to
/// crash, so every failure degrades silently: reads return <c>null</c>
/// ("no value"), writes become no-ops. Doubles are stored as invariant-culture
/// strings (the registry has no floating-point kind), booleans as DWORD 0/1.
/// </summary>
/// <remarks>
/// The one Windows corner of an otherwise portable library, and now it says
/// so. MakarovPhysics.Base targets a NEUTRAL framework, so the platform
/// analyser needs to be told - and once told it checks every CALL SITE, which
/// is a better guarantee than the target framework ever was: the framework
/// said the whole library was Windows-only, which was false about ninety-nine
/// percent of it and told nobody anything about this class (09.08).
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class UserRegistry
{
    /// <summary>The application's branch under HKEY_CURRENT_USER.</summary>
    public const string DefaultBranch = @"Software\MakarovPhysics";

    private readonly string _branch;

    /// <summary>Proxy for the default application branch.</summary>
    public UserRegistry() : this(DefaultBranch) { }

    /// <summary>Proxy for a specific branch under HKCU (tests use a scratch one).</summary>
    public UserRegistry(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            throw new ArgumentException("Registry branch must be a non-empty subkey path.", nameof(branch));
        }

        _branch = branch;
    }

    /// <summary>The HKCU subkey path this proxy reads and writes.</summary>
    public string Branch => _branch;

    public string? ReadString(string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_branch);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    public void WriteString(string name, string value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(_branch);
            key.SetValue(name, value, RegistryValueKind.String);
        }
        catch
        {
            // no-op: a failed settings write must not take the app down
        }
    }

    public int? ReadInt(string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_branch);
            return key?.GetValue(name) is int i ? i : null;
        }
        catch
        {
            return null;
        }
    }

    public void WriteInt(string name, int value)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(_branch);
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch
        {
            // no-op
        }
    }

    public bool? ReadBool(string name) => ReadInt(name) switch
    {
        null => null,
        0 => false,
        _ => true,
    };

    public void WriteBool(string name, bool value) => WriteInt(name, value ? 1 : 0);

    public double? ReadDouble(string name)
    {
        string? s = ReadString(name);
        if (s is null) return null;

        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double d) && double.IsFinite(d)
            ? d
            : null;
    }

    public void WriteDouble(string name, double value)
    {
        if (!double.IsFinite(value)) return;
        WriteString(name, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Remove one value (missing value is not an error).</summary>
    public void Delete(string name)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_branch, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch
        {
            // no-op
        }
    }

    /// <summary>Remove the whole branch (SelfTest cleans its scratch branch with this).</summary>
    public void DeleteBranch()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_branch, throwOnMissingSubKey: false);
        }
        catch
        {
            // no-op
        }
    }
}
