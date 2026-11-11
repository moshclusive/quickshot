using QuickShot.Core;
using QuickShot.UI;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QuickShot
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;
        private static EventWaitHandle _showWindowEvent;

        [STAThread]
        private static void Main(string[] args)
        {
            const string showEventName = @"Local\QuickShot_ShowMainWindow_v2";
            _singleInstanceMutex = new Mutex(true, @"Local\QuickShot_SingleInstance_Mutex_v2", out bool createdNew);
            if (!createdNew)
            {
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(showEventName);
                    ev.Set();
                }
                catch { }
                return;
            }

            _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.EnsureFontsLoaded();

            bool startInTray = args != null && args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            var settings = AppSettings.Load();
            var storage = new StorageService(settings);

            using var syncTarget = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
            var _ = syncTarget.Handle;

            var hotkeys = new HotkeyManager(syncTarget);
            TrayController tray = null;
            MainForm mainForm = null;
            var cts = new CancellationTokenSource();

            void QuitCompletely()
            {
                cts.Cancel();
                try { _showWindowEvent?.Set(); } catch { }
                try { tray?.Dispose(); } catch { }
                try { hotkeys?.Dispose(); } catch { }
                if (mainForm != null && !mainForm.IsDisposed)
                {
                    mainForm.AllowClose = true;
                    try { mainForm.Close(); mainForm.Dispose(); } catch { }
                }
                try { _singleInstanceMutex?.ReleaseMutex(); _singleInstanceMutex?.Dispose(); } catch { }
                Application.ExitThread();
                Environment.Exit(0);
            }

            mainForm = new MainForm(settings, storage, hotkeys,
                (mode, delay) => tray?.TriggerCapture(mode, delay),
                QuitCompletely);

            tray = new TrayController(settings, storage, hotkeys, mainForm, startInTray, QuitCompletely);

            Task.Run(() =>
            {
                while (!cts.IsCancellationRequested)
                {
                    if (_showWindowEvent.WaitOne(500) && !cts.IsCancellationRequested)
                    {
                        try
                        {
                            syncTarget.BeginInvoke(new Action(() => tray?.ShowMainWindow()));
                        }
                        catch { }
                    }
                }
            });

            Application.Run();
        }
    }
}
