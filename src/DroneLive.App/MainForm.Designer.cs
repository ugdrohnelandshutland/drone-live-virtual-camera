namespace DroneLive.App
{
    partial class MainForm
    {
        /// <summary>
        /// Vom Designer verwaltete Komponenten.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Gibt die Komponenten frei.
        /// </summary>
        /// <param name="disposing">Gibt auch verwaltete Ressourcen frei.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                _tray.Visible = false;
                _diagnose?.Dispose();
                _vorschau.Image?.Dispose();
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Vom Windows-Forms-Designer erzeugter Code

        /// <summary>
        /// Initialisiert die Basiseinstellungen des Formulars.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Text = "DroneLive2VirtualCamera";
        }

        #endregion
    }
}
