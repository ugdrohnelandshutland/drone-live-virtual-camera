#:project ../src/DroneLive.Core/DroneLive.Core.csproj
#:property TargetFramework=net10.0-windows
#:property UseWindowsForms=true
#:property PublishAot=false

// Erzeugt Resources/Application.ico aus derselben Zeichnung, die auch Tray, Taskleiste und Kein-Signal-Bild verwenden.
// Aufruf: dotnet run scripts/GenerateIcons.cs -- [Zielpfad]
using DroneLive.Core;

string ziel = args.Length > 0 ? args[0] : Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "../src/DroneLive.App/Resources/Application.ico");
ziel = Path.GetFullPath(ziel);
Directory.CreateDirectory(Path.GetDirectoryName(ziel)!);
File.WriteAllBytes(ziel, BrandGraphics.ErzeugeIco([16, 20, 24, 32, 40, 48, 64, 128, 256]));
Console.WriteLine($"Anwendungssymbol erzeugt: {ziel}");
