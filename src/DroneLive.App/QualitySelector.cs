namespace DroneLive.App;

/// <summary>Zwei nebeneinanderliegende Schaltflächen für HD und Full HD.</summary>
internal sealed class QualitySelector : UserControl
{
    private readonly RoundButton[] _optionen;
    private int _auswahl = -1;
    public event EventHandler? SelectedIndexChanged;

    public QualitySelector()
    {
        BackColor = AppTheme.Grund;
        var raster = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
        raster.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        raster.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        raster.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _optionen =
        [
            new RoundButton { Name = "QualitaetHd", Text = "HD", Untertitel = "1280 × 720", AccessibleName = "HD, 1280 × 720", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), Font = AppTheme.Bedienung },
            new RoundButton { Name = "QualitaetFullHd", Text = "Full HD", Untertitel = "1920 × 1080", AccessibleName = "Full HD, 1920 × 1080", Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 0), Font = AppTheme.Bedienung }
        ];
        for (int i = 0; i < _optionen.Length; i++)
        {
            int index = i;
            _optionen[i].Click += (_, _) => SelectedIndex = index;
            raster.Controls.Add(_optionen[i], i, 0);
        }
        Controls.Add(raster);
        SelectedIndex = 0;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _auswahl;
        set
        {
            if (value < 0 || value >= _optionen.Length) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _auswahl) return;
            _auswahl = value;
            Gestalte();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnEnabledChanged(EventArgs e) { Gestalte(); base.OnEnabledChanged(e); }

    private void Gestalte()
    {
        for (int i = 0; i < _optionen.Length; i++)
        {
            var option = _optionen[i];
            bool gewaehlt = i == _auswahl;
            option.AccessibleDescription = gewaehlt ? "ausgewählt" : null;
            // Die gewählte Qualität bleibt auch gesperrt farbig, damit sie während der Übertragung erkennbar ist.
            option.Hintergrund = gewaehlt ? AppTheme.AkzentFlaeche : Enabled ? AppTheme.Flaeche : AppTheme.Gesperrt;
            option.HoverHintergrund = gewaehlt ? AppTheme.AkzentHover : AppTheme.Grund;
            option.Rand = gewaehlt ? AppTheme.AkzentFlaeche : Enabled ? AppTheme.LinieStark : AppTheme.Linie;
            option.Schriftfarbe = gewaehlt ? Color.White : Enabled ? AppTheme.Text : AppTheme.Leise;
        }
    }
}
