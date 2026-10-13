using System.ComponentModel;
using System.Drawing.Imaging;
using System.Reflection;
using DroneLive.Core;
using DroneLive.App;

internal static class MainFormTests
{
    public static Task<IReadOnlyList<string>> PruefeAsync(string server, string ffmpeg, string ausgabe, Func<long> gesendeteTags)
    {
        var fertig = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var ergebnisse = new List<string>();
            Exception? fehler = null;
            bool erlauben = false;
            var rueckfragen = new List<bool>();
            // Testkameras ersetzen die COM-Kamera; geprüft wird ihre Lebensdauer in der Oberfläche.
            var kameras = new List<FakeCameraOutput>();
            try
            {
                ApplicationConfiguration.Initialize();
                using var formular = new MainForm(new AppConfiguration(server, ffmpeg, false, true), new UserSettings(),
                    aktiv => { rueckfragen.Add(aktiv); return erlauben; }, einstellungsDatei: Path.Combine(ausgabe, "ui-settings.json"),
                    kameraErstellen: (breite, hoehe) => { var kamera = new FakeCameraOutput(breite, hoehe); lock (kameras) kameras.Add(kamera); return kamera; });
                FakeCameraOutput[] Kameras() { lock (kameras) return [.. kameras]; }
                T Control<T>(string name) where T : Control => (T)formular.Controls.Find(name, true).Single();
                // Eine Schaltfläche startet ohne Übertragung und stoppt eine laufende.
                var umschalter = Control<Button>("Uebertragung");
                var drohnen = Control<ListBox>("Drohnenauswahl");
                var qualitaet = Control<QualitySelector>("Ausgabeaufloesung");
                bool KannStarten() => umschalter.Enabled && umschalter.Text == "Übertragung starten";
                bool KannStoppen() => umschalter.Enabled && umschalter.Text == "Übertragung stoppen";
                var container = (IContainer)typeof(MainForm).GetField("components", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(formular)!;
                var tray = container.Components.OfType<NotifyIcon>().Single();
                ToolStripMenuItem Menue(string text) => tray.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == text);
                void Pruefe(bool bedingung, string text)
                {
                    if (!bedingung) throw new InvalidOperationException(text);
                    ergebnisse.Add(text);
                }
                async Task Warte(Func<bool> bedingung)
                {
                    var zeit = System.Diagnostics.Stopwatch.StartNew();
                    while (!bedingung())
                    {
                        if (zeit.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Oberflächenzustand wurde nicht erreicht.");
                        await Task.Delay(30);
                    }
                }
                async Task StoppeUeberOberflaeche()
                {
                    umschalter.PerformClick();
                    await Warte(KannStarten);
                }
                void Bild(string name)
                {
                    using var bild = new Bitmap(formular.Width, formular.Height);
                    formular.DrawToBitmap(bild, new Rectangle(Point.Empty, bild.Size));
                    bild.Save(Path.Combine(ausgabe, name), ImageFormat.Png);
                }
                formular.Shown += async (_, _) =>
                {
                    var startsymbol = formular.Icon;
                    try
                    {
                        Pruefe(formular.Text == "DroneLive2VirtualCamera" && formular.FormBorderStyle == FormBorderStyle.FixedSingle && !formular.MaximizeBox &&
                            formular.ClientSize.Width <= formular.LogicalToDeviceUnits(800) && formular.ClientSize.Height <= formular.LogicalToDeviceUnits(570),
                            "Kompaktes Fenster mit festem Rahmen, korrektem Namen und gesperrtem Maximieren");
                        var adresse = Control<TextBox>("Serveradresse");
                        Pruefe(adresse.ReadOnly && adresse.Text == MainForm.Servername(new Uri(server)) &&!formular.Controls.OfType<TextBox>().Any(t => t.Text == ffmpeg), "Serveradresse nur lesbar; kein FFmpeg-Pfad in der Hauptansicht");
                        Pruefe(!KannStarten(), "Start bleibt bis zur erfolgreichen Serverabfrage gesperrt");
                        string bauart = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Name;
                        string exe = Path.GetFullPath(Path.Combine("src", "DroneLive.App", "bin", bauart, "net10.0-windows10.0.22621.0", "win-x64", "DroneLive2VirtualCamera.exe"));
                        using (var exeIcon = Icon.ExtractAssociatedIcon(exe))
                        {
                            Pruefe(exeIcon != null && formular.Icon != null && tray.Icon != null, "Eigenes Icon in EXE, Fenster und Windows-Tray vorhanden");
                            using var exeBild = exeIcon!.ToBitmap();
                            // Links neben dem Schild liegt die dunkle Grundfläche des neuen Symbols.
                            var farbe = exeBild.GetPixel(exeBild.Width / 8, exeBild.Height / 2);
                            Pruefe(Math.Abs(farbe.R - BrandGraphics.Grund.R) < 10 && Math.Abs(farbe.G - BrandGraphics.Grund.G) < 10 && Math.Abs(farbe.B - BrandGraphics.Grund.B) < 10,
                                "EXE enthält das neue Schildsymbol statt des Standardsymbols");
                            using var iconBild = formular.Icon!.ToBitmap();
                            iconBild.Save(Path.Combine(ausgabe, "application-icon.png"), ImageFormat.Png);
                        }
                        await Warte(KannStarten);
                        Pruefe(drohnen.Height <= DroneListBox.SichtbareZeilen * drohnen.ItemHeight, "Drohnenliste zeigt höchstens vier Einträge, weitere per Scrollen");
                        // Der Testserver meldet etwa alle zwei Sekunden „drone-updated“; unveränderte Einträge bleiben dieselben Objekte.
                        var ersterEintrag = drohnen.Items[0];
                        await Task.Delay(2500);
                        Pruefe(ReferenceEquals(drohnen.Items[0], ersterEintrag) && drohnen.SelectedItem is Drohne { Id: "test" },
                            "Unverändertes Hub-Ereignis lässt Drohnenliste und Auswahl unberührt (kein Flackern)");
                        var ersteKamera = Kameras().Single();
                        Pruefe(ersteKamera is { Breite: 1280, Hoehe: 720, Entfernt: false, Bilder: 0 }, "Virtuelle Kamera wird beim Programmstart ohne laufende Übertragung bereitgestellt");
                        drohnen.SelectedItem = drohnen.Items.Cast<Drohne>().Single(d => d.Id == "offline");
                        Pruefe(!KannStarten(), "Offline-Drohne kann nicht gestartet werden");
                        drohnen.SelectedItem = drohnen.Items.Cast<Drohne>().Single(d => d.Id == "h265");
                        Pruefe(!KannStarten(), "Nicht unterstütztes Videoformat kann nicht gestartet werden");
                        drohnen.SelectedItem = drohnen.Items.Cast<Drohne>().Single(d => d.Id == "test");
                        Bild("ui.png");
                        formular.Close();
                        Pruefe(!formular.IsDisposed && rueckfragen.SequenceEqual(new[] { false }), "Normales Schließen ohne Stream fragt nach; Abbrechen hält das Fenster offen");
                        umschalter.PerformClick();
                        await Warte(() => KannStoppen() && Control<Label>("Verbindungsstatus").Text == "Verbunden");
                        Pruefe(!drohnen.Enabled && !qualitaet.Enabled && !Control<Button>("Aktualisieren").Enabled, "Drohne, Qualität, Aktualisieren und Mehrfachstart während des Streams gesperrt");
                        Pruefe(tray.Text.Contains("Verbunden") && !ReferenceEquals(formular.Icon, startsymbol), "Tray und Taskleiste zeigen den Übertragungsstatus");
                        Pruefe(Kameras().Length == 1 && ersteKamera.Bilder > 0, "Die Übertragung schreibt in die bereits beim Start bereitgestellte Kamera");
                        Bild("ui-live.png");
                        formular.WindowState = FormWindowState.Minimized;
                        Pruefe(!formular.Visible && tray.Visible && !formular.ShowInTaskbar, "Minimieren versteckt das Fenster und zeigt das Tray-Icon");
                        Menue("Fenster öffnen").PerformClick();
                        Pruefe(formular.Visible && !tray.Visible && formular.WindowState == FormWindowState.Normal, "Tray-Menü stellt das Fenster wieder her");
                        long vorher = gesendeteTags();
                        formular.Close();
                        await Task.Delay(400);
                        Pruefe(!formular.Visible && tray.Visible && rueckfragen.Count == 1 && gesendeteTags() > vorher && KannStoppen(), "Schließen mit echtem Stream geht ohne Rückfrage in den Tray; der Empfang läuft weiter");
                        Pruefe(Menue("Fenster öffnen").Image != null && Menue("Übertragung stoppen").Image != null && Menue("Beenden …").Image != null, "Tray-Menüeinträge besitzen passende Icons");
                        Menue("Beenden …").PerformClick();
                        Pruefe(!formular.IsDisposed && tray.Visible && rueckfragen[^1], "Beenden aus dem Tray warnt vor dem Stoppen des Streams; Abbrechen erhält die Sitzung");
                        Menue("Übertragung stoppen").PerformClick();
                        await Warte(KannStarten);
                        Pruefe(!KannStoppen() && drohnen.Enabled && qualitaet.Enabled && !Menue("Übertragung stoppen").Enabled, "Tray-Stopp beendet den Stream und gibt die Auswahl wieder frei");
                        Pruefe(ReferenceEquals(formular.Icon, startsymbol), "Nach dem Stopp zeigt die Taskleiste wieder das Anwendungssymbol ohne Statuspunkt");
                        Pruefe(Kameras().Length == 1 && !ersteKamera.Entfernt && ersteKamera.KeinSignalMeldungen > 0,
                            "Gestoppte Übertragung behält die Kamera für die Videokonferenz und meldet dort Kein Signal");
                        Menue("Fenster öffnen").PerformClick();
                        // Drohnenwechsel: Stopp und erneuter Start verwenden dieselbe Kamera weiter.
                        int bilderVorWechsel = ersteKamera.Bilder;
                        umschalter.PerformClick();
                        await Warte(() => KannStoppen() && Control<Label>("Verbindungsstatus").Text == "Verbunden");
                        Pruefe(Kameras().Length == 1 && !ersteKamera.Entfernt && ersteKamera.Bilder > bilderVorWechsel,
                            "Neuer Start nach dem Stopp liefert Bilder an dieselbe Kamera, die Videokonferenz verliert sie nicht");
                        await StoppeUeberOberflaeche();
                        qualitaet.SelectedIndex = 1;
                        await Warte(() => Kameras().Length == 2 && KannStarten());
                        var zweiteKamera = Kameras()[1];
                        Pruefe(ersteKamera.Entfernt && zweiteKamera is { Breite: 1920, Hoehe: 1080, Entfernt: false },
                            "Geänderte Bildqualität stellt die Kamera einmalig in der neuen festen Größe bereit");
                        umschalter.PerformClick();
                        await Warte(() => KannStoppen() && Control<Label>("Verbindungsstatus").Text == "Verbunden" && zweiteKamera.Bilder > 0);
                        formular.Close();
                        erlauben = true;
                        Menue("Beenden …").PerformClick();
                    }
                    catch (Exception ex)
                    {
                        fehler = ex;
                        erlauben = true;
                        // Auch beim fehlgeschlagenen Prüfschritt den Empfang geordnet beenden.
                        Menue("Beenden …").PerformClick();
                        if (!formular.IsDisposed && formular.Visible) formular.Close();
                    }
                };
                Application.Run(formular);
                if (fehler != null) throw fehler;
                Pruefe(formular.IsDisposed && rueckfragen[^1], "Bestätigtes Tray-Beenden räumt den aktiven Stream auf und schließt die Anwendung");
                Pruefe(Kameras().Length == 2 && Kameras().All(k => k.Entfernt), "Die virtuelle Kamera wird erst beim Beenden der Anwendung entfernt");
                using var ohneStream = new MainForm(new AppConfiguration(server, ffmpeg, false, false), new UserSettings(), _ => true,
                    automatischLaden: false, einstellungsDatei: Path.Combine(ausgabe, "ui-settings.json"));
                ohneStream.Shown += (_, _) =>
                {
                    // Zusätzliche Layoutprüfung für größere Bildschirmdarstellung.
                    ohneStream.Scale(new SizeF(1.5f, 1.5f));
                    ohneStream.Font = new Font("Segoe UI", 13.5f);
                    ohneStream.PerformLayout();
                    using var bild = new Bitmap(ohneStream.Width, ohneStream.Height);
                    ohneStream.DrawToBitmap(bild, new Rectangle(Point.Empty, bild.Size));
                    bild.Save(Path.Combine(ausgabe, "ui-scaled.png"), ImageFormat.Png);
                    var gross = (Button)ohneStream.Controls.Find("Uebertragung", true).Single();
                    Pruefe(gross.Width > TextRenderer.MeasureText(gross.Text, gross.Font).Width + gross.Image!.Width, "Startbeschriftung passt auch bei größerer Darstellung vollständig in die Schaltfläche");
                    ohneStream.Close();
                };
                Application.Run(ohneStream);
                Pruefe(ohneStream.IsDisposed, "Bestätigtes normales Schließen ohne Stream beendet die Anwendung");
                fertig.TrySetResult(ergebnisse);
            }
            catch (Exception ex) { fertig.TrySetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return fertig.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}

/// <summary>Ersetzt die COM-Kamera in Oberflächenprüfungen und zählt deren Nutzung.</summary>
internal sealed class FakeCameraOutput(int breite, int hoehe) : ICameraOutput
{
    private int _bilder, _keinSignal, _entfernt;
    public int Breite => breite;
    public int Hoehe => hoehe;
    public int EntfernteBilddateien => 0;
    public int Bilder => Volatile.Read(ref _bilder);
    public int KeinSignalMeldungen => Volatile.Read(ref _keinSignal);
    public bool Entfernt => Volatile.Read(ref _entfernt) != 0;
    public void Schreibe(byte[] bild)
    {
        if (Entfernt) throw new ObjectDisposedException(nameof(FakeCameraOutput), "Bild an eine bereits entfernte Kamera geschrieben.");
        if (bild.Length != breite * hoehe * 4) throw new ArgumentException("Bildgröße passt nicht zur festen Kameragröße.");
        Interlocked.Increment(ref _bilder);
    }
    public void KeinSignal() => Interlocked.Increment(ref _keinSignal);
    public void Dispose() => Interlocked.Exchange(ref _entfernt, 1);
}
