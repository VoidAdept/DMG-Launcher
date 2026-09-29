namespace DMGLauncher;

// `DMG-Launcher.exe --selftest`: exit code 0 = pass.
static class SelfTest
{
    // Live hub shape (2026-09-29) plus a fabricated busy world and a hostile url.
    const string Hub = """
        general
        	title = "Duel Monsters Genesis"
        	long_desc = "a \"quoted\" desc"
        world/1
        	url = "byond://BYOND.world.954870843"
        	server_version = 516
        	status = "(12.0.3)"
        	players = list()
        world/2
        	url = "byond://1.2.3.4:5000"
        	status = "<b>Test \"Server\"</b>"
        	players = list("Some Duelist","Some-One")
        world/3
        	url = "byond://evil & calc.exe"
        	status = "x"
        	players = list()
        """;

    public static int Run()
    {
        var w = Byond.ParseWorlds(Hub.Replace("\n", "\r\n"));
        Check(w.Count == 2, "hostile url dropped, 2 worlds left");
        Check(w[0].Url == "byond://BYOND.world.954870843" && w[0].Name == "(12.0.3) · BYOND.world.954870843" && w[0].Players.Count == 0, "world 1");
        Check(w[1].Status == "Test \"Server\"","status html stripped + unescaped");
        Check(w[1].Players.SequenceEqual(["Some Duelist", "Some-One"]), "players list");
        Check(Byond.Ckey("Some Duelist") == "someduelist" && Byond.Ckey("Some-One") == "someone", "ckey");
        Check(Updater.IsNewer("v0.2.0", new Version(0, 1, 0, 0)), "newer tag");
        Check(!Updater.IsNewer("v0.1.0", new Version(0, 1, 0, 0)), "same version (3 vs 4 parts)");
        Check(!Updater.IsNewer("garbage", new Version(0, 1, 0)), "bad tag");
        Console.WriteLine("selftest OK");
        return 0;
    }

    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception($"selftest FAILED: {what}");
    }
}
