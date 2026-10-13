using System.Collections.Concurrent;
using System.Text;
using DroneLive.Core;

namespace DroneLive.App;

public partial class MainForm : Form
{
    private const string Anwendungsname = "DroneLive2VirtualCamera";
    private readonly AppConfiguration _konfiguration;
    private UserSettings _einstellungen;
    private readonly string? _einstellungsDatei;
    private readonly Func<bool, bool> _beendenBestaetigen;
    private readonly TextBox _server = new() { Name = "Serveradresse", ReadOnly = true, TabStop = false, BorderStyle = BorderStyle.None, BackColor = AppTheme.Grund, ForeColor = AppTheme.Text, Font = AppTheme.Bedienung, TextAlign = HorizontalAlignment.Right, Width = 240, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
    private readonly DroneListBox _drohnen = new() { Name = "Drohnenauswahl", Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "Drohne" };
    private readonly QualitySelector _aufloesung = new() { Name = "Ausgabeaufloesung", Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "Bildqualität" };
    private readonly RoundButton _laden = new() { Name = "Aktualisieren", Size = new Size(44, 44), Anchor = AnchorStyles.Right, Margin = new Padding(12, 0, 0, 0), AccessibleName = "Drohnenliste aktualisieren" };
    private readonly RoundButton _uebertragung = new() { Name = "Uebertragung", Text = "Übertragung starten", Dock = DockStyle.Fill, Margin = Padding.Empty, Font = AppTheme.Bedienung, Enabled = false };
    private readonly Label _status = new() { Name = "Verbindungsstatus", Text = "Kein Signal", AutoSize = true, Font = AppTheme.Name, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
    private readonly StatusDot _statusPunkt = new() { Size = new Size(10, 10), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 7, 0) };
    private readonly Label _anzahl = new() { AutoSize = true, Font = AppTheme.Klein, ForeColor = AppTheme.Leise, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
    private readonly PictureBox _schloss = new() { Size = new Size(16, 20), SizeMode = PictureBoxSizeMode.CenterImage, Margin = Padding.Empty, Visible = false, AccessibleName = "gesperrt" };
    private readonly Label _kachelNummer = new() { Dock = DockStyle.Fill, Font = AppTheme.NummerGross, TextAlign = ContentAlignment.MiddleCenter, BackColor = AppTheme.SlotLeer, ForeColor = AppTheme.Text, Margin = Padding.Empty, Text = "–" };
    private readonly Label _kachelName = new() { AutoSize = true, Font = AppTheme.Name, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = Padding.Empty, AutoEllipsis = true };
    private readonly Label _kachelFormat = new() { AutoSize = true, Font = AppTheme.Klein, ForeColor = AppTheme.Leise, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = Padding.Empty };
    private readonly Label _hinweis = new() { Dock = DockStyle.Fill, Text = "Die Drohnenliste wird geladen …", AutoEllipsis = true, Font = AppTheme.Text15, ForeColor = AppTheme.Leise, Margin = new Padding(0, 12, 0, 0) };
    private readonly PictureBox _vorschau = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = AppTheme.Bildrand, Margin = Padding.Empty };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly NotifyIcon _tray = new();
    private readonly ToolStripMenuItem _trayStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _trayOeffnen = new("Fenster öffnen");
    private readonly ToolStripMenuItem _trayStoppen = new("Übertragung stoppen");
    private readonly ToolStripMenuItem _trayBeenden = new("Beenden …");
    private readonly ToolTip _hilfe = new();
    private readonly ApplicationIcons _symbole;
    private readonly ConcurrentQueue<string> _meldungen = new();
    private readonly StringBuilder _protokoll = new();
    private readonly CancellationTokenSource _lebenszeit = new();
    private readonly SemaphoreSlim _ladenSperre = new(1);
    private readonly HubClient _hub;
    private Form? _diagnose;
    private TextBox? _diagnoseText;
    private CancellationTokenSource? _statusAbbruch;
    private Task? _statusAufgabe;
    private Task? _ladeAufgabe;
    private Task? _startAufgabe;
    private Task? _stopAufgabe;
    private Task? _kameraAufgabe;
    private CancellationTokenSource? _streamAbbruch;
    private Task? _streamAufgabe;
    private readonly Func<int, int, ICameraOutput> _kameraErstellen;
    // Die Kamera lebt von Programmstart bis Programmende, damit Teams sie beim Drohnenwechsel behält.
    private ICameraOutput? _kamera;
    private string? _kameraFehler;
    private byte[]? _letztesBild;
    private long _bildzeit;
    private byte[]? _angezeigtesBild;
    private byte[] _keinSignal = [];
    private int _breite = 1280, _hoehe = 720;
    private bool _schliesst, _darfSchliessen, _beendenBestaetigt, _imTray, _trayHinweisGezeigt;
    private bool _beschaeftigt, _laedt, _serverBereit, _sitzungAktiv, _listeWirdGesetzt;
    private string? _laufendeDrohnenId;
    private string _zustand = "Kein Signal";

    public MainForm() : this(AppConfiguration.Laden(), UserSettings.Laden()) { }

    internal MainForm(AppConfiguration konfiguration, UserSettings einstellungen,
        Func<bool, bool>? beendenBestaetigen = null, bool automatischLaden = true, string? einstellungsDatei = null,
        Func<int, int, ICameraOutput>? kameraErstellen = null)
    {
        _konfiguration = konfiguration;
        _konfiguration.Pruefen();
        _einstellungen = einstellungen;
        _einstellungsDatei = einstellungsDatei;
        _hub = new HubClient(_konfiguration.Serveradresse);
        _beendenBestaetigen = beendenBestaetigen ?? BestaetigeBeenden;
        _kameraErstellen = kameraErstellen ?? ((breite, hoehe) => new VirtualCameraOutput(breite, hoehe));
        InitializeComponent();
        components ??= new System.ComponentModel.Container();
        components.Add(_timer);
        components.Add(_tray);
        components.Add(_hilfe);
        _symbole = new ApplicationIcons(components);
        Text = Anwendungsname;
        Icon = _symbole.Anwendung;
        Font = new Font("Segoe UI", 9);
        BackColor = AppTheme.Grund;
        ForeColor = AppTheme.Text;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(780, 548);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        ErstelleOberflaeche();
        ErstelleTrayMenue();
        // In der Kopfzeile reicht der Servername; die vollständige Adresse steht im Tooltip.
        _server.Text = Servername(_hub.Basis);
        int adressbreite = TextRenderer.MeasureText(_server.Text, _server.Font).Width * 96 / DeviceDpi + 6;
        _server.Width = Math.Min(260, adressbreite);
        _hilfe.SetToolTip(_server, _konfiguration.Serveradresse + "\nDie Serveradresse wird über configuration.json festgelegt.");
        _hilfe.SetToolTip(_laden, "Drohnenliste aktualisieren (F5)");
        foreach (Control option in _aufloesung.Controls[0].Controls)
            _hilfe.SetToolTip(option, "HD benötigt weniger Rechenleistung. Eine Änderung stellt die Kamera neu bereit; in der Videokonferenz muss sie dann gegebenenfalls erneut ausgewählt werden.");
        _aufloesung.SelectedIndex = _einstellungen.Breite == 1920 ? 1 : 0;
        SetzeAufloesung();
        _aufloesung.SelectedIndexChanged += async (_, _) =>
        {
            if (_sitzungAktiv || _beschaeftigt) return;
            SetzeAufloesung();
            await AktualisiereKameraAsync();
        };
        _drohnen.SelectedIndexChanged += (_, _) => { if (!_listeWirdGesetzt && !_sitzungAktiv && !_beschaeftigt) { ZeigeAuswahlhinweis(); SynchronisiereBedienung(); } };
        _laden.Click += async (_, _) => await LadeServerAsync();
        // Eine Schaltfläche für beides: Sie startet ohne Übertragung und stoppt eine laufende.
        _uebertragung.Click += async (_, _) => { if (_sitzungAktiv) await StoppeAsync(); else await StarteAsync(); };
        _timer.Tick += (_, _) => AktualisiereVorschau();
        _timer.Start();
        SetzeStatus("Kein Signal", "Die Drohnenliste wird geladen …");
        AktualisiereVorschau();
        SynchronisiereBedienung();
        Shown += async (_, _) =>
        {
            await AktualisiereKameraAsync();
            if (automatischLaden) await LadeServerAsync();
        };
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) MinimiereInTray(); };
        FormClosing += SchliesseAsync;
        KeyDown += async (_, e) => { if (e.KeyCode == Keys.F5 && _laden.Enabled) { e.Handled = true; await LadeServerAsync(); } };
    }

    /// <summary>Zeichnet Fenster und alle Steuerelemente gemeinsam im Hintergrund (WS_EX_COMPOSITED), damit Aktualisierungen nicht flackern.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameter = base.CreateParams;
            parameter.ExStyle |= 0x02000000;
            return parameter;
        }
    }

    /// <summary>Host mit Port, falls dieser vom Standard des Protokolls abweicht.</summary>
    internal static string Servername(Uri adresse) => adresse.IsDefaultPort ? adresse.Host : $"{adresse.Host}:{adresse.Port}";

    private void ErstelleOberflaeche()
    {
        // Aufbau nach dem Designentwurf: Kopfzeile, links Bedienung, rechts Vorschau-Kachel, Fußzeile.
        // Deckende Hintergründe statt Transparenz: Durchsichtige Container zeichnen den Elternhintergrund für jedes Kind neu.
        static TableLayoutPanel Raster(int spalten, int zeilen, Padding? innen = null, Color? farbe = null) => new()
        { Dock = DockStyle.Fill, ColumnCount = spalten, RowCount = zeilen, Margin = Padding.Empty, Padding = innen ?? Padding.Empty, BackColor = farbe ?? AppTheme.Grund };
        static Label Kennzeichnung(string text) => new()
        { Text = text.ToUpperInvariant(), AutoSize = true, Font = AppTheme.Kennzeichnung, ForeColor = AppTheme.Leise, Anchor = AnchorStyles.Left, Margin = Padding.Empty };

        var wurzel = Raster(1, 4, new Padding(16, 0, 16, 14));
        wurzel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        wurzel.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        wurzel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wurzel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        var kopf = Raster(5, 1);
        kopf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        kopf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        kopf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        kopf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        kopf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        kopf.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        int symbolGroesse = LogicalToDeviceUnits(34);
        var symbol = new PictureBox { Size = new Size(34, 34), SizeMode = PictureBoxSizeMode.CenterImage, Image = BrandGraphics.ErzeugeSymbol(symbolGroesse), Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0) };
        var wortmarke = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty, BackColor = AppTheme.Grund };
        wortmarke.Controls.Add(new WordmarkLabel { Margin = new Padding(0, 0, 12, 0) });
        wortmarke.Controls.Add(new Label { Text = "FÜR VIDEOKONFERENZEN", AutoSize = true, Font = AppTheme.Kennzeichnung, ForeColor = AppTheme.Leise, Margin = new Padding(0, 11, 0, 0) });
        var server = Raster(1, 2);
        server.Dock = DockStyle.None;
        server.AutoSize = true;
        server.Anchor = AnchorStyles.Right;
        server.Controls.Add(new Label { Text = "Server", AutoSize = true, Font = AppTheme.Klein, ForeColor = AppTheme.Leise, Anchor = AnchorStyles.Right, Margin = Padding.Empty }, 0, 0);
        server.Controls.Add(_server, 0, 1);
        _laden.Image = _symbole[Symbol.Aktualisieren];
        kopf.Controls.Add(symbol, 0, 0);
        kopf.Controls.Add(wortmarke, 1, 0);
        kopf.Controls.Add(server, 3, 0);
        kopf.Controls.Add(_laden, 4, 0);
        wurzel.Controls.Add(kopf, 0, 0);
        wurzel.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Linie, Margin = Padding.Empty }, 0, 1);

        var koerper = Raster(2, 1);
        koerper.Margin = new Padding(0, 14, 0, 14);
        koerper.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        koerper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        koerper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var links = Raster(1, 6);
        links.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        links.RowStyles.Add(new RowStyle(SizeType.Absolute, DroneListBox.SichtbareZeilen * DroneListBox.Zeilenhoehe - DroneListBox.Abstand));
        links.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        links.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        links.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        links.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var drohnenKopf = Raster(2, 1);
        drohnenKopf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        drohnenKopf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        drohnenKopf.Controls.Add(Kennzeichnung("Drohne"), 0, 0);
        var anzahlBlock = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty, BackColor = AppTheme.Grund };
        _schloss.Image = _symbole[Symbol.Schloss];
        anzahlBlock.Controls.Add(_schloss);
        anzahlBlock.Controls.Add(_anzahl);
        drohnenKopf.Controls.Add(anzahlBlock, 1, 0);
        links.Controls.Add(drohnenKopf, 0, 0);
        links.Controls.Add(_drohnen, 0, 1);
        var qualitaet = Kennzeichnung("Bildqualität");
        qualitaet.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        qualitaet.Margin = new Padding(0, 0, 0, 8);
        links.Controls.Add(qualitaet, 0, 2);
        links.Controls.Add(_aufloesung, 0, 3);
        links.Controls.Add(_uebertragung, 0, 5);
        koerper.Controls.Add(links, 0, 0);

        var rechts = Raster(1, 2);
        rechts.Margin = new Padding(16, 0, 0, 0);
        rechts.RowStyles.Add(new RowStyle(SizeType.Absolute, 301));
        rechts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var kachel = new RoundedPanel { Dock = DockStyle.Fill, BackColor = AppTheme.Flaeche, Margin = Padding.Empty };
        var kachelRaster = Raster(1, 2, farbe: AppTheme.Flaeche);
        kachelRaster.RowStyles.Add(new RowStyle(SizeType.Absolute, 243));
        kachelRaster.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        kachelRaster.Controls.Add(_vorschau, 0, 0);
        var leiste = Raster(3, 1, farbe: AppTheme.Flaeche);
        leiste.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        leiste.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leiste.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        leiste.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var namensblock = Raster(1, 2, new Padding(12, 0, 8, 0), AppTheme.Flaeche);
        namensblock.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        namensblock.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        namensblock.Controls.Add(_kachelName, 0, 0);
        namensblock.Controls.Add(_kachelFormat, 0, 1);
        var statusblock = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 14, 0), BackColor = AppTheme.Flaeche };
        statusblock.Controls.Add(_statusPunkt);
        statusblock.Controls.Add(_status);
        leiste.Controls.Add(_kachelNummer, 0, 0);
        leiste.Controls.Add(namensblock, 1, 0);
        leiste.Controls.Add(statusblock, 2, 0);
        kachelRaster.Controls.Add(leiste, 0, 1);
        kachel.Controls.Add(kachelRaster);
        rechts.Controls.Add(kachel, 0, 0);
        rechts.Controls.Add(_hinweis, 0, 1);
        koerper.Controls.Add(rechts, 1, 0);
        wurzel.Controls.Add(koerper, 0, 2);

        var fusszeile = Raster(2, 1);
        fusszeile.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fusszeile.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fusszeile.Controls.Add(new Label { Text = "In der Videokonferenz die Kamera „Drone Live Kamera“ wählen.", AutoSize = true, Font = AppTheme.Klein, ForeColor = AppTheme.Leise, Anchor = AnchorStyles.Left, Margin = Padding.Empty }, 0, 0);
        var diagnose = new LinkLabel { Text = "Diagnose", AutoSize = true, Font = AppTheme.Klein, Anchor = AnchorStyles.Right, LinkBehavior = LinkBehavior.AlwaysUnderline, LinkColor = AppTheme.Akzent, ActiveLinkColor = AppTheme.AkzentHover, Margin = Padding.Empty };
        diagnose.LinkClicked += (_, _) => ZeigeDiagnose();
        fusszeile.Controls.Add(diagnose, 1, 0);
        wurzel.Controls.Add(fusszeile, 0, 3);
        Controls.Add(wurzel);
    }

    private void ErstelleTrayMenue()
    {
        var menue = new ContextMenuStrip(components);
        _trayOeffnen.Image = _symbole[Symbol.Fenster];
        _trayStoppen.Image = _symbole[Symbol.Stopp];
        _trayBeenden.Image = _symbole[Symbol.Beenden];
        menue.Items.AddRange([_trayStatus, new ToolStripSeparator(), _trayOeffnen, _trayStoppen, new ToolStripSeparator(), _trayBeenden]);
        _tray.ContextMenuStrip = menue;
        _tray.Icon = _symbole.Tray("Bereit");
        _tray.Text = Anwendungsname;
        _tray.DoubleClick += (_, _) => ZeigeFenster();
        _trayOeffnen.Click += (_, _) => ZeigeFenster();
        _trayStoppen.Click += async (_, _) => await StoppeAsync();
        _trayBeenden.Click += (_, _) =>
        {
            if (_beschaeftigt || _schliesst || !_beendenBestaetigen(_sitzungAktiv)) return;
            _beendenBestaetigt = true;
            Close();
        };
    }

    private bool BestaetigeBeenden(bool aktiv) => MessageBox.Show(this,
        (aktiv ? "Die laufende Übertragung wird beendet.\n" : "") +
        (_kamera != null ? "Die virtuelle Kamera steht der Videokonferenz danach nicht mehr zur Verfügung.\n" : "") +
        "DroneLive2VirtualCamera wirklich schließen?",
        Anwendungsname, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void MinimiereInTray()
    {
        if (_schliesst || _imTray) return;
        _imTray = true;
        _tray.Visible = true;
        _diagnose?.Hide();
        Hide();
        ShowInTaskbar = false;
        if (!_trayHinweisGezeigt)
        {
            _trayHinweisGezeigt = true;
            _tray.ShowBalloonTip(2000, Anwendungsname,
                _sitzungAktiv ? "Die Übertragung läuft weiter. Doppelklick auf das Symbol öffnet das Fenster."
                : _kamera != null ? "Die virtuelle Kamera bleibt verfügbar. Doppelklick auf das Symbol öffnet das Fenster."
                : "Doppelklick auf das Symbol öffnet das Fenster. Über das Menü können Sie die Anwendung beenden.", ToolTipIcon.Info);
        }
    }

    private void ZeigeFenster()
    {
        if (_schliesst) return;
        // Beim Wiederherstellen den Tray-Zustand bis nach Show beibehalten;
        // Windows aktualisiert den minimierten Zustand beim Anzeigen des Handles.
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        _imTray = false;
        Activate();
        _tray.Visible = false;
    }

    private void SetzeAufloesung()
    {
        (_breite, _hoehe) = _aufloesung.SelectedIndex == 1 ? (1920, 1080) : (1280, 720);
        _keinSignal = Bildwerkzeuge.KeinSignal(_breite, _hoehe);
        Interlocked.Exchange(ref _letztesBild, null);
        _angezeigtesBild = null;
    }

    private bool KannStarten => _serverBereit && !_beschaeftigt && !_sitzungAktiv && !_schliesst &&
        _drohnen.SelectedItem is Drohne { Zustand: "live", Codec: null or "avc" or "h264" };

    private void SynchronisiereBedienung()
    {
        // Alle Zuweisungen nur bei echten Änderungen: Jede gleiche Zuweisung zeichnet sonst neu und lässt das Fenster zucken.
        bool bereit = !_sitzungAktiv && !_beschaeftigt && !_schliesst;
        Setze(_drohnen, bereit && _serverBereit && _drohnen.Items.Count > 0);
        Setze(_aufloesung, bereit);
        bool ladenMoeglich = bereit && !_laedt;
        Setze(_laden, ladenMoeglich);
        _laden.Hintergrund = ladenMoeglich ? AppTheme.Flaeche : AppTheme.Gesperrt;
        _laden.HoverHintergrund = AppTheme.Grund;
        _laden.Rand = ladenMoeglich ? AppTheme.LinieStark : AppTheme.Linie;
        GestalteUebertragung();
        _trayStoppen.Enabled = _sitzungAktiv && !_beschaeftigt && !_schliesst;
        _trayBeenden.Enabled = !_beschaeftigt && !_schliesst;
        if (UseWaitCursor != _beschaeftigt) UseWaitCursor = _beschaeftigt;
        string trayText = Anwendungsname + " · " + (_sitzungAktiv ? _zustand : "Keine Übertragung");
        if (_tray.Text != trayText) _tray.Text = trayText;
        // Tray und Taskleiste zeigen denselben Zustand.
        string symbolZustand = _sitzungAktiv ? _zustand : "Bereit";
        var trayIcon = _symbole.Tray(symbolZustand);
        if (!ReferenceEquals(_tray.Icon, trayIcon)) _tray.Icon = trayIcon;
        var fensterIcon = _symbole.Fenster(symbolZustand);
        if (!ReferenceEquals(Icon, fensterIcon)) Icon = fensterIcon;
        string trayStatus = _sitzungAktiv ? _zustand : "Keine Übertragung";
        if (_trayStatus.Text != trayStatus) _trayStatus.Text = trayStatus;
        var trayBild = _symbole[_zustand == "Verbunden" ? Symbol.Verbunden : _zustand == "Fehler" ? Symbol.Fehler : Symbol.KeinSignal];
        if (!ReferenceEquals(_trayStatus.Image, trayBild)) _trayStatus.Image = trayBild;
        AktualisiereKachel();
    }

    private static void Setze(Control steuerelement, bool aktiv) { if (steuerelement.Enabled != aktiv) steuerelement.Enabled = aktiv; }
    private static void Setze(Control steuerelement, string text) { if (steuerelement.Text != text) steuerelement.Text = text; }
    private static void Setze(ButtonBase schaltflaeche, Image bild) { if (!ReferenceEquals(schaltflaeche.Image, bild)) schaltflaeche.Image = bild; }

    private void GestalteUebertragung()
    {
        bool stoppen = _sitzungAktiv;
        Setze(_uebertragung, stoppen ? "Übertragung stoppen" : "Übertragung starten");
        Setze(_uebertragung, stoppen ? !_beschaeftigt && !_schliesst : KannStarten);
        if (stoppen)
        {
            Setze(_uebertragung, _symbole[Symbol.Stopp]);
            (_uebertragung.Hintergrund, _uebertragung.HoverHintergrund, _uebertragung.Rand, _uebertragung.Schriftfarbe) =
                (_uebertragung.Enabled ? AppTheme.Flaeche : AppTheme.Gesperrt, AppTheme.Grund, _uebertragung.Enabled ? AppTheme.LinieStark : AppTheme.Linie, _uebertragung.Enabled ? AppTheme.Text : AppTheme.Leise);
        }
        else if (_uebertragung.Enabled)
        {
            Setze(_uebertragung, _symbole[Symbol.StartHell]);
            (_uebertragung.Hintergrund, _uebertragung.HoverHintergrund, _uebertragung.Rand, _uebertragung.Schriftfarbe) =
                (AppTheme.AkzentFlaeche, AppTheme.AkzentHover, AppTheme.AkzentFlaeche, Color.White);
        }
        else
        {
            Setze(_uebertragung, _symbole[Symbol.Start]);
            (_uebertragung.Hintergrund, _uebertragung.HoverHintergrund, _uebertragung.Rand, _uebertragung.Schriftfarbe) =
                (AppTheme.Gesperrt, AppTheme.Gesperrt, AppTheme.Linie, AppTheme.Leise);
        }
    }

    /// <summary>Vorschau-Leiste und Zähler über der Drohnenliste an Auswahl und Zustand anpassen.</summary>
    private void AktualisiereKachel()
    {
        int index = _drohnen.SelectedIndex;
        var drohne = _drohnen.SelectedItem as Drohne;
        Setze(_kachelNummer, index >= 0 ? (index + 1).ToString() : "–");
        Setze(_kachelName, drohne?.Name ?? "Keine Drohne gewählt");
        Setze(_kachelFormat, (_konfiguration.VirtuelleKamera ? "Kameraausgabe: " : "Nur Vorschau: ") + $"{_breite} × {_hoehe}");
        (_kachelNummer.BackColor, _kachelNummer.ForeColor) = !_sitzungAktiv ? (AppTheme.SlotLeer, AppTheme.Text)
            : _zustand == "Verbunden" ? (AppTheme.Live, Color.White)
            : _zustand == "Fehler" ? (AppTheme.Fehler, Color.White)
            : (AppTheme.KeinSignal, AppTheme.Text);
        var drohnen = _drohnen.Items.OfType<Drohne>().ToArray();
        string anzahl = drohnen.Length == 0 ? "" : $"{drohnen.Count(d => d.IstLive)} von {drohnen.Length} live";
        bool gesperrt = _sitzungAktiv && drohnen.Length > 0;
        Setze(_anzahl, gesperrt ? "gesperrt · " + anzahl : anzahl);
        if (_schloss.Visible != gesperrt) _schloss.Visible = gesperrt;
    }

    private void ZeigeAuswahlhinweis()
    {
        // Ohne virtuelle Kamera ist Teams nicht versorgt; dieser Hinweis hat Vorrang vor der Drohnenauswahl.
        if (_kameraFehler != null) { SetzeStatus("Fehler", _kameraFehler); return; }
        string hinweis = _drohnen.SelectedItem switch
        {
            Drohne { Zustand: not "live" } => "Diese Drohne ist offline. Wählen Sie eine Drohne mit Live-Signal.",
            Drohne { Codec: not (null or "avc" or "h264") } => "Dieses Videoformat wird nicht unterstützt. Bitte H.264 einstellen lassen.",
            Drohne when _kamera != null => "Bereit. Die Kamera „Drone Live Kamera“ ist schon in der Videokonferenz wählbar und zeigt bis zum Start „Kein Signal“.",
            Drohne => "Zum Starten auf „Übertragung starten“ klicken.",
            _ => "Keine Drohne verfügbar. Prüfen Sie den Server und aktualisieren Sie die Liste."
        };
        SetzeStatus("Kein Signal", hinweis);
    }

    private void Log(string meldung)
    {
        if (_meldungen.Count < 200) _meldungen.Enqueue($"{DateTime.Now:HH:mm:ss}  {meldung}");
    }

    private void Ui(Action aktion)
    {
        if (_schliesst || IsDisposed || !IsHandleCreated) return;
        if (!InvokeRequired) { aktion(); return; }
        try { BeginInvoke(aktion); } catch (InvalidOperationException) { }
    }

    private Task AufUiAsync(Action aktion)
    {
        if (_schliesst || IsDisposed || !IsHandleCreated) return Task.CompletedTask;
        if (!InvokeRequired) { aktion(); return Task.CompletedTask; }
        var fertig = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(() =>
            {
                try { if (!_schliesst) aktion(); fertig.TrySetResult(); }
                catch (Exception ex) { fertig.TrySetException(ex); }
            });
        }
        catch (InvalidOperationException) { fertig.TrySetResult(); }
        return fertig.Task;
    }

    private void Status(string zustand, string hinweis)
    {
        if (zustand != "Verbunden") { Interlocked.Exchange(ref _letztesBild, null); _kamera?.KeinSignal(); }
        if (hinweis.StartsWith("Verbindung unterbrochen:")) hinweis = "Keine Verbindung zur Drohne. Die Anwendung verbindet sich automatisch erneut.";
        Ui(() => SetzeStatus(zustand, hinweis));
    }

    private void SetzeStatus(string zustand, string hinweis)
    {
        _zustand = zustand;
        Setze(_status, zustand);
        if (_hinweis.Text != hinweis)
        {
            _hinweis.Text = hinweis;
            _hilfe.SetToolTip(_hinweis, hinweis);
        }
        _status.ForeColor = zustand == "Fehler" ? AppTheme.Fehler : zustand == "Verbunden" ? AppTheme.Live : AppTheme.KeinSignalText;
        _statusPunkt.Farbe = zustand == "Fehler" ? AppTheme.Fehler : zustand == "Verbunden" ? AppTheme.Live : AppTheme.KeinSignal;
        SynchronisiereBedienung();
    }

    private async Task LadeServerAsync()
    {
        if (_laedt || _beschaeftigt || _sitzungAktiv || _schliesst) return;
        // Nur „Aktualisieren“ sperren: Liste, Bildqualität und Start behalten ihr Aussehen, damit nichts flackert.
        // Die Liste wird ohnehin auf dem UI-Thread ausgetauscht und behält dabei die Auswahl.
        _laedt = true;
        if (_serverBereit) SynchronisiereBedienung();
        else SetzeStatus("Kein Signal", "Die Drohnenliste wird geladen …");
        try
        {
            _ladeAufgabe = AktualisiereDrohnenAsync();
            await _ladeAufgabe;
            if (_schliesst) return;
            if (_statusAufgabe == null)
            {
                _statusAbbruch = CancellationTokenSource.CreateLinkedTokenSource(_lebenszeit.Token);
                var token = _statusAbbruch.Token;
                _statusAufgabe = Task.Run(() => _hub.BeobachteAsync(AktualisiereDrohnenAsync, Log, token));
            }
        }
        catch (OperationCanceledException) when (_lebenszeit.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _serverBereit = false;
            Status("Fehler", "Der Server ist nicht erreichbar. Bitte Netzwerk prüfen und die Liste erneut aktualisieren.");
            Log(ex.ToString());
        }
        finally
        {
            _ladeAufgabe = null;
            _laedt = false;
            if (!_schliesst) SynchronisiereBedienung();
        }
    }

    private async Task AktualisiereDrohnenAsync()
    {
        await _ladenSperre.WaitAsync(_lebenszeit.Token);
        try
        {
            var liste = await _hub.LadeDrohnenAsync(_lebenszeit.Token);
            await AufUiAsync(() =>
            {
                string? id = _laufendeDrohnenId ?? (_drohnen.SelectedItem as Drohne)?.Id ?? _einstellungen.DrohnenId;
                var bisher = _drohnen.SelectedItem as Drohne;
                var neu = liste.ToList();
                var gewaehlt = neu.FirstOrDefault(d => d.Id == id);
                if (_sitzungAktiv && gewaehlt == null && bisher != null)
                {
                    gewaehlt = bisher with { Zustand = "offline" };
                    neu.Add(gewaehlt);
                }
                gewaehlt ??= neu.FirstOrDefault(d => d.Zustand == "live" && d.Codec is null or "avc" or "h264") ?? neu.FirstOrDefault();
                _serverBereit = true;
                // Häufige Hub-Ereignisse ändern meist nichts an der Liste; dann wird sie gar nicht angefasst.
                bool geaendert = neu.Count != _drohnen.Items.Count || neu.Where((d, i) => !Equals(_drohnen.Items[i], d)).Any();
                if (geaendert || !Equals(_drohnen.SelectedItem, gewaehlt))
                {
                    _listeWirdGesetzt = true;
                    int oben = _drohnen.TopIndex;
                    if (geaendert) _drohnen.BeginUpdate();
                    try
                    {
                        // Nur geänderte Einträge ersetzen, damit die Liste nicht springt.
                        for (int i = 0; i < neu.Count; i++)
                        {
                            if (i >= _drohnen.Items.Count) _drohnen.Items.Add(neu[i]);
                            else if (!Equals(_drohnen.Items[i], neu[i])) _drohnen.Items[i] = neu[i];
                        }
                        while (_drohnen.Items.Count > neu.Count) _drohnen.Items.RemoveAt(_drohnen.Items.Count - 1);
                        if (!Equals(_drohnen.SelectedItem, gewaehlt)) _drohnen.SelectedItem = gewaehlt;
                        if (oben >= 0 && oben < _drohnen.Items.Count && _drohnen.TopIndex != oben) _drohnen.TopIndex = oben;
                    }
                    finally
                    {
                        if (geaendert) _drohnen.EndUpdate();
                        _listeWirdGesetzt = false;
                    }
                }
                if (!_sitzungAktiv) ZeigeAuswahlhinweis();
                SynchronisiereBedienung();
            });
            Log($"Drohnenliste aktualisiert: {liste.Count} Einträge.");
        }
        finally { _ladenSperre.Release(); }
    }

    private async Task StarteAsync()
    {
        if (!KannStarten) return;
        _startAufgabe = StarteSitzungAsync();
        try { await _startAufgabe; } finally { _startAufgabe = null; }
    }

    private async Task StarteSitzungAsync()
    {
        _beschaeftigt = true;
        _sitzungAktiv = true;
        SynchronisiereBedienung();
        try
        {
            var drohne = (Drohne)_drohnen.SelectedItem!;
            string ffmpeg = _konfiguration.FfmpegProgramm();
            _laufendeDrohnenId = drohne.Id;
            SetzeAufloesung();
            SpeichereEinstellungen();
            SetzeStatus("Kein Signal", "Die Übertragung wird gestartet …");
            // Normalerweise besteht die Kamera bereits seit Programmstart; neu erstellt wird sie nur nach einem Fehler.
            await StelleKameraBereitAsync();
            _lebenszeit.Token.ThrowIfCancellationRequested();
            var empfaenger = new StreamReceiver(_hub.StreamAdresse(drohne.Id), ffmpeg, _breite, _hoehe, _konfiguration.Hardwaredekodierung,
                bild =>
                {
                    _kamera?.Schreibe(bild);
                    Interlocked.Exchange(ref _bildzeit, Environment.TickCount64);
                    Interlocked.Exchange(ref _letztesBild, bild);
                }, Status, Log);
            _streamAbbruch = CancellationTokenSource.CreateLinkedTokenSource(_lebenszeit.Token);
            _streamAufgabe = empfaenger.StarteAsync(_streamAbbruch.Token);
            Log($"Stream gestartet: {drohne.Name}, Ausgabe {_breite} × {_hoehe}." + (_konfiguration.VirtuelleKamera ? " Virtuelle Kamera aktiv." : " Nur Vorschau laut Konfiguration."));
            _ = BeobachteStreamendeAsync(_streamAufgabe, _streamAbbruch.Token);
        }
        catch (OperationCanceledException) when (_lebenszeit.IsCancellationRequested) { await StoppeSitzungAsync(false); }
        catch (Exception ex)
        {
            await StoppeSitzungAsync(false);
            Status("Fehler", Startfehlertext(ex));
            Log(ex.ToString());
        }
        finally { _beschaeftigt = false; if (!_schliesst) SynchronisiereBedienung(); }
    }

    // InvalidDataException erbt nicht von IOException, trägt aber die Installationshinweise der Kamera.
    internal static string Startfehlertext(Exception ex,
        string allgemein = "Die Übertragung konnte nicht gestartet werden. Bitte Kamera-Installation und Konfiguration prüfen lassen. Details stehen unter „Diagnose“.") =>
        ex is IOException or InvalidDataException ? ex.Message : allgemein;

    /// <summary>Stellt die Kamera in der gewählten Größe bereit, sofern sie nicht bereits so besteht.</summary>
    private async Task StelleKameraBereitAsync()
    {
        if (!_konfiguration.VirtuelleKamera || _kamera != null && _kamera.Breite == _breite && _kamera.Hoehe == _hoehe) return;
        // Eine andere Ausgabegröße erfordert eine neue Kamerasitzung; nur in diesem Fall verliert Teams die Kamera.
        await EntferneKameraAsync();
        int breite = _breite, hoehe = _hoehe;
        try { _kamera = await Task.Run(() => _kameraErstellen(breite, hoehe)); }
        catch (Exception ex)
        {
            _kameraFehler = Startfehlertext(ex, "Die virtuelle Kamera konnte nicht bereitgestellt werden. Bitte Kamera-Installation prüfen lassen. Details stehen unter „Diagnose“.");
            throw;
        }
        _kameraFehler = null;
        if (_kamera.EntfernteBilddateien > 0) Log($"Verwaiste Bilddateien früherer Sitzungen entfernt: {_kamera.EntfernteBilddateien}.");
        Log($"Virtuelle Kamera bereitgestellt: {breite} × {hoehe}.");
    }

    private async Task EntferneKameraAsync()
    {
        if (_kamera == null) return;
        var kamera = _kamera;
        _kamera = null;
        await Task.Run(kamera.Dispose);
        Log("Virtuelle Kamera entfernt.");
    }

    /// <summary>Bereitstellung beim Programmstart und nach geänderter Bildqualität; Fehler werden angezeigt statt weitergegeben.</summary>
    private async Task AktualisiereKameraAsync()
    {
        if (!_konfiguration.VirtuelleKamera || _beschaeftigt || _sitzungAktiv || _schliesst) return;
        _beschaeftigt = true;
        SetzeStatus("Kein Signal", "Die virtuelle Kamera wird bereitgestellt …");
        try
        {
            _kameraAufgabe = StelleKameraBereitAsync();
            await _kameraAufgabe;
        }
        catch (Exception ex) { Log(ex.ToString()); }
        finally
        {
            _kameraAufgabe = null;
            _beschaeftigt = false;
            if (!_schliesst)
            {
                if (_kameraFehler != null || _serverBereit) ZeigeAuswahlhinweis();
                else SetzeStatus("Kein Signal", "Die virtuelle Kamera ist bereit. Die Drohnenliste kann mit „Aktualisieren“ geladen werden.");
            }
        }
    }

    private async Task BeobachteStreamendeAsync(Task aufgabe, CancellationToken token)
    {
        await Task.Yield();
        try { await aufgabe; }
        catch (Exception ex) { Log(ex.ToString()); Status("Fehler", "Die Übertragung wurde wegen eines Fehlers beendet. Details stehen unter „Diagnose“."); }
        if (token.IsCancellationRequested || _schliesst || _streamAufgabe != aufgabe) return;
        if (_startAufgabe != null) await _startAufgabe;
        if (!token.IsCancellationRequested && !_schliesst) await StoppeAsync(false);
    }

    private async Task StoppeAsync(bool zeigeStatus = true)
    {
        if (_stopAufgabe != null) { await _stopAufgabe; return; }
        if (_beschaeftigt && !_schliesst) return;
        _beschaeftigt = true;
        SynchronisiereBedienung();
        _stopAufgabe = StoppeSitzungAsync(zeigeStatus);
        try { await _stopAufgabe; }
        finally { _stopAufgabe = null; _beschaeftigt = false; if (!_schliesst) SynchronisiereBedienung(); }
    }

    private async Task StoppeSitzungAsync(bool zeigeStatus)
    {
        if (_streamAbbruch != null) await _streamAbbruch.CancelAsync();
        if (_streamAufgabe != null) { try { await _streamAufgabe; } catch (Exception ex) { Log(ex.Message); } }
        _streamAbbruch?.Dispose(); _streamAbbruch = null; _streamAufgabe = null;
        // Die Kamera bleibt für Teams bestehen und zeigt bis zum nächsten Start „Kein Signal“.
        _kamera?.KeinSignal();
        _sitzungAktiv = false;
        _laufendeDrohnenId = null;
        Interlocked.Exchange(ref _letztesBild, null);
        if (!_schliesst && zeigeStatus) SetzeStatus("Kein Signal", _kamera != null
            ? "Übertragung gestoppt. Die Videokonferenz zeigt „Kein Signal“, bis Sie eine Drohne auswählen und erneut starten."
            : "Übertragung gestoppt. Sie können jetzt eine Drohne auswählen und erneut starten.");
    }

    private void AktualisiereVorschau()
    {
        bool frisch = Environment.TickCount64 - Interlocked.Read(ref _bildzeit) <= 2000;
        byte[] bild = (frisch ? Volatile.Read(ref _letztesBild) : null) ?? _keinSignal;
        if (_zustand != "Fehler" && _sitzungAktiv && !_beschaeftigt)
        {
            bool signal = !ReferenceEquals(bild, _keinSignal);
            if (signal && _zustand != "Verbunden") SetzeStatus("Verbunden", "Die Übertragung läuft. Beim Minimieren oder Schließen bleibt sie im Hintergrund aktiv.");
            if (!signal && _zustand == "Verbunden") Status("Kein Signal", "Es kommen keine neuen Bilder. Die Übertragung bleibt aktiv und wartet auf das Signal.");
        }
        if (!_imTray && !ReferenceEquals(bild, _angezeigtesBild))
        {
            var alt = _vorschau.Image;
            _vorschau.Image = Bildwerkzeuge.AlsBitmap(bild, _breite, _hoehe);
            _angezeigtesBild = bild;
            alt?.Dispose();
        }
        bool neueMeldungen = false;
        while (_meldungen.TryDequeue(out var meldung)) { _protokoll.AppendLine(meldung); neueMeldungen = true; }
        if (_protokoll.Length > 60_000) _protokoll.Remove(0, _protokoll.Length - 30_000);
        if (neueMeldungen && _diagnoseText is { IsDisposed: false }) _diagnoseText.Text = _protokoll.ToString();
    }

    private void ZeigeDiagnose()
    {
        if (_diagnose is { IsDisposed: false }) { _diagnose.Show(); _diagnose.Activate(); return; }
        _diagnoseText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = _protokoll.ToString() };
        _diagnose = new Form { Text = Anwendungsname + " · Diagnose", Icon = _symbole.Anwendung, ClientSize = new Size(620, 320), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent };
        _diagnose.Controls.Add(_diagnoseText);
        _diagnose.Show(this);
    }

    private void SpeichereEinstellungen()
    {
        _einstellungen = new((_drohnen.SelectedItem as Drohne)?.Id ?? "", _breite, _hoehe);
        try { _einstellungen.Speichern(_einstellungsDatei); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("Auswahl konnte nicht gespeichert werden: " + ex.Message); }
    }

    private async void SchliesseAsync(object? sender, FormClosingEventArgs e)
    {
        if (_darfSchliessen) return;
        e.Cancel = true;
        if (_schliesst) return;
        bool systemBeendet = e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing;
        if (!systemBeendet && !_beendenBestaetigt)
        {
            if (_sitzungAktiv) { MinimiereInTray(); return; }
            if (!_beendenBestaetigen(false)) return;
        }
        _schliesst = true;
        _tray.Visible = false;
        _timer.Stop();
        Enabled = false;
        try
        {
            SpeichereEinstellungen();
            await _lebenszeit.CancelAsync();
            if (_statusAbbruch != null) await _statusAbbruch.CancelAsync();
            if (_startAufgabe != null) await _startAufgabe;
            if (_stopAufgabe != null) await _stopAufgabe;
            await StoppeSitzungAsync(false);
            if (_kameraAufgabe != null) { try { await _kameraAufgabe; } catch (Exception) { /* Bereits protokolliert. */ } }
            await EntferneKameraAsync();
            if (_ladeAufgabe != null) { try { await _ladeAufgabe; } catch (OperationCanceledException) { } }
            if (_statusAufgabe != null) { try { await _statusAufgabe; } catch (OperationCanceledException) { } }
        }
        catch (Exception ex) { Log("Fehler beim Beenden: " + ex); }
        finally
        {
            _hub.Dispose();
            _statusAbbruch?.Dispose();
            _diagnose?.Dispose();
            _vorschau.Image?.Dispose(); _vorschau.Image = null;
            _lebenszeit.Dispose();
            _darfSchliessen = true;
            Close();
        }
    }
}
