using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

public class PatchManifest {
    public string version { get; set; }
    public string steamBuildId { get; set; }
    public int originalSize { get; set; }
    public int patchedSize { get; set; }
    public int patchCount { get; set; }
    public string originalSHA256 { get; set; }
    public string patchedSHA256 { get; set; }
    public string appendSHA256 { get; set; }
    public string tablePatchSHA256 { get; set; }
    public string originalLauncherSHA256 { get; set; }
    public string mainExecutableSHA256 { get; set; }
    public string[] previousPatchSHA256 { get; set; }
}
public class OperationResult {
    public bool success { get; set; }
    public string message { get; set; }
}

public static class PatchEngine {
    public const string MainExe = "CAPTAIN TSUBASA RISE OF NEW CHAMPIONS.exe";
    public const string BackupFolder = "Holly-e-Benji-Nomi-Italia-Backup";
    public static readonly PatchManifest Manifest = new JavaScriptSerializer().Deserialize<PatchManifest>(Encoding.UTF8.GetString(Resource("manifest.json")));
    public static byte[] Resource(string name) {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
            if (s == null) throw new InvalidOperationException("Il pacchetto dell'installatore è incompleto.");
            using (MemoryStream m = new MemoryStream()) { s.CopyTo(m); return m.ToArray(); }
        }
    }
    public static string Hash(byte[] bytes) {
        using (SHA256 sha = SHA256.Create()) { return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
    public static string FileHash(string path) {
        if (!File.Exists(path)) throw new InvalidOperationException("File del gioco non trovato: " + Path.GetFileName(path));
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path)) { return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
    }
    public static void AssertHash(string path, string hash) {
        if (FileHash(path) != hash) throw new InvalidOperationException("Il file " + Path.GetFileName(path) + " non corrisponde alla versione compatibile.");
    }
    public static bool IsPatchedText(string hash) {
        if (hash == Manifest.patchedSHA256) return true;
        return Manifest.previousPatchSHA256 != null && Array.IndexOf(Manifest.previousPatchSHA256, hash) >= 0;
    }
    public static void VerifyBundle() {
        byte[] added = Resource("append-text.bin"), table = Resource("table-patch.bin");
        if (Hash(added) != Manifest.appendSHA256 || Hash(table) != Manifest.tablePatchSHA256 ||
            table.Length != Manifest.patchCount * 20 || Manifest.originalSize + added.Length != Manifest.patchedSize)
            throw new InvalidOperationException("Il pacchetto incorporato non supera i controlli di integrità.");
        HashSet<uint> positions = new HashSet<uint>();
        const uint pool = 13586912;
        using (BinaryReader reader = new BinaryReader(new MemoryStream(table))) {
            for (int i = 0; i < Manifest.patchCount; i++) {
                uint position = reader.ReadUInt32(); reader.ReadUInt32(); reader.ReadUInt32();
                uint offset = reader.ReadUInt32(), length = reader.ReadUInt32();
                long withinAdded = (long)pool + offset - Manifest.originalSize;
                if (position < 655512 || position + 8 > pool || !positions.Add(position) ||
                    withinAdded < 0 || length == 0 || withinAdded + length > added.Length || added[(int)(withinAdded + length - 1)] != 0)
                    throw new InvalidOperationException("I riferimenti dei testi incorporati non sono validi.");
            }
        }
    }
    public static string NormalizeRoot(string selection) {
        if (String.IsNullOrWhiteSpace(selection)) throw new InvalidOperationException("Seleziona la cartella del gioco.");
        string p = Path.GetFullPath(selection.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (File.Exists(Path.Combine(p, MainExe))) return p;
        string child = Path.Combine(p, "GameData");
        if (File.Exists(Path.Combine(child, MainExe))) return child;
        throw new InvalidOperationException("Questa cartella non contiene Captain Tsubasa: Rise of New Champions. Seleziona la cartella del gioco o GameData.");
    }
    public static List<string> LibraryPaths(string vdf) {
        List<string> result = new List<string>();
        foreach (Match m in Regex.Matches(vdf, "\\\"path\\\"\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"", RegexOptions.IgnoreCase)) {
            string p = m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
            if (Path.IsPathRooted(p)) result.Add(p);
        }
        // Also accept the older flat libraryfolders.vdf layout.
        foreach (Match m in Regex.Matches(vdf, "\\\"[0-9]+\\\"\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"")) {
            string p = m.Groups[1].Value.Replace("\\\\", "\\");
            if (Path.IsPathRooted(p)) result.Add(p);
        }
        return result;
    }
    private static void AddPath(HashSet<string> paths, string p) {
        if (String.IsNullOrWhiteSpace(p)) return;
        try { paths.Add(Path.GetFullPath(p)); } catch { }
    }
    public static List<string> FindGames() {
        HashSet<string> steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) {
            foreach (RegistryView view in new RegistryView[] { RegistryView.Registry32, RegistryView.Registry64 }) {
                try {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey k = baseKey.OpenSubKey(@"Software\Valve\Steam")) {
                        if (k != null) { AddPath(steamRoots, k.GetValue("SteamPath") as string); AddPath(steamRoots, k.GetValue("InstallPath") as string); }
                    }
                } catch { }
            }
        }
        AddPath(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        AddPath(steamRoots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        HashSet<string> libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (string steam in steamRoots) {
            foreach (string relative in new string[] { @"steamapps\libraryfolders.vdf", @"config\libraryfolders.vdf" }) {
                try { string file = Path.Combine(steam, relative); if (File.Exists(file)) foreach (string p in LibraryPaths(File.ReadAllText(file))) AddPath(libraries, p); } catch { }
            }
        }
        HashSet<string> games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string lib in libraries) {
            string steamapps = Path.Combine(lib, "steamapps");
            string manifest = Path.Combine(steamapps, "appmanifest_1163550.acf");
            try {
                if (File.Exists(manifest)) {
                    Match m = Regex.Match(File.ReadAllText(manifest), "\\\"installdir\\\"\\s*\\\"([^\\\"]+)\\\"");
                    if (m.Success) {
                        string p = Path.Combine(steamapps, "common", m.Groups[1].Value, "GameData");
                        if (File.Exists(Path.Combine(p, MainExe))) AddPath(games, p);
                    }
                }
                string defaultGame = Path.Combine(steamapps, "common", "Captain Tsubasa Rise of New Champions", "GameData");
                if (File.Exists(Path.Combine(defaultGame, MainExe))) AddPath(games, defaultGame);
            } catch { }
        }
        return new List<string>(games);
    }
    public static string Check(string root) {
        root = NormalizeRoot(root);
        AssertHash(Path.Combine(root, MainExe), Manifest.mainExecutableSHA256);
        string loader = FileHash(Path.Combine(root, "rename.exe"));
        string text = FileHash(Path.Combine(root, @"string\WW\text.ttb"));
        if (loader != Manifest.originalLauncherSHA256 && loader != Manifest.mainExecutableSHA256)
            throw new InvalidOperationException("Il programma di avvio è diverso da quello compatibile: potrebbe esserci un'altra mod o un aggiornamento. Nessun file è stato modificato.");
        if (text != Manifest.originalSHA256 && !IsPatchedText(text))
            throw new InvalidOperationException("I testi appartengono a una versione diversa o a un'altra mod. Questo installatore è per la build Steam " + Manifest.steamBuildId + ". Nessun file è stato modificato.");
        if (loader == Manifest.originalLauncherSHA256 && text == Manifest.originalSHA256) return "Gioco compatibile: file originali presenti. Puoi installare la patch offline.";
        if (loader == Manifest.mainExecutableSHA256 && text == Manifest.patchedSHA256) return "Patch con nomi italiani offline già attiva. Metti Steam offline prima di giocare.";
        if (loader == Manifest.mainExecutableSHA256 && IsPatchedText(text)) return "Versione precedente della patch presente. Puoi aggiornarla con «Installa per l'offline».";
        return "La modalità è incompleta. Ripristina gli originali prima di avviare normalmente il gioco.";
    }
    public static byte[] BuildText(byte[] original) {
        if (original.Length != Manifest.originalSize || Hash(original) != Manifest.originalSHA256) throw new InvalidOperationException("La copia dei testi originali non supera la verifica.");
        byte[] added = Resource("append-text.bin"), table = Resource("table-patch.bin");
        if (Hash(added) != Manifest.appendSHA256 || Hash(table) != Manifest.tablePatchSHA256 || table.Length != Manifest.patchCount * 20)
            throw new InvalidOperationException("I dati della patch non superano il controllo di integrità.");
        byte[] candidate = new byte[Manifest.patchedSize];
        if (original.Length + added.Length != candidate.Length) throw new InvalidOperationException("Dimensione della patch non valida.");
        Buffer.BlockCopy(original, 0, candidate, 0, original.Length);
        Buffer.BlockCopy(added, 0, candidate, original.Length, added.Length);
        uint pool = BitConverter.ToUInt32(original, 36);
        using (BinaryReader r = new BinaryReader(new MemoryStream(table))) {
            for (int i = 0; i < Manifest.patchCount; i++) {
                uint position = r.ReadUInt32(), oldOffset = r.ReadUInt32(), oldLength = r.ReadUInt32(), newOffset = r.ReadUInt32(), newLength = r.ReadUInt32();
                if ((long)position + 8 > pool || BitConverter.ToUInt32(original, (int)position) != oldOffset || BitConverter.ToUInt32(original, (int)position + 4) != oldLength ||
                    (long)pool + newOffset + newLength > candidate.Length || newLength == 0 || candidate[(int)((long)pool + newOffset + newLength - 1)] != 0)
                    throw new InvalidOperationException("Una voce della patch non corrisponde al file originale.");
                Buffer.BlockCopy(BitConverter.GetBytes(newOffset), 0, candidate, (int)position, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(newLength), 0, candidate, (int)position + 4, 4);
            }
        }
        if (Hash(candidate) != Manifest.patchedSHA256) throw new InvalidOperationException("La patch ricostruita non supera il controllo di integrità.");
        return candidate;
    }
    private static void GameClosed(string root) {
        foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(MainExe))) {
            using (p) {
                try { if (String.Equals(p.MainModule.FileName, Path.Combine(root, MainExe), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Chiudi Captain Tsubasa prima di installare o ripristinare la patch."); }
                catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Non posso verificare un processo di Captain Tsubasa. Chiudi il gioco e riprova."); }
            }
        }
        foreach (Process p in Process.GetProcessesByName("rename")) {
            using (p) {
                try { if (String.Equals(p.MainModule.FileName, Path.Combine(root, "rename.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Chiudi l'avvio di Captain Tsubasa prima di continuare."); }
                catch (System.ComponentModel.Win32Exception) { throw new InvalidOperationException("È presente un processo di avvio che non posso verificare. Chiudi il gioco e riprova."); }
            }
        }
    }
    private static void EnsureBackup(string source, string backup, string expected) {
        if (File.Exists(backup)) { AssertHash(backup, expected); return; }
        if (FileHash(source) != expected) throw new InvalidOperationException("La patch è già presente, ma manca la copia originale salvata da questo installatore. Ripristina prima con il pacchetto usato per installarla.");
        File.Copy(source, backup, false);
        AssertHash(backup, expected);
    }
    private static bool HasOriginalBackup(string directory) {
        try {
            string text = Path.Combine(directory, "text.ttb"), loader = Path.Combine(directory, "rename.exe");
            return File.Exists(text) && File.Exists(loader) && new FileInfo(text).Length == Manifest.originalSize &&
                FileHash(text) == Manifest.originalSHA256 && FileHash(loader) == Manifest.originalLauncherSHA256;
        } catch { return false; }
    }
    private static string FindOriginalBackup(string gameRoot) {
        List<string> roots = new List<string> { gameRoot, Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) };
        foreach (string root in roots) {
            if (HasOriginalBackup(root)) return root;
            try {
                foreach (string directory in Directory.GetDirectories(root)) {
                    if (HasOriginalBackup(directory)) return directory;
                    string nested = Path.Combine(directory, "backup-originale");
                    if (HasOriginalBackup(nested)) return nested;
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return null;
    }
    private static void PrepareBackups(string root, string text, string loader, string backupRoot) {
        string backupText = Path.Combine(backupRoot, "text.ttb"), backupLoader = Path.Combine(backupRoot, "rename.exe");
        bool needText = !File.Exists(backupText) && FileHash(text) != Manifest.originalSHA256;
        bool needLoader = !File.Exists(backupLoader) && FileHash(loader) != Manifest.originalLauncherSHA256;
        string previous = needText || needLoader ? FindOriginalBackup(root) : null;
        if ((needText || needLoader) && previous == null)
            throw new InvalidOperationException("Versione precedente già presente, ma non ho trovato entrambe le copie originali. Ripristina prima con il pacchetto usato per installarla.");
        Directory.CreateDirectory(backupRoot);
        if (needText) File.Copy(Path.Combine(previous, "text.ttb"), backupText, false);
        if (needLoader) File.Copy(Path.Combine(previous, "rename.exe"), backupLoader, false);
        EnsureBackup(text, backupText, Manifest.originalSHA256);
        EnsureBackup(loader, backupLoader, Manifest.originalLauncherSHA256);
    }
    private static void AtomicWrite(string destination, byte[] data, string expected) {
        if (Hash(data) != expected) throw new InvalidOperationException("Dati da installare non validi.");
        string pending = Path.Combine(Path.GetDirectoryName(destination), ".holly-benji-" + Guid.NewGuid().ToString("N") + ".tmp");
        try {
            File.WriteAllBytes(pending, data); AssertHash(pending, expected);
            File.Replace(pending, destination, null); AssertHash(destination, expected);
        } finally { if (File.Exists(pending)) File.Delete(pending); }
    }
    public static OperationResult Apply(string action, string root) {
        root = NormalizeRoot(root); Check(root); GameClosed(root);
        if (action != "install" && action != "restore") throw new InvalidOperationException("Operazione non riconosciuta.");
        string text = Path.Combine(root, @"string\WW\text.ttb"), loader = Path.Combine(root, "rename.exe");
        string backupRoot = Path.Combine(root, BackupFolder), backupText = Path.Combine(backupRoot, "text.ttb"), backupLoader = Path.Combine(backupRoot, "rename.exe");
        string previousTextHash = FileHash(text), previousLoaderHash = FileHash(loader);
        if (action == "restore" && previousTextHash == Manifest.originalSHA256 && previousLoaderHash == Manifest.originalLauncherSHA256)
            return new OperationResult { success = true, message = "Il gioco contiene già i testi e l'avvio originali." };
        // Both originals must be available before either installed file changes.
        PrepareBackups(root, text, loader, backupRoot);
        AssertHash(backupText, Manifest.originalSHA256); AssertHash(backupLoader, Manifest.originalLauncherSHA256);
        byte[] targetText = action == "install" ? BuildText(File.ReadAllBytes(backupText)) : File.ReadAllBytes(backupText);
        byte[] targetLoader = File.ReadAllBytes(action == "install" ? Path.Combine(root, MainExe) : backupLoader);
        string targetTextHash = action == "install" ? Manifest.patchedSHA256 : Manifest.originalSHA256;
        string targetLoaderHash = action == "install" ? Manifest.mainExecutableSHA256 : Manifest.originalLauncherSHA256;
        byte[] previousText = File.ReadAllBytes(text), previousLoader = File.ReadAllBytes(loader);
        try {
            // Recheck after preparing the patch and backups, before making changes.
            GameClosed(root); AssertHash(text, previousTextHash); AssertHash(loader, previousLoaderHash);
            if (action == "restore") {
                if (previousLoaderHash != targetLoaderHash) AtomicWrite(loader, targetLoader, targetLoaderHash);
                if (previousTextHash != targetTextHash) AtomicWrite(text, targetText, targetTextHash);
            } else {
                if (previousTextHash != targetTextHash) AtomicWrite(text, targetText, targetTextHash);
                if (previousLoaderHash != targetLoaderHash) AtomicWrite(loader, targetLoader, targetLoaderHash);
            }
        } catch (Exception error) {
            try {
                if (FileHash(text) != previousTextHash) AtomicWrite(text, previousText, previousTextHash);
                if (FileHash(loader) != previousLoaderHash) AtomicWrite(loader, previousLoader, previousLoaderHash);
            } catch (Exception rollbackError) { throw new IOException("Operazione interrotta. Le copie originali sono in " + backupRoot + ". Errore: " + error.Message + " Ripristino automatico: " + rollbackError.Message); }
            throw;
        }
        return new OperationResult { success = true, message = action == "install"
            ? "Patch installata per l'offline. Metti Steam in modalità offline e avvia il gioco in italiano.\n\nOriginali salvati in:\n" + backupRoot
            : "Testi e avvio Easy Anti-Cheat originali ripristinati. Puoi usare la modalità standard." };
    }
}

public class InstallerWindow : Form {
    private TextBox pathBox;
    private Label status;
    private CheckBox offline;
    private Button install, restore, check, browse;
    public InstallerWindow() {
        Text = "Holly e Benji · Installatore nomi italiani";
        ClientSize = new Size(820, 515); MinimumSize = new Size(836, 554);
        Font = new Font("Segoe UI", 10F); BackColor = Color.White;
        StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
        Label title = new Label { Text = "Holly e Benji", Font = new Font("Segoe UI", 24F, FontStyle.Bold), ForeColor = Color.FromArgb(23, 64, 84), AutoSize = true, Location = new Point(28, 20) };
        Controls.Add(title);
        Controls.Add(new Label { Text = "Nomi con le versioni del cartone animato in Italia · Versione " + PatchEngine.Manifest.version, AutoSize = true, Location = new Point(31, 79) });
        Controls.Add(new Label { Text = "Captain Tsubasa: Rise of New Champions — Steam per Windows", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(31, 106) });
        Controls.Add(new Label { Text = "Cartella del gioco", AutoSize = true, Location = new Point(31, 155) });
        pathBox = new TextBox { Location = new Point(31, 181), Size = new Size(617, 30) };
        browse = new Button { Text = "Sfoglia…", Location = new Point(660, 180), Size = new Size(125, 31) };
        browse.Click += delegate {
            using (FolderBrowserDialog d = new FolderBrowserDialog { Description = "Seleziona la cartella di Captain Tsubasa oppure GameData.", ShowNewFolderButton = false }) {
                if (Directory.Exists(pathBox.Text)) d.SelectedPath = pathBox.Text;
                if (d.ShowDialog(this) == DialogResult.OK) { pathBox.Text = d.SelectedPath; VerifySelection(); }
            }
        };
        Controls.Add(pathBox); Controls.Add(browse);
        offline = new CheckBox { Text = "Userò la patch solo offline, con Steam offline e senza Easy Anti-Cheat.", AutoSize = true, Location = new Point(31, 239) };
        Controls.Add(offline);
        Controls.Add(new Label { Text = "Gli originali vengono salvati. «Ripristina originali» riattiva l'avvio standard.", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(31, 270) });
        install = new Button { Text = "Installa per l'offline", Location = new Point(31, 317), Size = new Size(238, 45), BackColor = Color.FromArgb(23, 104, 92), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        restore = new Button { Text = "Ripristina originali", Location = new Point(284, 317), Size = new Size(238, 45) };
        check = new Button { Text = "Verifica", Location = new Point(537, 317), Size = new Size(248, 45) };
        install.Click += async delegate { if (!offline.Checked) { MessageBox.Show(this, "Conferma la casella per l'uso esclusivamente offline. La patch avvia il gioco senza Easy Anti-Cheat.", "Uso offline", MessageBoxButtons.OK, MessageBoxIcon.Information); return; } await Run("install"); };
        restore.Click += async delegate { await Run("restore"); };
        check.Click += delegate { VerifySelection(); };
        Controls.Add(install); Controls.Add(restore); Controls.Add(check);
        status = new Label { Location = new Point(31, 389), Size = new Size(754, 85), ForeColor = Color.FromArgb(23, 64, 84), Text = "Ricerca del gioco…" };
        Controls.Add(status);
        Controls.Add(new Label { Text = "Chiudi il gioco prima di installare. L'installatore controlla la versione e non modifica i salvataggi.", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(31, 480), Font = new Font("Segoe UI", 9F) });
        Shown += delegate { RefreshGame(); };
    }
    public void RefreshGame() {
        List<string> games = PatchEngine.FindGames();
        if (games.Count == 1) { pathBox.Text = games[0]; VerifySelection(); }
        else status.Text = games.Count == 0 ? "Gioco non individuato automaticamente. Usa «Sfoglia…» per selezionarlo." : "Sono presenti più copie del gioco. Seleziona la cartella da modificare con «Sfoglia…».";
    }
    private void VerifySelection() {
        try { string root = PatchEngine.NormalizeRoot(pathBox.Text); pathBox.Text = root; status.Text = PatchEngine.Check(root); status.ForeColor = Color.FromArgb(23, 104, 92); }
        catch (Exception e) { status.Text = e.Message; status.ForeColor = Color.FromArgb(154, 50, 45); }
    }
    private static bool IsAdmin() {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) { return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    }
    private static string Quote(string s) { return "\"" + s.Replace("\"", "").TrimEnd('\\') + "\""; }
    private async Task Run(string action) {
        string root;
        try { root = PatchEngine.NormalizeRoot(pathBox.Text); PatchEngine.Check(root); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Controllo della versione", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        install.Enabled = restore.Enabled = check.Enabled = browse.Enabled = pathBox.Enabled = offline.Enabled = false;
        status.Text = action == "install" ? "Controllo, copia di sicurezza e installazione…" : "Controllo e ripristino degli originali…";
        try {
            OperationResult result;
            if (IsAdmin()) result = await Task.Run(() => PatchEngine.Apply(action, root));
            else {
                string resultFile = Path.Combine(Path.GetTempPath(), "holly-benji-result-" + Guid.NewGuid().ToString("N") + ".json");
                try {
                    ProcessStartInfo info = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                        "--worker " + action + " " + Quote(root) + " " + Quote(resultFile)) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                    using (Process worker = Process.Start(info)) { await Task.Run(() => worker.WaitForExit()); }
                    if (!File.Exists(resultFile)) throw new InvalidOperationException("L'operazione non è stata completata.");
                    result = new JavaScriptSerializer().Deserialize<OperationResult>(File.ReadAllText(resultFile, Encoding.UTF8));
                } finally { if (File.Exists(resultFile)) File.Delete(resultFile); }
            }
            status.Text = result.success ? (action == "install" ? "Patch offline installata e verificata. Originali conservati." : "Gioco originale ripristinato e verificato.") : result.message;
            MessageBox.Show(this, result.message, result.success ? "Operazione completata" : "Operazione interrotta", MessageBoxButtons.OK, result.success ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        } catch (System.ComponentModel.Win32Exception e) {
            status.Text = e.NativeErrorCode == 1223 ? "Installazione annullata. Nessun file modificato." : e.Message;
        } catch (Exception e) { status.Text = e.Message; MessageBox.Show(this, e.Message, "Operazione interrotta", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { install.Enabled = restore.Enabled = check.Enabled = browse.Enabled = pathBox.Enabled = offline.Enabled = true; }
    }
}

public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        try {
            if (args.Length == 4 && args[0] == "--worker") {
                string resultFile = Path.GetFullPath(args[3]);
                if (!Regex.IsMatch(Path.GetFileName(resultFile), "^holly-benji-result-[a-f0-9]{32}\\.json$")) throw new InvalidOperationException("Percorso del risultato non valido.");
                OperationResult result;
                try { result = PatchEngine.Apply(args[1], args[2]); }
                catch (Exception e) { result = new OperationResult { success = false, message = e.Message }; }
                File.WriteAllText(resultFile, new JavaScriptSerializer().Serialize(result), Encoding.UTF8);
                return result.success ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "--detect") {
                File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(PatchEngine.FindGames()), Encoding.UTF8); return 0;
            }
            if (args.Length == 2 && args[0] == "--verify-bundle") {
                PatchEngine.VerifyBundle();
                File.WriteAllText(args[1], "Embedded patch integrity and references verified.", Encoding.UTF8);
                return 0;
            }
            if (args.Length == 2 && args[0] == "--verify") { PatchEngine.Check(args[1]); return 0; }
            if ((args.Length == 5 || args.Length == 6) && args[0] == "--self-test") { SelfTest(args); return 0; }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--render-preview") {
                using (InstallerWindow window = new InstallerWindow()) {
                    window.ShowInTaskbar = false; window.StartPosition = FormStartPosition.Manual;
                    window.Location = new Point(-32000, -32000); window.Show(); Application.DoEvents();
                    using (Bitmap b = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height)); b.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
                    window.Hide();
                }
                return 0;
            }
            Application.Run(new InstallerWindow()); return 0;
        } catch (Exception e) {
            if (args.Length > 0) { string log = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "installer-test-error.txt"); File.WriteAllText(log, e.ToString()); }
            else MessageBox.Show(e.Message, "Installatore Holly e Benji", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("TEST FAILED: " + message); }
    private static void SelfTest(string[] args) {
        string root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
        string game = Path.Combine(root, "GameData"); Directory.CreateDirectory(Path.Combine(game, @"string\WW"));
        File.Copy(args[2], Path.Combine(game, @"string\WW\text.ttb"), true);
        File.Copy(args[3], Path.Combine(game, PatchEngine.MainExe), true);
        File.Copy(args[4], Path.Combine(game, "rename.exe"), true);
        string vdf = "\"libraryfolders\" { \"0\" { \"path\" \"C:\\\\Program Files (x86)\\\\Steam\" \"apps\" { \"1163550\" \"123\" } } \"1\" { \"path\" \"D:\\\\Games\\\\Steam Library\" } }";
        List<string> libraries = PatchEngine.LibraryPaths(vdf);
        Require(libraries.Count == 2 && libraries[0] == @"C:\Program Files (x86)\Steam" && libraries[1] == @"D:\Games\Steam Library", "Steam library parser");
        Require(PatchEngine.LibraryPaths("\"libraryfolders\" { \"1\" \"E:\\\\Old Library\" }")[0] == @"E:\Old Library", "legacy Steam library parser");
        Require(PatchEngine.NormalizeRoot(root) == game && PatchEngine.NormalizeRoot(game) == game, "game folder selection");
        PatchEngine.Check(game);
        Require(PatchEngine.Hash(PatchEngine.BuildText(File.ReadAllBytes(args[2]))) == PatchEngine.Manifest.patchedSHA256, "embedded patch reconstruction");
        PatchEngine.Apply("install", game); PatchEngine.Check(game);
        Require(PatchEngine.FileHash(Path.Combine(game, @"string\WW\text.ttb")) == PatchEngine.Manifest.patchedSHA256, "installed text");
        Require(PatchEngine.FileHash(Path.Combine(game, "rename.exe")) == PatchEngine.Manifest.mainExecutableSHA256, "offline launcher");
        PatchEngine.Apply("install", game); PatchEngine.Apply("restore", game); PatchEngine.Apply("restore", game);
        Require(PatchEngine.FileHash(Path.Combine(game, @"string\WW\text.ttb")) == PatchEngine.Manifest.originalSHA256, "text restoration");
        Require(PatchEngine.FileHash(Path.Combine(game, "rename.exe")) == PatchEngine.Manifest.originalLauncherSHA256, "launcher restoration");
        string text = Path.Combine(game, @"string\WW\text.ttb"); byte[] altered = File.ReadAllBytes(text); altered[100] ^= 1; File.WriteAllBytes(text, altered);
        bool refused = false; try { PatchEngine.Apply("install", game); } catch (InvalidOperationException) { refused = true; }
        Require(refused && PatchEngine.FileHash(text) == PatchEngine.Hash(altered), "modified version rejected without writes");
        File.Copy(args[2], text, true);
        if (args.Length == 6) {
            string previousGame = Path.Combine(root, "PreviousVersion", "GameData");
            Directory.CreateDirectory(Path.Combine(previousGame, @"string\WW"));
            string previousBackup = Path.Combine(previousGame, "Previous-Backup"); Directory.CreateDirectory(previousBackup);
            File.Copy(args[2], Path.Combine(previousBackup, "text.ttb"), true);
            File.Copy(args[4], Path.Combine(previousBackup, "rename.exe"), true);
            File.Copy(args[5], Path.Combine(previousGame, @"string\WW\text.ttb"), true);
            File.Copy(args[3], Path.Combine(previousGame, PatchEngine.MainExe), true);
            File.Copy(args[3], Path.Combine(previousGame, "rename.exe"), true);
            Require(PatchEngine.IsPatchedText(PatchEngine.FileHash(Path.Combine(previousGame, @"string\WW\text.ttb"))), "previous patch recognized");
            PatchEngine.Apply("install", previousGame);
            Require(PatchEngine.FileHash(Path.Combine(previousGame, @"string\WW\text.ttb")) == PatchEngine.Manifest.patchedSHA256, "previous version upgraded");
            PatchEngine.Apply("restore", previousGame);
            Require(PatchEngine.FileHash(Path.Combine(previousGame, @"string\WW\text.ttb")) == PatchEngine.Manifest.originalSHA256, "upgraded text restored");
            Require(PatchEngine.FileHash(Path.Combine(previousGame, "rename.exe")) == PatchEngine.Manifest.originalLauncherSHA256, "upgraded loader restored");
        }
        File.WriteAllText(Path.Combine(root, "result.txt"), "PASS: bundled payload, Steam discovery parser, alternate game path, install, verification, repeated install, restore, repeated restore, incompatible version rejection, and previous-version upgrade/restoration. No game process launched.");
    }
}
