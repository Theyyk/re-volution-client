namespace REvolutionClient.Launcher.Infrastructure;

internal sealed class LauncherPaths
{
    public LauncherPaths(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Install root is required.", nameof(root));
        Root = Path.GetFullPath(root.Trim());
        LauncherProfile.ValidateInstallRoot(Root);
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
        LauncherProfile.ValidateInstallRoot(Root);
        if (LauncherProfile.IsDevelopment && Directory.Exists(Root))
        {
            var pending = new Stack<string>();
            pending.Push(Root);
            while (pending.TryPop(out var parent))
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(parent))
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("DEV installation contains a file/directory link or junction.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                }
            }
        }
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Game);
        Directory.CreateDirectory(Downloads);
    }
}
