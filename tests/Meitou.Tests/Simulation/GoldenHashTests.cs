using Meitou.Tests.Combat;

namespace Meitou.Tests.Simulation;

/// <summary>
/// Pins the state hashes of the scripted scenarios the other tests use, so a refactor that is meant to change nothing is checked to
/// change nothing (the thread-count tests only check that the threads agree with each other). A change of behaviour that is meant must
/// update these numbers in the same commit and say so.
/// </summary>
public class GoldenHashTests(ITestOutputHelper output)
{
    static readonly (string Name, Func<List<ulong>> Run)[] Scenarios =
    [
        ("wander", () => DeterminismTests.Hashes(7, 4)),
        ("player", () => PlayerTests.Scripted(4)),
        ("roaming", () => RoamingTests.Scripted(4)),
        ("bodies", () => BodyWiringTests.Hashes(9, 4, 2000)),
        ("fight", () => FightWiringTests.Hashes(4)),
        ("duels", () => DuelTests.Hashes(5, 4, out _)),
        ("wander-parts", () => Parts(WanderSystem.Create(11, 4, 200, minPartition: 4), 1, 30, 90, 300)),
        ("town-parts", () => Parts(TownParts(), 1, 40, 200, 700)),
        ("first-aid", () => FirstAidTests.Scripted(4)),
        ("aggro", () => AggroTests.Hashes(4)),
        ("ranged", () => RangedTests.Hashes(4)),
        ("carry", () => CarryTests.Scripted(4)),
        ("crime", () => CrimeTests.Scripted(4)),
        ("build", () => ConstructionTests.Scripted(4)),
        ("dialogue", () => DialogueTests.Scripted(4)),
    ];

    /// <summary>A roaming town with bodies and feeding, cut into many partitions (the other worlds here are mostly one partition at the default size).</summary>
    static Meitou.Simulation.World TownParts()
    {
        var db = SyntheticTown.Database(roaming: true, anatomy: true, items: true);
        return SyntheticTown.World(21, 4, db: db, bodies: true, bodyTimeScale: 2000, feed: new Meitou.Simulation.Items.FeedSettings(), minPartition: 4);
    }

    static List<ulong> Parts(Meitou.Simulation.World w, params int[] checkpoints)
    {
        using var world = w;
        var hashes = new List<ulong>();
        int ran = 0;
        foreach (int target in checkpoints)
        {
            world.RunTicks(target - ran);
            ran = target;
            hashes.Add(world.StateHash());
        }
        return hashes;
    }

    static readonly Dictionary<string, ulong[]> Golden = new()
    {
        ["wander"] = [0x9C9775CE6597DD6BUL, 0xFC7CDBEE899281FCUL, 0xBB6C16EF02A628E9UL, 0xBDFB49EB74CDADFBUL, 0xF598C41B6B09487BUL, 0x67FB41FE12600A56UL],
        ["player"] = [0x939102537B980742UL, 0x148F05C662B04AC6UL, 0x3503F3B2A8CD7D04UL, 0xEBC077BDC541F15BUL, 0x5A38398000D9AB8EUL, 0x60428AE5A82BF88BUL],
        ["roaming"] = [0x9D4CAEBD1FC1DEEBUL, 0xDDE5D9A3AF737B5AUL, 0xEE5C3FBBF2EA8FA0UL, 0xBB35FAB7101EA3EFUL, 0xE445D804941F4230UL, 0x85C363D7EF759282UL],
        ["bodies"] = [0x8996DF8EE939ADA2UL, 0xB3C7BB1C7571A6E2UL, 0xD86830764C5F63FAUL, 0x2E8A63233A8270ECUL, 0x6EDB730AE4DCCB8EUL, 0xAC0BD4F593B92FE1UL, 0xBF1B06D311CEF435UL],
        ["fight"] = [0xAFD100B5D5A103EFUL, 0x4EA620A36534D64FUL, 0xF5C15A07B4AB6B36UL, 0xB32F1235AB25E062UL, 0x0F5D21B044D881E3UL],
        ["duels"] = [0xB95ECA48BE4D6B67UL, 0xBDF3C811FA72B3E9UL, 0x3678377EAC3F88CAUL, 0x2E9CF179BA55D4FEUL, 0x3DA3B7D34F5586B6UL, 0xD4C3C8E4BC1B8B38UL],
        ["wander-parts"] = [0x3543B26C51534781UL, 0x8B2D54DAA6E46871UL, 0x0A60CEAEB398B8A0UL, 0x147F1D52DE1D4B4AUL],
        ["town-parts"] = [0x0B77F8D9DE908484UL, 0x50CA3CB485013362UL, 0x40710C833902A4CDUL, 0xA10ED1987E63978DUL],
        ["first-aid"] = [0xBF17302ED6B23714UL, 0x9CF95520B6C2E14AUL, 0x3D9E0AA38A43E24FUL, 0xD7AC9D6817486BE5UL, 0x0AF171E0EB5A29E9UL, 0xFE4DF26B5E8C9D66UL],
        ["aggro"] = [0xC195D045D841D14EUL, 0x1C83DE9535DB1DCEUL, 0xF65452202387C102UL, 0x63EFB71657520CC1UL, 0x9B7F6D3FA0ECBB52UL],
        ["ranged"] = [0xCDB87B3C94CC14ACUL, 0x1D779480FC376D8EUL, 0x0DF5A302BB29A205UL, 0xDCAFFD8B368042F1UL, 0x574223B4CFCFA1ACUL],
        ["carry"] = [0xFB6457CC83EE9BDAUL, 0xB9B54DAB899194CFUL, 0x034E7A2D72066F4BUL, 0xAED089381F2152C1UL, 0xA37BF71A6A86F33FUL, 0x6BB94FB2AB7F2AA4UL, 0x0210C37796B2DBD8UL],
        ["crime"] = [0x21C5A7B717D594DBUL, 0xC1126C11F921A5AEUL, 0x87F9DC29E54A73D8UL, 0x71E450EF7D33EC3CUL, 0x934CB003F5B82491UL, 0x7BC6E071DE99673AUL],
        ["build"] = [0x65D4C75216A7692CUL, 0xA8C813F2CD3ECC26UL, 0x4C77BC946AC3E9D8UL, 0x5E86BB43B164AAC7UL, 0xB1D7B4CE0B74E759UL, 0xAA9839F9FD80A9C1UL, 0x0002677BB86FF1E8UL, 0x2B97AC9EF598FEBBUL],
        ["dialogue"] = [0xDD56E9565C8AD0FEUL, 0x3E0A77AA4F593314UL, 0x3FD582EAC093CC20UL, 0x6963BBAEB6C0E9FCUL, 0x784B35604C393664UL, 0xA3DFB392251E4108UL],
    };

    [Fact]
    public void Scripted_scenarios_hash_as_pinned()
    {
        // The pins were made on Windows; the wandering scenarios use MathF.Sin/Cos/Atan2, which come from the platform's C library and can differ in the last bit elsewhere.
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "hashes are pinned on Windows");
        var failures = new List<string>();
        foreach (var (name, run) in Scenarios)
        {
            var got = run();
            output.WriteLine($"[\"{name}\"] = [{string.Join(", ", got.Select(h => $"0x{h:X16}UL"))}],");
            if (!Golden.TryGetValue(name, out var want)) failures.Add($"{name}: not pinned");
            else if (!want.SequenceEqual(got)) failures.Add($"{name}: hashes changed");
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }
}
