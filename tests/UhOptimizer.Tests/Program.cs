using UhOptimizer.Core;

if (args.Length == 2 && args[0] == "--live-update-check")
{
    using var http = UpdateClient.CreateHttpClient();
    var result = await new UpdateClient(http).CheckAsync(args[1]);
    Console.WriteLine("Live GitHub update check passed. Latest compatible version: " + result.LatestVersion + "; newer version available: " + (result.Available is not null) + ". No EXE was downloaded.");
    return 0;
}

// Dependency-free behavioral tests. Power writes use a fake, never Windows.
int passed = 0, failed = 0;
void Test(string name, Action body)
{
    try { body(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); failed++; }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Throws(Action body) { bool threw = false; try { body(); } catch { threw = true; } Assert(threw, "Expected an exception"); }
var original = Guid.NewGuid(); var target = Guid.NewGuid();
(FakePower power, MemoryStore store, PowerService service) Setup()
{
    var power = new FakePower(original, target); var store = new MemoryStore(); return (power, store, new(power, store));
}

Test("Backup is saved before the first Windows write", () =>
{
    var (power, store, service) = Setup();
    power.BeforeSet = () => Assert(store.Backup is { Status: "Prepared" } && store.Backup.OriginalId == original);
    var result = service.Apply(target);
    Assert(result.Status == "Applied" && power.Active == target && store.Saves == 2);
});
Test("Backup write failure leaves system unchanged", () =>
{
    var (power, store, service) = Setup(); store.FailSave = true; Throws(() => service.Apply(target)); Assert(power.Active == original && power.Writes == 0);
});
Test("A second change cannot overwrite an outstanding backup", () =>
{
    var (power, store, service) = Setup(); service.Apply(target); var id = store.Backup!.Id;
    Throws(() => service.Apply(original)); Assert(store.Backup.Id == id && power.Active == target && power.Writes == 1);
});
Test("No-op plan selection creates no backup", () =>
{
    var (power, store, service) = Setup(); Throws(() => service.Apply(original)); Assert(store.Backup is null && power.Writes == 0);
});
Test("Unknown target creates no backup", () =>
{
    var (power, store, service) = Setup(); Throws(() => service.Apply(Guid.NewGuid())); Assert(store.Backup is null && power.Writes == 0);
});
Test("Failed Windows write keeps recovery available", () =>
{
    var (power, store, service) = Setup(); power.FailSet = true; Throws(() => service.Apply(target));
    Assert(store.Backup?.Status == "Prepared"); power.FailSet = false; service.Restore(); Assert(store.Backup?.Status == "Restored");
});
Test("Unconfirmed change keeps Prepared journal", () =>
{
    var (power, store, service) = Setup(); power.IgnoreWrites = true; Throws(() => service.Apply(target)); Assert(store.Backup?.Status == "Prepared");
});
Test("Final journal failure keeps a durable Prepared recovery record", () =>
{
    var (power, store, service) = Setup(); store.FailOnSave = 2;
    Throws(() => service.Apply(target)); Assert(power.Active == target && store.Backup?.Status == "Prepared");
    store.FailOnSave = 0; new PowerService(power, store).Restore(); Assert(power.Active == original);
});
Test("Restore survives a restarted service", () =>
{
    var (power, store, service) = Setup(); service.Apply(target); new PowerService(power, store).Restore();
    Assert(power.Active == original && store.Backup?.RestoredUtc is not null && store.Backup.Status == "Restored");
});
Test("Missing original plan preserves the backup", () =>
{
    var (power, store, service) = Setup(); service.Apply(target); power.Plans.RemoveAll(x => x.Id == original);
    Throws(() => service.Restore()); Assert(store.Backup?.Status == "Applied" && power.Active == target);
});
Test("Failed restore can be retried", () =>
{
    var (power, store, service) = Setup(); service.Apply(target); power.FailSet = true; Throws(() => service.Restore());
    Assert(store.Backup?.Status == "Applied"); power.FailSet = false; service.Restore(); Assert(power.Active == original);
});
Test("Verified restore permits another backed-up change", () =>
{
    var (power, store, service) = Setup(); service.Apply(target); var id = store.Backup!.Id; service.Restore(); service.Apply(target); Assert(store.Backup!.Id != id);
});
Test("Restore journal failure remains retryable without another Windows write", () =>
{
    var (power, store, service) = Setup(); service.Apply(target); store.FailOnSave = 3;
    Throws(() => service.Restore()); Assert(power.Active == original && store.Backup?.Status == "Applied");
    int writes = power.Writes; store.FailOnSave = 0; service.Restore(); Assert(power.Writes == writes && store.Backup?.Status == "Restored");
});
Test("Manual return to original plan restores without another system write", () =>
{
    var (power, store, service) = Setup(); service.Apply(target); power.Active = original; service.Restore(); Assert(power.Writes == 1 && store.Backup!.Status == "Restored");
});

var temp = Path.Combine(args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetTempPath(), "UhOptimizerTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    Test("JSON persistence roundtrip and replacement", () =>
    {
        var store = new JsonBackupStore(temp); var power = new FakePower(original, target); var service = new PowerService(power, store);
        service.Apply(target); Assert(store.Load()!.OriginalId == original); new PowerService(power, new JsonBackupStore(temp)).Restore(); Assert(store.Load()!.Status == "Restored");
        Assert(Directory.GetFiles(temp, "*.tmp").Length == 0);
    });
    Test("Corrupt backup blocks changes without deleting evidence", () =>
    {
        var store = new JsonBackupStore(temp); File.WriteAllText(store.BackupPath, "{damaged"); var power = new FakePower(original, target);
        Throws(() => new PowerService(power, store).Apply(target)); Assert(power.Writes == 0 && File.ReadAllText(store.BackupPath) == "{damaged");
    });
    Test("Unknown journal status is rejected", () =>
    {
        var store = new JsonBackupStore(temp); store.Save(new() { OriginalId = original, TargetId = target, Status = "Unknown" }); Throws(() => store.Load());
    });
    Test("FiveM folder validation and cache scope", () =>
    {
        var fivem = Path.Combine(temp, "FiveM"); Directory.CreateDirectory(fivem); Assert(FiveMPaths.Resolve(fivem) is null);
        File.WriteAllText(Path.Combine(fivem, "FiveM.exe"), "fixture"); Assert(FiveMPaths.Resolve(fivem) == fivem);
        var data = Path.Combine(fivem, "FiveM.app", "data"); var cache = Path.Combine(data, "cache"); Directory.CreateDirectory(cache);
        File.WriteAllBytes(Path.Combine(cache, "test.bin"), new byte[15]);
        Directory.CreateDirectory(Path.Combine(data, "game-storage")); File.WriteAllBytes(Path.Combine(data, "game-storage", "keep.bin"), new byte[100]);
        var result = FiveMPaths.InspectCache(data); Assert(result.Bytes == 15 && result.Files == 1 && !result.IsPartial);
        Assert(File.Exists(Path.Combine(cache, "test.bin")), "Inspection modified the cache");
    });
    Test("Cache inspection honors cancellation", () =>
    {
        var cancel = new CancellationToken(true); Throws(() => FiveMPaths.InspectCache(Path.Combine(temp, "FiveM", "FiveM.app", "data"), cancel));
    });
    FeatureTests.Run(Test, temp);
}
finally { Directory.Delete(temp, recursive: true); }
Console.WriteLine($"\n{passed} passed; {failed} failed. No Windows settings were changed.");
return failed == 0 ? 0 : 1;

sealed class MemoryStore : IBackupStore
{
    public PowerBackup? Backup; public int Saves, FailOnSave; public bool FailSave;
    public PowerBackup? Load() => Backup is null ? null : Backup with { };
    public void Save(PowerBackup backup) { Saves++; if (FailSave || Saves == FailOnSave) throw new IOException("Simulated full disk"); Backup = backup with { }; }
}
sealed class FakePower(Guid original, Guid target) : IPowerSystem
{
    public Guid Active = original;
    public List<PowerPlan> Plans = [new(original, "Balanced"), new(target, "High performance")];
    public bool FailSet, IgnoreWrites; public int Writes; public Action? BeforeSet;
    public IReadOnlyList<PowerPlan> GetPlans() => Plans;
    public Guid GetActive() => Active;
    public void SetActive(Guid id) { BeforeSet?.Invoke(); if (FailSet) throw new IOException("Simulated Windows error"); Writes++; if (!IgnoreWrites) Active = id; }
}
