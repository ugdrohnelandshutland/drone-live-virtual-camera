namespace DroneLive.App
{
    internal static class Program
    {
        /// <summary>
        /// Einstiegspunkt der Anwendung.
        /// </summary>
        [STAThread]
        static void Main()
        {
            using var einzelinstanz = new Mutex(true, "Local\\DroneLiveKamera-Anwendung", out bool ersteInstanz);
            if (!ersteInstanz)
            {
                MessageBox.Show("DroneLive2VirtualCamera läuft bereits. Verwenden Sie das bestehende Fenster oder das Symbol im Infobereich der Taskleiste.", "DroneLive2VirtualCamera", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ApplicationConfiguration.Initialize();
            try { Application.Run(new MainForm()); }
            catch (InvalidDataException ex)
            {
                MessageBox.Show(ex.Message, "DroneLive2VirtualCamera · Konfiguration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
