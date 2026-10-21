using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Game;
using Meitou.Simulation;
using Meitou.Tests.Simulation;
using static Meitou.Tests.Save.SaveSamples;

namespace Meitou.Tests.Gameplay;

public class NewGameSettingsTests
{
    [Fact, Slow]
    public void OriginalDescriptionsAndLabelsHaveTranslationsInEveryInstalledLanguage()
    {
        var install = Meitou.Content.GameInstall.Locate();
        Assert.SkipWhen(install is null, "No Kenshi install configured");
        string root = Path.Combine(install.Root, "locale");
        var help = Meitou.Data.Ui.NewGameHelpCatalog.Read(Path.Combine(root, "en_GB/LC_MESSAGES/main.pot"));
        foreach (var option in NewGameSettings.Options)
        {
            string english = help.EnglishAt(option.HelpLine);
            Assert.True(english.Length > option.Label.Length);
            foreach (string folder in Directory.EnumerateDirectories(root))
            {
                string messages = Path.Combine(folder, "LC_MESSAGES");
                if (!File.Exists(Path.Combine(messages, "main.mo")) && !File.Exists(Path.Combine(messages, "main.po"))) continue;
                var catalog = Meitou.Localisation.GettextCatalog.Load(messages);
                Assert.False(string.IsNullOrWhiteSpace(catalog.Find(english)), $"{Path.GetFileName(folder)}: {option.Key} description");
                Assert.False(string.IsNullOrWhiteSpace(catalog.Find(option.Label)), $"{Path.GetFileName(folder)}: {option.Key} label");
            }
        }
    }

    [Fact]
    public void SaveRoundTripKeepsAllOptionsAndOldSavesUseNeutralValues()
    {
        var options = new NewGameSettings();
        float[] values = [3.25f, 2, 1.5f, .75f, 1.25f, 2, 3.5f];
        for (int i = 0; i < values.Length; i++) options = options.Set(i, values[i]);
        var game = new SaveBuilder(17).Game;
        options.Write(game.Camera);
        string folder = Temp("difficulty");
        try
        {
            game.Write(folder);
            var loaded = NewGameSettings.Read(SaveGame.Load(folder).Camera);
            for (int i = 0; i < values.Length; i++) Assert.Equal(values[i], loaded[i]);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        var old = NewGameSettings.Read(new SaveCamera(new FcsRecord()));
        for (int i = 0; i < values.Length; i++) Assert.Equal(1, old[i]);
    }

    [Fact]
    public void InvalidSavedOptionsAreBoundedAndNewBodyFactoriesDoNotMutateTheRunningWorld()
    {
        var camera = new SaveCamera(new FcsRecord());
        camera.Record.Floats["meitou difficulty hunger"] = float.NaN;
        camera.Record.Floats["meitou difficulty death"] = -10;
        camera.Record.Floats["meitou difficulty research"] = 99;
        var options = NewGameSettings.Read(camera);
        Assert.Equal(1, options[0]); Assert.Equal(.5f, options[1]); Assert.Equal(2, options[4]);
        var data = PopulationData.Create(SyntheticTown.Database(), []);
        var originalFactory = data.Bodies;
        var changed = options.Set(0, 4).Set(2, 3).Apply(data);
        Assert.Equal(1, data.BodyOptions.HungerTime);
        Assert.Equal(4, changed.Bodies.Options.HungerTime);
        Assert.Equal(3, changed.BodyOptions.GlobalDamageMultiplier);
        Assert.NotSame(originalFactory, changed.Bodies);
        Assert.Same(originalFactory, data.Bodies);
    }
}
