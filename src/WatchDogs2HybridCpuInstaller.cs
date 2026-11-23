using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Watch Dogs 2 Hybrid CPU Fix")]
[assembly: AssemblyDescription("Installs or removes a Windows compatibility fix for Watch Dogs 2")]
[assembly: AssemblyProduct("Watch Dogs 2 Hybrid CPU Fix")]
[assembly: AssemblyVersion("1.0.0.0")]

internal static class Installer
{
    private const string Title = "Watch Dogs 2 Hybrid CPU Fix";
    private const string DatabaseId = "{07076d86-4014-5cbe-a359-18019a3d9108}";
    private const string CompatibilityKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags";

    [DllImport("apphelp.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SdbInitDatabase(uint flags, string path);

    [DllImport("apphelp.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SdbGetMatchingExe(IntPtr database, string path, string module,
        IntPtr environment, uint flags, [Out] byte[] result);

    [DllImport("apphelp.dll")]
    private static extern void SdbReleaseDatabase(IntPtr database);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--status")
                return IsInstalled() ? 0 : 2;

            if (args.Length == 2 && args[0] == "--check")
            {
                using (var payload = new DatabasePayload())
                    return MatchesGameFolder(payload.Path, args[1]) ? 0 : 2;
            }

            if (args.Length != 0)
                return 1;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var dialog = new SetupForm())
                Application.Run(dialog);
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 0)
                MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static byte[] ReadPayload()
    {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("WatchDogs2.sdb"))
        {
            if (input == null)
                throw new InvalidOperationException("The installer is missing its compatibility database.");
            using (var buffer = new MemoryStream())
            {
                input.CopyTo(buffer);
                return buffer.ToArray();
            }
        }
    }

    private sealed class DatabasePayload : IDisposable
    {
        private FileStream stream;
        internal readonly string Path;

