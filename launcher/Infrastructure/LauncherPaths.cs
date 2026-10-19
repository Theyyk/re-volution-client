namespace REvolutionClient.Launcher.Infrastructure;

internal sealed class LauncherPaths
{
    public LauncherPaths()
    {
        Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "REvolutionClient");
        Runtime = Path.Combine(Root, "runtime", "java8");
        Game = Path.Combine(Root, "game");
        Downloads = Path.Combine(Root, "downloads");
    }

    public string Root { get; }
    public string Runtime { get; }
    public string Game { get; }
    public string Downloads { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Game);
        Directory.CreateDirectory(Downloads);
    }
}
