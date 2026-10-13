# Herkunft und Anpassungen

Media-Foundation-Quelle aus [smourier/VCamNetSample](https://github.com/smourier/VCamNetSample), MIT-Lizenz (siehe LICENSE).
Übernommener Stand: `5f2e41c8173ffdd62a964d0231aaa782577951c2`.

Die bestehende COM-Aktivierung, Media-Source, Stream-Deskriptoren und Ereignisbehandlung werden weiterverwendet.
Die Beispielgrafik wurde durch einen BGRA-Dateispeicher mit drei getrennten Bildplätzen,
atomarer Veröffentlichung, Frischeprüfung und „Kein Signal“ ersetzt (Protokollversion 2).
Speicherblockkopien und eine monotone Zeitprüfung verhindern vermeidbare Kopierkonflikte
und Ausfälle durch Änderungen der Systemuhr. Ein Leser behält kurz das letzte konsistente Bild;
Fristablauf und ausdrücklicher Signalverlust gelten auch für dieses Bild.
Die Quelle erhält Pfad und feste Ausgabegröße über benutzerdefinierte IMFVirtualCamera-Attribute.
Die CLSID wurde geändert, damit sie nicht mit dem Originalbeispiel kollidiert.
RGB32 und NV12 werden im Hauptspeicher erzeugt; D3D-Allocatoren werden deshalb nicht aktiviert.
Sample-Anforderungen werden asynchron mit etwa 30 Bildern/s bedient.
Originalbezeichner und Originalkommentare in unveränderten Teilen bleiben zur Nachvollziehbarkeit erhalten.

DirectNCore 1.18.0 ist die Interop-Abhängigkeit des Beispiels. Die Anwendung nutzt FFmpeg als separates Programm;
das Installationspaket liefert eine FFmpeg-Binärdatei mit eigenen Lizenzhinweisen mit (siehe `licenses/FFmpeg.txt` im Paket).
