namespace REvolutionClient.Launcher.Infrastructure;

internal sealed class LauncherPaths
{
    public LauncherPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Install root is required.", nameof(root));
        Root = Path.GetFullPath(root.Trim());
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
