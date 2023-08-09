﻿using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace HeartRate;

public class HeartRateSettings
{
    private readonly string _filename;

    private static readonly Lazy<string> _generatedFilename = new(GetFilenameCore);

    // See note in Load for how to version the file.
    private const int _settingsVersion = 1;

    public Size UIWindowSize => new(UIWindowSizeX, UIWindowSizeY);

    public int Version;
    public string FontName;
    public string UIFontName;
    public bool UIFontUseSize;
    public FontStyle UIFontStyle;
    public int UIFontSize;
    public int UIWindowSizeX;
    public int UIWindowSizeY;
    public ContentAlignment UITextAlignment;
    public int AlertLevel;
    public int WarnLevel;
    public TimeSpan AlertTimeout;
    public TimeSpan DisconnectedTimeout;
    public Color Color;
    public Color WarnColor;
    public Color UIColor;
    public Color UIWarnColor;
    public Color UIBackgroundColor;
    public string UIBackgroundFile;
    public ImageLayout UIBackgroundLayout;
    public bool Sizable;
    public string LogFormat;
    public string LogDateFormat;
    public string LogFile;
    public string IBIFile;
    public string HeartRateFile;
    public ConnectionInfo? UDP;
    public ulong? BluetoothAddress;

    public HeartRateSettings(string filename)
    {
        _filename = filename;
        Version = _settingsVersion;
        FontName = "Arial";
        UIFontName = "Arial";
        UIFontStyle = FontStyle.Regular;
        UIFontSize = 20;
        UIFontUseSize = false;
        UIWindowSizeX = 350;
        UIWindowSizeY = 250;
        UITextAlignment = ContentAlignment.MiddleCenter;
        WarnLevel = 65;
        AlertLevel = 70;
        AlertTimeout = TimeSpan.FromMinutes(2);
        DisconnectedTimeout = TimeSpan.FromSeconds(10);
        Color = Color.LightBlue;
        WarnColor = Color.Red;
        UIColor = Color.DarkBlue;
        UIWarnColor = Color.Red;
        UIBackgroundColor = Color.Transparent;
        UIBackgroundFile = " ";
        UIBackgroundLayout = ImageLayout.Stretch;
        Sizable = true;
        LogFormat = "csv";
        LogDateFormat = DateTimeFormatter.DefaultColumn;
        LogFile = " "; // Initialize to " " instead of null so the entry is still written.
        IBIFile = " ";
        HeartRateFile = " ";
        UDP = null;
        BluetoothAddress = null;
    }

    public void Save()
    {
        HeartRateSettingsProtocol.Save(this, _filename);
    }

    public void Load()
    {
        var protocol = HeartRateSettingsProtocol.Load(_filename);

        if (protocol == null)
        {
            return;
        }

        FontName = protocol.FontName ?? FontName;
        UIFontName = protocol.UIFontName ?? UIFontName;
        UIFontStyle = EnumOrDefault(protocol.UIFontStyle, UIFontStyle);
        UIFontSize = protocol.UIFontSize ?? UIFontSize;
        UIFontUseSize = protocol.UIFontUseSize ?? UIFontUseSize;
        UIWindowSizeX = protocol.UIWindowSizeX ?? UIWindowSizeX;
        UIWindowSizeY = protocol.UIWindowSizeY ?? UIWindowSizeY;
        UITextAlignment = EnumOrDefault(protocol.UITextAlignment, UITextAlignment);
        AlertLevel = protocol.AlertLevel ?? AlertLevel;
        WarnLevel = protocol.WarnLevel ?? WarnLevel;
        AlertTimeout = protocol.AlertTimeout == null ? AlertTimeout : TimeSpan.FromMilliseconds(protocol.AlertTimeout.Value);
        DisconnectedTimeout = protocol.DisconnectedTimeout == null ? DisconnectedTimeout : TimeSpan.FromMilliseconds(protocol.DisconnectedTimeout.Value);
        Color = ColorOrDefault(protocol.Color, Color);
        WarnColor = ColorOrDefault(protocol.WarnColor, WarnColor);
        UIColor = ColorOrDefault(protocol.UIColor, UIColor);
        UIWarnColor = ColorOrDefault(protocol.UIWarnColor, UIWarnColor);
        UIBackgroundColor = ColorOrDefault(protocol.UIBackgroundColor, UIBackgroundColor);
        UIBackgroundFile = protocol.UIBackgroundFile ?? UIBackgroundFile;
        UIBackgroundLayout = EnumOrDefault(protocol.UIBackgroundLayout, UIBackgroundLayout);
        Sizable = protocol.Sizable ?? Sizable;
        LogFormat = protocol.LogFormat ?? LogFormat;
        LogDateFormat = protocol.LogDateFormat ?? LogDateFormat;
        LogFile = protocol.LogFile ?? LogFile;
        IBIFile = protocol.IBIFile ?? IBIFile;
        HeartRateFile = protocol.HeartRateFile ?? HeartRateFile;
        UDP = ConnectionInfo.Parse(protocol.UDP) ?? UDP;
        BluetoothAddress = ReadUlong(protocol.BluetoothAddress);

        // A hack fix from a bug that's been fixed.
        if (UITextAlignment == 0) UITextAlignment = ContentAlignment.MiddleCenter;
        if (UIFontSize <= 0) UIFontSize = 20;
        if (UIWindowSizeX <= 0) UIWindowSizeX = 350;
        if (UIWindowSizeY <= 0) UIWindowSizeY = 250;

        // In the future:
        // if (protocol.Version >= 2) ...
    }

