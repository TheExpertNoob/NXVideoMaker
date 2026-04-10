using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace NXVideoMaker;

public record BuildConfig(
    string Title,
    string Author,
    string DisplayVersion,
    string TitleId,
    string IconPath,
    string VideoFolder,
    string KeysPath,
    int    KeyGen,
    string SdkVersion,
    string SysVersion,
    string ExeDir      // directory containing NXVideoMaker.exe
);

public static class BuildRunner
{
    public static async Task RunAsync(
        BuildConfig      cfg,
        IProgress<string> log,
        CancellationToken ct)
    {
        // ── Validate inputs ──────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(cfg.Title))       throw new Exception("Title is required.");
        if (string.IsNullOrWhiteSpace(cfg.Author))      throw new Exception("Author is required.");
        if (string.IsNullOrWhiteSpace(cfg.TitleId))     throw new Exception("Title ID is required.");
        if (string.IsNullOrWhiteSpace(cfg.DisplayVersion)) throw new Exception("Display version is required.");
        if (!File.Exists(cfg.IconPath))                 throw new Exception("Icon file not found.");
        if (!Directory.Exists(cfg.VideoFolder))         throw new Exception("Video folder not found.");
        if (!File.Exists(cfg.KeysPath))                 throw new Exception("Keys file not found.");

        using var res = new ResourceHelper();
        string work = res.WorkDir;

        try
        {
            // ── Extract embedded tools ───────────────────────────────────────
            log.Report("[init] Extracting tools...");
            string hacpack   = res.Extract("hacpack.exe");
            string npdmtool  = res.Extract("npdmtool.exe");
            string npdmJson  = res.Extract("npdm.json");
            string logoDir   = res.ExtractFolder("logo", "logo");

            // Make binaries executable (no-op on Windows, safety for Wine/Proton)
            foreach (var bin in new[] { hacpack, npdmtool })
                File.SetAttributes(bin, FileAttributes.Normal);

            // ── Detect signing keys (silent — log hints only) ────────────────
            log.Report("[keys] Checking for optional signing keys...");
            string? acidKey    = FindKey(cfg.ExeDir, "acid_private.pem",    log);
            string? ncasig1Key = FindKey(cfg.ExeDir, "ncasig1_private.pem", log);
            string? ncasig2Key = FindKey(cfg.ExeDir, "ncasig2_private.pem", log);
            string? ncasig2Mod = null;

            if (ncasig2Key != null)
            {
                log.Report("[keys] Deriving ncasig2 modulus from PEM...");
                ncasig2Mod = Path.Combine(work, "ncasig2_modulus.bin");
                DeriveModulus(ncasig2Key, ncasig2Mod);
                log.Report("[keys] ncasig2_modulus.bin derived.");
            }

            // ── Copy keys into work dir ──────────────────────────────────────
            string keysWork = Path.Combine(work, "keys.dat");
            File.Copy(cfg.KeysPath, keysWork, overwrite: true);

            // ── Detect content type → stage exefs ───────────────────────────
            log.Report("[-1/7] Detecting content type...");
            string exefsWork = Path.Combine(work, "exefs");
            Directory.CreateDirectory(exefsWork);

            bool hasHtml = File.Exists(Path.Combine(cfg.VideoFolder, "index.html"));
            bool hasMp4  = File.Exists(Path.Combine(cfg.VideoFolder, "video.mp4"));

            if (hasHtml)
            {
                log.Report("[-1/7] Found index.html — using template/index/exefs");
                StageExefs(res, "index", exefsWork);
            }
            else if (hasMp4)
            {
                log.Report("[-1/7] Found video.mp4 — using template/video/exefs");
                StageExefs(res, "video", exefsWork);
            }
            else
            {
                throw new Exception(
                    "video folder must contain either index.html or video.mp4 — neither found.");
            }

            // ── Step 0: Patch npdm.json → build NPDM ────────────────────────
            log.Report("[0/7] Building NPDM...");
            string patchedNpdm = Path.Combine(work, "npdm_patched.json");
            PatchNpdm(npdmJson, patchedNpdm, cfg.TitleId);

            string npdmOut = Path.Combine(exefsWork, "main.npdm");
            await RunToolAsync(npdmtool,
                $"\"{patchedNpdm}\" \"{npdmOut}\"",
                work, log, ct);

            // ── Step 1 (formerly 2): Generate control romfs ──────────────────
            log.Report("[1/7] Generating control romfs...");
            string controlRomfs = Path.Combine(work, "control_romfs");
            NacpGenerator.Generate(cfg.Title, cfg.Author, cfg.DisplayVersion,
                                   cfg.TitleId, cfg.IconPath, controlRomfs);

            string ncaDir  = Path.Combine(work, "nca");
            string nspDir  = Path.Combine(work, "nsp");
            Directory.CreateDirectory(ncaDir);
            Directory.CreateDirectory(nspDir);

            // ── Step 2 (formerly 3): Build Control NCA ───────────────────────
            log.Report("[2/7] Building Control NCA...");
            var controlFlags = new FlagBuilder(keysWork, ncaDir, cfg)
                .Add("--ncatype control")
                .Add($"--romfsdir \"{controlRomfs}\"")
                .AddIfFile(ncasig1Key, $"--ncasig1privatekey \"{ncasig1Key}\"");

            await RunToolAsync(hacpack, controlFlags.Build(), work, log, ct);

            string controlNca = LatestNca(ncaDir, exclude: null);
            log.Report($"[2/7] Control NCA: {controlNca}");

            // ── Step 3 (formerly 4): Build Program NCA ───────────────────────
            log.Report("[3/7] Building Program NCA...");
            var programFlags = new FlagBuilder(keysWork, ncaDir, cfg)
                .Add("--ncatype program")
                .Add($"--exefsdir \"{exefsWork}\"")
                .Add($"--logodir \"{logoDir}\"")
                .AddIfFile(ncasig2Key, $"--ncasig2privatekey \"{ncasig2Key}\"")
                .AddIfFile(ncasig2Mod, $"--ncasig2modulus \"{ncasig2Mod}\"")
                .AddIfFile(acidKey,    $"--acidsigprivatekey \"{acidKey}\"")
                .AddIfFile(ncasig1Key, $"--ncasig1privatekey \"{ncasig1Key}\"");

            await RunToolAsync(hacpack, programFlags.Build(), work, log, ct);

            string programNca = LatestNca(ncaDir, exclude: Path.GetFileName(controlNca));
            log.Report($"[3/7] Program NCA: {programNca}");

            // ── Step 4 (formerly 5): Build Manual NCA ───────────────────────
            log.Report("[4/7] Building Manual NCA...");
            string manualStage = Path.Combine(work, "manual_stage", "html-document", ".htdocs");
            Directory.CreateDirectory(manualStage);
            CopyDirectory(cfg.VideoFolder, manualStage);

            var manualFlags = new FlagBuilder(keysWork, ncaDir, cfg)
                .Add("--ncatype manual")
                .Add($"--romfsdir \"{Path.Combine(work, "manual_stage")}\"")
                .AddIfFile(ncasig1Key, $"--ncasig1privatekey \"{ncasig1Key}\"");

            await RunToolAsync(hacpack, manualFlags.Build(), work, log, ct);

            string manualNca = LatestNca(ncaDir,
                exclude: Path.GetFileName(controlNca),
                exclude2: Path.GetFileName(programNca));
            log.Report($"[4/7] Manual NCA: {manualNca}");

            // ── Step 5 (formerly 6): Build Meta NCA ─────────────────────────
            log.Report("[5/7] Building Meta NCA...");
            var metaFlags = new FlagBuilder(keysWork, ncaDir, cfg)
                .Add("--ncatype meta")
                .Add("--titletype application")
                .Add($"--requiredsystemversion {cfg.SysVersion}")
                .Add($"--programnca \"{controlNca}\"")
                .Add($"--controlnca \"{controlNca}\"")
                .Add($"--htmldocnca \"{manualNca}\"")
                .AddIfFile(ncasig1Key, $"--ncasig1privatekey \"{ncasig1Key}\"");

            // Meta NCA references the full paths
            var metaFlagsFull = new FlagBuilder(keysWork, ncaDir, cfg)
                .Add("--ncatype meta")
                .Add("--titletype application")
                .Add($"--requiredsystemversion {cfg.SysVersion}")
                .Add($"--programnca \"{programNca}\"")
                .Add($"--controlnca \"{controlNca}\"")
                .Add($"--htmldocnca \"{manualNca}\"")
                .AddIfFile(ncasig1Key, $"--ncasig1privatekey \"{ncasig1Key}\"");

            await RunToolAsync(hacpack, metaFlagsFull.Build(), work, log, ct);

            // ── Step 6 (formerly 7): Build NSP ──────────────────────────────
            log.Report("[6/7] Building NSP...");
            await RunToolAsync(hacpack,
                $"-k \"{keysWork}\" -o \"{nspDir}\" --type nsp --ncadir \"{ncaDir}\" --titleid {cfg.TitleId}",
                work, log, ct);

            // ── Rename and move NSP next to exe ──────────────────────────────
            log.Report("[7/7] Finalising NSP...");
            string tidLower  = cfg.TitleId.ToLowerInvariant();
            string rawNsp    = Path.Combine(nspDir, $"{tidLower}.nsp");
            string nspName   = $"{tidLower}.nsp";
            string nspDest   = Path.Combine(cfg.ExeDir, nspName);

            if (File.Exists(rawNsp))
                File.Move(rawNsp, nspDest, overwrite: true);
            else
                throw new Exception($"Expected NSP not found: {rawNsp}");

            log.Report("");
            log.Report("─────────────────────────────────────────");
            log.Report($" Build complete.");
            log.Report($" NSP: {nspName}");
            log.Report("─────────────────────────────────────────");
        }
        catch (OperationCanceledException)
        {
            log.Report("[cancelled] Build cancelled — cleaning up...");
            throw; // ResourceHelper.Dispose() in the using block handles temp dir
        }
        catch
        {
            // ResourceHelper.Dispose() in the using block handles temp dir cleanup
            throw;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? FindKey(string dir, string filename, IProgress<string> log)
    {
        string path = Path.Combine(dir, filename);
        if (File.Exists(path))
        {
            log.Report($"[keys]   FOUND    {filename}");
            return path;
        }
        log.Report($"[keys]   not found: {filename}");
        return null;
    }

    private static void DeriveModulus(string pemPath, string outPath)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(pemPath));
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        File.WriteAllBytes(outPath, parameters.Modulus!);
    }

