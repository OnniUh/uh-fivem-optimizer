using System.Text.Json;

namespace UhOptimizer.Core;

public record PowerPlan(Guid Id, string Name)
{
    public override string ToString() => Name;
}

public interface IPowerSystem
{
    IReadOnlyList<PowerPlan> GetPlans();
    Guid GetActive();
    void SetActive(Guid id);
}

public record PowerBackup
{
    public int SchemaVersion { get; init; } = 1;
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public Guid OriginalId { get; init; }
    public string OriginalName { get; init; } = "";
    public Guid TargetId { get; init; }
    public string TargetName { get; init; } = "";
    public string Status { get; set; } = "Prepared";
    public DateTimeOffset? RestoredUtc { get; set; }
}

public interface IBackupStore
{
    PowerBackup? Load();
    void Save(PowerBackup backup);
}

/// <summary>One outstanding change. Backups are durable before any system write.</summary>
public sealed class PowerService(IPowerSystem power, IBackupStore store)
{
    public PowerBackup? GetBackup() => store.Load();

    public PowerBackup Apply(Guid target)
    {
        var previous = store.Load();
        if (previous is not null && previous.Status != "Restored")
            throw new InvalidOperationException("Restore the existing backup before making another change.");
        var plans = power.GetPlans();
        var selected = plans.FirstOrDefault(x => x.Id == target)
            ?? throw new InvalidOperationException("That power plan is no longer available. Scan again.");
        var original = power.GetActive();
        if (original == target) throw new InvalidOperationException("That power plan is already active. Nothing changed.");
        var backup = new PowerBackup
        {
            OriginalId = original,
            OriginalName = plans.FirstOrDefault(x => x.Id == original)?.Name ?? original.ToString(),
            TargetId = target,
            TargetName = selected.Name
        };
        store.Save(backup); // If backup persistence fails, do not touch Windows.
        power.SetActive(target);
        if (power.GetActive() != target)
            throw new InvalidOperationException("Windows did not confirm the change. The original plan is saved in Restore.");
        backup.Status = "Applied";
        store.Save(backup);
        return backup;
    }

    public PowerBackup Restore()
    {
        var backup = store.Load() ?? throw new InvalidOperationException("There is no saved power plan to restore.");
        if (backup.Status == "Restored") throw new InvalidOperationException("This backup has already been restored.");
        if (!power.GetPlans().Any(x => x.Id == backup.OriginalId))
            throw new InvalidOperationException("The original power plan was removed from Windows. The backup has been kept.");
        if (power.GetActive() != backup.OriginalId) power.SetActive(backup.OriginalId);
        if (power.GetActive() != backup.OriginalId)
            throw new InvalidOperationException("Windows did not confirm the restore. The backup has been kept; you can retry.");
        backup.Status = "Restored";
        backup.RestoredUtc = DateTimeOffset.UtcNow;
        store.Save(backup);
        return backup;
    }
}

public sealed class JsonBackupStore(string folder) : IBackupStore
{
    public string BackupPath => Path.Combine(folder, "power-backup.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public PowerBackup? Load()
    {
        if (!File.Exists(BackupPath)) return null;
        try
        {
            var backup = JsonSerializer.Deserialize<PowerBackup>(File.ReadAllText(BackupPath), Options)
                ?? throw new InvalidDataException("Empty backup.");
            if (backup.SchemaVersion != 1 || backup.Id == Guid.Empty || backup.OriginalId == Guid.Empty ||
                backup.TargetId == Guid.Empty || backup.Status is not ("Prepared" or "Applied" or "Restored"))
                throw new InvalidDataException("Unknown backup format.");
            return backup;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException("The backup could not be read. It has been kept for recovery; no new changes are allowed.", ex);
        }
    }

    public void Save(PowerBackup backup)
    {
        Directory.CreateDirectory(folder);
        // Atomic replacement: an interrupted write never truncates the old backup.
        var temp = Path.Combine(folder, $"backup-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, backup, Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, BackupPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
