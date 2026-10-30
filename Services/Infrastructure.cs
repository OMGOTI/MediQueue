using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediQueue.Hubs;
using MediQueue.Models;
using Microsoft.AspNetCore.SignalR;

namespace MediQueue.Services;

public interface IClock { DateTime Now { get; } }

public sealed class SystemClock : IClock { public DateTime Now => DateTime.Now; }

/// <summary>Tells the open pages that something changed in a queue.</summary>
public interface IQueueNotifier { void QueueChanged(int? departmentId); }

public sealed class NullNotifier : IQueueNotifier { public void QueueChanged(int? departmentId) { } }

public sealed class SignalRNotifier : IQueueNotifier
{
    private readonly IHubContext<QueueHub> _hub;
    public SignalRNotifier(IHubContext<QueueHub> hub) { _hub = hub; }
    public void QueueChanged(int? departmentId) => _ = _hub.Clients.All.SendAsync("queueChanged", departmentId);
}

public interface IDataStore
{
    StoreData Data { get; }
    void Save();
}

/// <summary>Keeps all data in memory and saves it to one JSON file. No database to install.</summary>
public sealed class JsonDataStore : IDataStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _path;
    private readonly object _io = new();
    public StoreData Data { get; }

    /// <param name="path">File to save in. Pass null to keep everything only in memory (used by tests).</param>
    public JsonDataStore(string? path)
    {
        _path = path;
        Data = Load() ?? new StoreData();
    }

    private StoreData? Load()
    {
        if (_path == null || !File.Exists(_path)) return null;
        try
        {
            return JsonSerializer.Deserialize<StoreData>(File.ReadAllText(_path), Options);
        }
        catch
        {
            // keep the damaged file for inspection and start fresh
            File.Copy(_path, _path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            return null;
        }
    }

    public void Save()
    {
        if (_path == null) return;
        lock (_io)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Options));
            File.Move(tmp, _path, true);
        }
    }
}

public static class PasswordHelper
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return "v1." + Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || parts[0] != "v1") return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Small helpers used by the pages to show times and labels.</summary>
public static class Ui
{
    public static string Eta(int? minutes) =>
        minutes == null ? "–" : minutes <= 0 ? "Any moment" : minutes < 60 ? $"~{minutes} min" : $"~{minutes / 60}h {minutes % 60:00}m";

    public static string Time(DateTime? t) =>
        t == null ? "–" : t.Value.ToString("hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    public static string DateTimeText(DateTime t) =>
        t.ToString("dd MMM, hh:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    public static string StatusText(TokenStatus s) => s switch
    {
        TokenStatus.Waiting => "Waiting",
        TokenStatus.Called => "Called",
        TokenStatus.InConsultation => "With doctor",
        TokenStatus.Completed => "Completed",
        TokenStatus.Skipped => "Missed call",
        TokenStatus.Cancelled => "Cancelled",
        _ => s.ToString(),
    };

    public static string StatusClass(TokenStatus s) => "st-" + s.ToString().ToLowerInvariant();

    public static string PriorityText(PriorityLevel p) => p switch
    {
        PriorityLevel.Normal => "Normal",
        PriorityLevel.Senior => "Senior citizen",
        PriorityLevel.Pregnant => "Pregnant",
        PriorityLevel.Disabled => "Differently abled",
        PriorityLevel.Emergency => "Emergency",
        _ => p.ToString(),
    };

    /// <summary>"GM-012" becomes "G M 12" so the voice reads it clearly.</summary>
    public static string Speak(string code)
    {
        var parts = code.Split('-');
        var letters = string.Join(" ", parts[0].ToUpperInvariant().ToCharArray());
        return parts.Length > 1 && int.TryParse(parts[1], out var n) ? $"{letters} {n}" : letters;
    }
}