    private static TEnum EnumOrDefault<TEnum>(string? input, TEnum defaultValue)
        where TEnum : struct
    {
        if (input == null) return defaultValue;
        return Enum.TryParse<TEnum>(
            input, true, out var parsed)
            ? parsed : defaultValue;
    }

    public HeartRateSettings Clone()
    {
        return new HeartRateSettings(_filename)
        {
            Version = Version,
            FontName = FontName,
            UIFontName = UIFontName,
            UIFontStyle = UIFontStyle,
            UIFontUseSize = UIFontUseSize,
            UIFontSize = UIFontSize,
            UIWindowSizeX = UIWindowSizeX,
            UIWindowSizeY = UIWindowSizeY,
            UITextAlignment = UITextAlignment,
            AlertLevel = AlertLevel,
            WarnLevel = WarnLevel,
            AlertTimeout = AlertTimeout,
            DisconnectedTimeout = DisconnectedTimeout,
            Color = Color,
            WarnColor = WarnColor,
            UIColor = UIColor,
            UIWarnColor = UIWarnColor,
            UIBackgroundColor = UIBackgroundColor,
            UIBackgroundFile = UIBackgroundFile,
            UIBackgroundLayout = UIBackgroundLayout,
            Sizable = Sizable,
            LogFormat = LogFormat,
            LogDateFormat = LogDateFormat,
            LogFile = LogFile,
            IBIFile = IBIFile,
            HeartRateFile = HeartRateFile,
            UDP = UDP,
            BluetoothAddress = BluetoothAddress,
        };
    }

    private static Color ColorOrDefault(string? s, Color defaultColor)
    {
        if (s == null) return defaultColor;
        try
        {
            return Color.FromArgb(Convert.ToInt32(s, 16));
        }
        catch
        {
            return defaultColor;
        }
    }

    private static ulong? ReadUlong(string? s)
    {
        if (ulong.TryParse(s, out var result)) return result;
        return null;
    }

    public static string GetFilename() => _generatedFilename.Value;

    private static string GetSettingsDirectory()
    {
        return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "runtime");
    }

    public static string GetSettingsFile(string filename)
    {
        return Path.Combine(GetSettingsDirectory(), filename);
    }

    private static string GetFilenameCore()
    {
        var appDir = GetSettingsDirectory();

        // Arg, do this better.
        try
        {
            Directory.CreateDirectory(appDir);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Can't create settings directory {appDir}, application won't be able to run. Perhaps it doesn't have appropriate permissions?", e);
        }

        return Path.Combine(appDir, "settings.xml");
    }
}

public struct ConnectionInfo
{
    public string Hostname { get; set; }
    public int Port { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Hostname) && Port > 0;

    public static ConnectionInfo? Parse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return default;

        var split = connectionString.Split(new[] { ':' }, 2);
        if (!int.TryParse(split[1], out var port)) return default;
        return new ConnectionInfo
        {
            Hostname = split[0],
            Port = port,
        };
    }

    public override string? ToString()
    {
        if (!IsValid) return null;
        return $"{Hostname}:{Port}";
    }
}

