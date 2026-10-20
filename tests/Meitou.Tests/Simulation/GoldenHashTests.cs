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
        ["fight"] = [0xAFD100B5D5A103EFUL, 0x6E1E6CB99A854811UL, 0xB22C13565140E525UL, 0x8CDACD918E27C204UL, 0x96FE5A49E785F339UL],
        ["duels"] = [0xCB48BE7C00CEA9B5UL, 0x7999323A412E2EE9UL, 0xC1219CA1B65C5329UL, 0x09140343A9074FFEUL, 0x4238CCA30F2024F6UL, 0x78A5C001D908D07BUL],
        ["wander-parts"] = [0x3543B26C51534781UL, 0x8B2D54DAA6E46871UL, 0x0A60CEAEB398B8A0UL, 0x147F1D52DE1D4B4AUL],
        ["town-parts"] = [0x0B77F8D9DE908484UL, 0x50CA3CB485013362UL, 0x40710C833902A4CDUL, 0xA10ED1987E63978DUL],
        ["first-aid"] = [0xBF17302ED6B23714UL, 0x9CF95520B6C2E14AUL, 0x3D9E0AA38A43E24FUL, 0xD7AC9D6817486BE5UL, 0x0AF171E0EB5A29E9UL, 0xFE4DF26B5E8C9D66UL],
        ["aggro"] = [0xC195D045D841D14EUL, 0x1C83DE9535DB1DCEUL, 0xAC58933B4C40681DUL, 0xD33F51CEA261E055UL, 0x10FB696D0ADCE5AFUL],
        ["ranged"] = [0xE568D90CF6852D36UL, 0xA93E005EFCCBA40FUL, 0xA226B0AFF22585EAUL, 0xA0480187369AE329UL, 0xE1A90C255C9C1989UL],
        ["carry"] = [0xFB6457CC83EE9BDAUL, 0xB9B54DAB899194CFUL, 0x034E7A2D72066F4BUL, 0xAED089381F2152C1UL, 0xA37BF71A6A86F33FUL, 0x6BB94FB2AB7F2AA4UL, 0x0210C37796B2DBD8UL],
        ["crime"] = [0x21C5A7B717D594DBUL, 0xC1126C11F921A5AEUL, 0xB3C629A77CEC63C9UL, 0x016061FA1C3C053DUL, 0x4C7CEAFFFC1FB6BCUL, 0xCC487CEB72048973UL],
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
