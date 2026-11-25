using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: AssemblyTitle("WanjieScroll Windows Launcher")]
[assembly: AssemblyProduct("WanjieScroll")]

internal static class WindowsLauncher
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        bool installOnly = args.Length >= 1 && args[0] == "--install-only";
        if ((args.Length != 0 && !installOnly) || args.Length > 2)
        {
            Console.Error.WriteLine("Usage: WanjieScroll-Play.exe [--install-only [player.zip]]");
            return 2;
        }

        string scratch = Path.Combine(Path.GetTempPath(), "WanjieScroll-" + Guid.NewGuid().ToString("N"));
        string installer = Path.Combine(scratch, "Play.ps1");
        string descriptor = Path.Combine(scratch, "PlayableRelease.json");
        string helper = Path.Combine(scratch, "tools", "assets", "Receive-ReleaseArchive.ps1");

        try
        {
            Console.WriteLine("万界长卷");
            Console.WriteLine("首次启动需要下载约 1.6 GB 游戏数据。");
            Extract("Play.ps1", installer);
            Extract("PlayableRelease.json", descriptor);
            Extract("Receive-ReleaseArchive.ps1", helper);

            string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuotePath(installer)
            };
            // Windows PowerShell needs its own built-in module paths when launched from PowerShell 7.
            start.EnvironmentVariables.Remove("PSModulePath");
            if (installOnly)
                start.Arguments += " -InstallOnly";
            if (args.Length == 2)
            {
                string archive = Path.GetFullPath(args[1]);
                if (!File.Exists(archive))
                    throw new FileNotFoundException("找不到本地游戏数据包。", archive);
                start.Arguments += " -PlayerArchivePath " + QuotePath(archive);
            }

            using (var child = Process.Start(start))
            {
                if (child == null)
                    throw new InvalidOperationException("无法启动安装程序。");
                child.WaitForExit();
                if (child.ExitCode != 0)
                    throw new InvalidOperationException("游戏数据安装失败，请检查上方提示。");
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            if (!installOnly && !Console.IsInputRedirected)
            {
                Console.WriteLine("按 Enter 关闭。");
                Console.ReadLine();
            }
            return 1;
        }
        finally
        {
            try
            {
                File.Delete(helper);
                File.Delete(descriptor);
                File.Delete(installer);
                string assets = Path.GetDirectoryName(helper);
                string tools = Path.GetDirectoryName(assets);
                if (Directory.Exists(assets)) Directory.Delete(assets);
                if (Directory.Exists(tools)) Directory.Delete(tools);
                if (Directory.Exists(scratch)) Directory.Delete(scratch);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Extract(string resource, string destination)
    {
        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
        {
            if (source == null)
                throw new InvalidOperationException("启动器缺少安装资源 " + resource);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            using (Stream output = File.Open(destination, FileMode.CreateNew, FileAccess.Write))
                source.CopyTo(output);
        }
    }

    private static string QuotePath(string path)
    {
        if (path.IndexOf('"') >= 0)
            throw new ArgumentException("文件路径包含无效字符。", "path");
        return "\"" + path + "\"";
    }
}