        internal DatabasePayload()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "WatchDogs2-" + Guid.NewGuid().ToString("N") + ".sdb");
            try
            {
                byte[] bytes = ReadPayload();
                using (var output = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                foreach (byte expected in bytes)
                {
                    if (stream.ReadByte() != expected)
                        throw new IOException("The temporary compatibility database changed.");
                }
                if (stream.ReadByte() != -1)
                    throw new IOException("The temporary compatibility database changed.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (stream == null)
                return;
            stream.Dispose();
            stream = null;
            File.Delete(Path);
        }
    }

    private static bool MatchesGameFolder(string databasePath, string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        const uint databaseFullPath = 2;
        IntPtr database = SdbInitDatabase(databaseFullPath, databasePath);
        if (database == IntPtr.Zero)
            throw new InvalidOperationException("Windows could not open the compatibility database.");
        try
        {
            foreach (string directory in new[] { Path.Combine(folder, "bin"), Path.Combine(folder, "bin_plus"), folder })
            {
                string exe = Path.Combine(directory, "WatchDogs2.exe");
                if (File.Exists(exe) && SdbGetMatchingExe(database, exe, null, IntPtr.Zero, 0, new byte[4096]))
                    return true;
            }
            return false;
        }
        finally
        {
            SdbReleaseDatabase(database);
        }
    }

    private static string InstalledDatabase()
    {
        using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var db = hive.OpenSubKey(CompatibilityKey + @"\InstalledSDB\" + DatabaseId))
        using (var app = hive.OpenSubKey(CompatibilityKey + @"\Custom\WatchDogs2.exe"))
        {
            if (db == null || app == null || app.GetValue(DatabaseId + ".sdb") == null)
                return null;
            return db.GetValue("DatabasePath") as string;
        }
    }

    private static bool IsInstalled()
    {
        string path = InstalledDatabase();
        if (path == null || !File.Exists(path))
            return false;
        using (var sha = SHA256.Create())
        {
            return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))) ==
                Convert.ToBase64String(sha.ComputeHash(ReadPayload()));
        }
    }

    private static string FindUbisoftFolder()
    {
        using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        using (var install = hive.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs\2688"))
        {
            if (install != null)
                return install.GetValue("InstallDir") as string ?? "";
        }
        return "";
    }

    private static void RunWindowsInstaller(string arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sdbinst.exe"), arguments);
        info.UseShellExecute = true;
        info.Verb = "runas";
        info.WindowStyle = ProcessWindowStyle.Hidden;
        using (Process process = Process.Start(info))
        {
            if (process == null)
                throw new InvalidOperationException("Windows could not start its compatibility installer.");
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("The Windows compatibility installer returned exit code " + process.ExitCode + ".");
        }
    }

    private sealed class SetupForm : Form
    {
        private readonly TextBox folder = new TextBox();
        private readonly Label status = new Label();
        private readonly Button install = new Button();
        private readonly Button remove = new Button();

        internal SetupForm()
        {
            Text = Title;
            ClientSize = new System.Drawing.Size(525, 192);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 2;
            layout.RowCount = 5;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 5; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddLabel(layout, "Installs the Windows hybrid-CPU compatibility fix for supported Watch Dogs 2 builds. The fix is experimental.", 0);
            AddLabel(layout, "Watch Dogs 2 installation folder", 1);
            folder.Dock = DockStyle.Fill;
            folder.Text = FindUbisoftFolder();
            layout.Controls.Add(folder, 0, 2);

            var browse = new Button { Text = "Browse…", AutoSize = true };
            browse.Click += Browse;
            layout.Controls.Add(browse, 1, 2);

            status.AutoSize = true;
            status.Margin = new Padding(3, 8, 3, 8);
            layout.Controls.Add(status, 0, 3);
            layout.SetColumnSpan(status, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
            install.Text = "Install fix";
            install.AutoSize = true;
            install.Click += InstallFix;
            remove.Text = "Uninstall fix";
            remove.AutoSize = true;
            remove.Click += RemoveFix;
            buttons.Controls.Add(close);
            buttons.Controls.Add(install);
            buttons.Controls.Add(remove);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);
            CancelButton = close;
            RefreshStatus();
        }

        private static void AddLabel(TableLayoutPanel layout, string text, int row)
        {
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new System.Drawing.Size(490, 0) };
            label.Margin = new Padding(3, 3, 3, 7);
            layout.Controls.Add(label, 0, row);
            layout.SetColumnSpan(label, 2);
        }

        private void RefreshStatus()
        {
            bool installed = IsInstalled();
            status.Text = installed ? "Status: Fix installed" : "Status: Fix not installed";
            install.Enabled = !installed;
            remove.Enabled = InstalledDatabase() != null;
        }

        private void Browse(object sender, EventArgs args)
        {
            using (var picker = new FolderBrowserDialog())
            {
                picker.Description = "Select the Watch Dogs 2 installation folder.";
                picker.ShowNewFolderButton = false;
                if (Directory.Exists(folder.Text))
                    picker.SelectedPath = folder.Text;
                if (picker.ShowDialog(this) == DialogResult.OK)
                    folder.Text = picker.SelectedPath;
            }
        }

        private void InstallFix(object sender, EventArgs args)
        {
            Perform(() =>
            {
                using (var payload = new DatabasePayload())
                {
                    if (!MatchesGameFolder(payload.Path, folder.Text.Trim()))
                    {
                        MessageBox.Show(this, "No supported Watch Dogs 2 build was found in that folder. Select the game folder and try again.",
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    RunWindowsInstaller("-q \"" + payload.Path + "\"");
                }
                if (!IsInstalled())
                    throw new InvalidOperationException("Windows did not register the expected fix.");
            });
        }

        private void RemoveFix(object sender, EventArgs args)
        {
            if (MessageBox.Show(this, "Uninstall the Watch Dogs 2 hybrid-CPU fix?", Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Perform(() =>
            {
                RunWindowsInstaller("-q -u -g " + DatabaseId);
                if (InstalledDatabase() != null)
                    throw new InvalidOperationException("Windows still reports the fix as installed.");
            });
        }

        private void Perform(Action action)
        {
            try
            {
                UseWaitCursor = true;
                action();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                string message = ex.NativeErrorCode == 1223 ? "The administrator prompt was canceled." : ex.Message;
                MessageBox.Show(this, message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                UseWaitCursor = false;
                RefreshStatus();
            }
        }
    }
}