// The object which is serialized to/from XML. XmlSerializer has poor
// type support. HeartRateSettingsProtocol is public to appease
// XmlSerializer.
public class HeartRateSettingsProtocol
{
    // XmlSerializer is used to avoid third party dependencies. It's not
    // pretty.
    private static readonly XmlSerializer _serializer = new(typeof(HeartRateSettingsProtocol));

    // Do not remove the setter as it's needed by the serializer.
    // ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
    public int? Version { get; set; }
    public string? FontName { get; set; }
    public string? UIFontName { get; set; }
    public string? UIFontStyle { get; set; }
    public bool? UIFontUseSize { get; set; }
    public int? UIFontSize { get; set; }
    public int? UIWindowSizeX { get; set; }
    public int? UIWindowSizeY { get; set; }
    public string? UITextAlignment { get; set; }
    public int? AlertLevel { get; set; }
    public int? WarnLevel { get; set; }
    public int? AlertTimeout { get; set; }
    public int? DisconnectedTimeout { get; set; }
    public string? Color { get; set; }
    public string? WarnColor { get; set; }
    public string? UIColor { get; set; }
    public string? UIWarnColor { get; set; }
    public string? UIBackgroundColor { get; set; }
    public string? UIBackgroundFile { get; set; }
    public string? UIBackgroundLayout { get; set; }
    public bool? Sizable { get; set; }
    public string? LogFormat { get; set; }
    public string? LogDateFormat { get; set; }
    public string? LogFile { get; set; }
    public string? IBIFile { get; set; }
    public string? HeartRateFile { get; set; }
    public string? UDP { get; set; }
    public string? BluetoothAddress { get; set; }
    // ReSharper restore AutoPropertyCanBeMadeGetOnly.Global

    // Required by deserializer.
    // ReSharper disable once UnusedMember.Global
    public HeartRateSettingsProtocol() { }

    private HeartRateSettingsProtocol(HeartRateSettings settings)
    {
        Version = settings.Version;
        FontName = settings.FontName;
        AlertLevel = settings.AlertLevel;
        UIFontName = settings.UIFontName;
        UIFontStyle = settings.UIFontStyle.ToString();
        UIFontUseSize = settings.UIFontUseSize;
        UIFontSize = settings.UIFontSize;
        UIWindowSizeX = settings.UIWindowSizeX;
        UIWindowSizeY = settings.UIWindowSizeY;
        UITextAlignment = settings.UITextAlignment.ToString();
        WarnLevel = settings.WarnLevel;
        AlertTimeout = (int)settings.AlertTimeout.TotalMilliseconds;
        DisconnectedTimeout = (int)settings.DisconnectedTimeout.TotalMilliseconds;
        Color = ColorToString(settings.Color);
        WarnColor = ColorToString(settings.WarnColor);
        UIColor = ColorToString(settings.UIColor);
        UIWarnColor = ColorToString(settings.UIWarnColor);
        UIBackgroundColor = ColorToString(settings.UIBackgroundColor);
        UIBackgroundFile = settings.UIBackgroundFile;
        UIBackgroundLayout = settings.UIBackgroundLayout.ToString();
        Sizable = settings.Sizable;
        LogFormat = settings.LogFormat;
        LogDateFormat = settings.LogDateFormat;
        LogFile = settings.LogFile;
        IBIFile = settings.IBIFile;
        HeartRateFile = settings.HeartRateFile;
        UDP = settings.UDP == null ? " " : settings.UDP.ToString();
        BluetoothAddress = settings.BluetoothAddress == null ? " " : settings.BluetoothAddress.ToString();
    }

    private static string ColorToString(Color color)
    {
        return color.ToArgb().ToString("X8");
    }

    internal static HeartRateSettingsProtocol? Load(string filename)
    {
        DebugLog.WriteLog($"Loading from {filename}");

        if (filename == null)
        {
            throw new FileNotFoundException($"Unable to read null file.");
        }

        if (!File.Exists(filename) || new FileInfo(filename).Length < 5)
        {
            return null;
        }

        // Exception timebomb #1
        using var fs = File.OpenRead(filename);
        // Exception timebomb #2
        return _serializer.Deserialize(fs) as HeartRateSettingsProtocol;
    }

    internal static void Save(HeartRateSettings settings, string filename)
    {
        DebugLog.WriteLog($"Saving to {filename}");

        var protocol = new HeartRateSettingsProtocol(settings);

        using var fs = File.Open(filename, FileMode.Create, FileAccess.Write);
        _serializer.Serialize(fs, protocol);
    }
}