    private static void PatchNpdm(string srcPath, string dstPath, string titleId)
    {
        var doc  = JsonNode.Parse(File.ReadAllText(srcPath))!;
        string hex = $"0x{titleId}";
        doc["title_id"]             = hex;
        doc["program_id"]           = hex;
        doc["program_id_range_min"] = hex;
        doc["program_id_range_max"] = hex;
        File.WriteAllText(dstPath, doc.ToJsonString());
    }

    private static void StageExefs(ResourceHelper res, string variant, string destExefs)
    {
        // Extract the embedded template/<variant>/exefs/main binary
        // Embedded resource name: NXVideoMaker.Resources.template.<variant>.exefs.main
        string resourceSuffix = $"template.{variant}.exefs.main";
        Directory.CreateDirectory(destExefs);
        string extracted = res.Extract(resourceSuffix, targetFileName: $"main_{variant}");
        File.Move(extracted, Path.Combine(destExefs, "main"), overwrite: true);
    }

    private static string LatestNca(string ncaDir, string? exclude = null, string? exclude2 = null)
    {
        return Directory.GetFiles(ncaDir, "*.nca")
            .Where(f => !string.Equals(Path.GetFileName(f), exclude, StringComparison.OrdinalIgnoreCase))
            .Where(f => !string.Equals(Path.GetFileName(f), exclude2, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(f => f)
            .FirstOrDefault()
            ?? throw new Exception("Expected NCA not found in output directory.");
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    private static async Task RunToolAsync(
        string tool, string args, string workDir,
        IProgress<string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(tool, args)
        {
            WorkingDirectory       = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new Exception($"Failed to start {Path.GetFileName(tool)}");

        // Register a callback that kills the process tree if the token fires
        using var kill = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        });

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) log.Report(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) log.Report(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(CancellationToken.None); // wait for kill to complete

        ct.ThrowIfCancellationRequested(); // surface cancellation to the caller

        if (proc.ExitCode != 0)
            throw new Exception(
                $"{Path.GetFileName(tool)} exited with code {proc.ExitCode}");
    }

    // ── Simple flag builder ───────────────────────────────────────────────────

    private class FlagBuilder
    {
        private readonly List<string> _parts = new();

        public FlagBuilder(string keysPath, string ncaDir, BuildConfig cfg)
        {
            _parts.Add($"-k \"{keysPath}\"");
            _parts.Add($"-o \"{ncaDir}\"");
            _parts.Add("--type nca");
            _parts.Add($"--keygeneration {cfg.KeyGen}");
            _parts.Add($"--sdkversion {cfg.SdkVersion}");
            _parts.Add($"--titleid {cfg.TitleId}");
        }

        public FlagBuilder Add(string flag)       { _parts.Add(flag); return this; }
        public FlagBuilder AddIfFile(string? path, string flag)
        {
            if (path != null && File.Exists(path)) _parts.Add(flag);
            return this;
        }

        public string Build() => string.Join(" ", _parts);
    }
}