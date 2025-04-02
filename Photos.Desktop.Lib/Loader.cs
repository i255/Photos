using Photos.Core;
using System.Diagnostics;
using System.Threading;

namespace Photos.Desktop;

public static class Loader
{
    static string dataPath;
    static string openFile = null;
    static bool latinTitle;

    static void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "/register" when OperatingSystem.IsWindows():
                    RegistryUtils.Register(true);
                    Environment.Exit(0);
                    break;
                case "/data_path" when i + 1 < args.Length:
                    dataPath = args[++i];
                    break;
                case "/latin_title":
                    latinTitle = true;
                    break;
                case "/perf_test":
                    PhotoView.PerfTest = DateTime.UtcNow;
                    break;
                default:
                    if (File.Exists(args[i]))
                        openFile = args[i];
                    break;
            }
        }
    }

    public static void Run(string[] args, Action<PhotoProvider> photoProviderSetup = null, Action<GameSetup> gameSetup = null)
    {
        if (dataPath == null)
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localApplicationData))
                localApplicationData = "data";

            dataPath = Path.Combine(localApplicationData, Core.Utils.LatinAppName);
        }

        ImageLoader.KnownEndings.AddRange(DesktopImageLoader.KnownEndings);

        try
        {
            ParseArgs(args);

            var movedFile = Path.Combine(dataPath, "moved.txt");
            if (File.Exists(movedFile))
                dataPath = File.ReadAllText(movedFile);

            Directory.CreateDirectory(dataPath);
            Core.Utils.LogFile = Path.Combine(dataPath, "error_log.txt");

            var fname = "open.txt";
            var commFile = Path.Combine(dataPath, fname);

            var m = new Mutex(false, "uPhoto_Mutex_" + dataPath.Replace('\\', '_').Replace('/', '_'));
            var isFirst = m.WaitOne(0);

            var folderLibraryMode = true;

            if (isFirst)
            {
                try
                {
                    var editorDir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    var provider = Task.Run(() => new PhotoProvider(dataPath, editorDir, openFile, folderLibraryMode: folderLibraryMode, setupEvents: photoProviderSetup));

                    using var game = new Game(latinTitle || OperatingSystem.IsLinux(), provider, gameSetup);

                    using var watcher = new FileSystemWatcher(dataPath, fname);
                    watcher.Created += (x, y) =>
                    {
                        Thread.Sleep(10);
                        string text = null;
                        try
                        {
                            text = File.ReadAllText(commFile);
                            File.Delete(commFile);
                        }
                        catch { }

                        if (text != null)
                            game.OpenFile(text);
                    };

                    try {
                        watcher.EnableRaisingEvents = true;
                    } catch(Exception ex) { Core.Utils.LogError(ex); }

                    game.Run();
                }
                finally
                {
                    m.ReleaseMutex();
                }
            }
            else if (openFile != null)
            {
                var argFile = Path.GetFullPath(openFile);
                File.Delete(commFile);
                File.WriteAllText(commFile, argFile);
            }
        }
        catch (Exception ex)
        {
            try
            {
                Core.Utils.LogError(ex);
                var errFile = Path.Combine(dataPath, "error_message.txt");
                var errMessage = $"{ex.GetType().FullName}: {ex.Message}";
                if (ex.InnerException != null)
                    errMessage += $"; {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}";
                File.WriteAllText(errFile, errMessage);

                Process.Start(new ProcessStartInfo() { FileName = errFile, UseShellExecute = true });
            }
            catch { }
        }
    }
}

