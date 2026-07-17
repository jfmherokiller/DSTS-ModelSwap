using MVLibraryNET;
using MVLibraryNET.MBE;

// MbeDumper — extract/inspect MBE tables from MVGL archives.
//
//   list <mvgl> [substr]              List file names in the MVGL (optionally filtered).
//   dump <mvgl> <fileSubstr> <outDir> Extract every .mbe whose name contains fileSubstr,
//                                     dump each sheet to <outDir>\<mbeName>.<sheet>.csv

if (args.Length < 2)
{
    Console.Error.WriteLine("usage:\n  list <mvgl> [substr]\n  dump <mvgl> <fileSubstr> <outDir>");
    return 1;
}

var mode = args[0].ToLowerInvariant();
var mvglPath = args[1];
if (!File.Exists(mvglPath))
{
    Console.Error.WriteLine($"MVGL not found: {mvglPath}");
    return 1;
}

using var reader = MVLibrary.Instance.CreateMvglReader(File.OpenRead(mvglPath), true);
var files = reader.GetFiles();

if (mode == "list")
{
    var substr = args.Length > 2 ? args[2] : null;
    var matches = files
        .Where(f => substr == null || f.FileName.Contains(substr, StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f.FileName);
    var n = 0;
    foreach (var f in matches)
    {
        Console.WriteLine($"{f.FileName}\t({f.ExtractSize} bytes)");
        n++;
    }
    Console.Error.WriteLine($"[{n} match(es) of {files.Length} files]");
    return 0;
}

if (mode == "dump")
{
    if (args.Length < 4) { Console.Error.WriteLine("dump needs <mvgl> <fileSubstr> <outDir>"); return 1; }
    var fileSubstr = args[2];
    var outDir = args[3];
    Directory.CreateDirectory(outDir);

    var targets = files.Where(f =>
        f.FileName.EndsWith(".mbe", StringComparison.OrdinalIgnoreCase) &&
        f.FileName.Contains(fileSubstr, StringComparison.OrdinalIgnoreCase)).ToArray();

    if (targets.Length == 0) { Console.Error.WriteLine($"No .mbe matching '{fileSubstr}'."); return 1; }

    foreach (var f in targets)
    {
        Console.Error.WriteLine($"Extracting {f.FileName} ...");
        using var data = reader.ExtractFile(f);
        using var ms = new MemoryStream(data.Span.ToArray());
        var mbe = new Mbe(ms, false);

        var baseName = Path.GetFileNameWithoutExtension(f.FileName);
        foreach (var (sheetName, sheet) in mbe.Sheets)
        {
            var outFile = Path.Combine(outDir, $"{baseName}.{sheetName}.csv");
            File.WriteAllText(outFile, sheet.ToCsv());
            Console.WriteLine($"  -> {outFile}  (sheet '{sheetName}')");
        }
    }
    return 0;
}

if (mode == "testap")
{
    // testap <mvgl> <mbeFileSubstr> <sheet> <apCsvPath>  — replicate MbeProcessor append offline.
    if (args.Length < 5) { Console.Error.WriteLine("testap needs <mvgl> <mbeFileSubstr> <sheet> <apCsvPath>"); return 1; }
    var mbeSubstr = args[2];
    var sheetName = args[3];
    var apCsvPath = args[4];

    var target = files.First(f =>
        f.FileName.EndsWith(".mbe", StringComparison.OrdinalIgnoreCase) &&
        f.FileName.Contains(mbeSubstr, StringComparison.OrdinalIgnoreCase));
    using var data = reader.ExtractFile(target);
    using var ms = new MemoryStream(data.Span.ToArray());
    var mbe = new Mbe(ms, false);

    var sheet = mbe.Sheets[sheetName];
    Console.Error.WriteLine($"Before append:\n{sheet.ToCsv()}");
    sheet.AppendCsv(File.ReadAllText(apCsvPath));
    Console.Error.WriteLine($"After append:\n{sheet.ToCsv()}");

    // Round-trip through Write to catch serialization errors.
    using var outMs = new MemoryStream();
    mbe.Write(outMs);
    Console.Error.WriteLine($"[Write OK — {outMs.Length} bytes]");
    return 0;
}

if (mode == "extractregex")
{
    // extractregex <mvgl> <regex> <outDir> — extract files whose FileName matches the regex.
    if (args.Length < 4) { Console.Error.WriteLine("extractregex needs <mvgl> <regex> <outDir>"); return 1; }
    var rx = new System.Text.RegularExpressions.Regex(args[2], System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    var outDir = args[3];
    Directory.CreateDirectory(outDir);
    int n = 0;
    foreach (var f in files)
    {
        var name = Path.GetFileName(f.FileName);
        if (!rx.IsMatch(name)) continue;
        using var data = reader.ExtractFile(f);
        File.WriteAllBytes(Path.Combine(outDir, name), data.Span.ToArray());
        n++;
    }
    Console.Error.WriteLine($"[extracted {n} files matching /{args[2]}/ to {outDir}]");
    return 0;
}

if (mode == "extract")
{
    if (args.Length < 4) { Console.Error.WriteLine("extract needs <mvgl> <fileSubstr> <outDir>"); return 1; }
    var fileSubstr = args[2];
    var outDir = args[3];
    Directory.CreateDirectory(outDir);

    var targets = files.Where(f =>
        f.FileName.Contains(fileSubstr, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (targets.Length == 0) { Console.Error.WriteLine($"No file matching '{fileSubstr}'."); return 1; }

    foreach (var f in targets)
    {
        using var data = reader.ExtractFile(f);
        var outFile = Path.Combine(outDir, Path.GetFileName(f.FileName));
        File.WriteAllBytes(outFile, data.Span.ToArray());
        Console.WriteLine($"  -> {outFile}  ({data.Span.Length} bytes)");
    }
    return 0;
}

Console.Error.WriteLine($"Unknown mode: {mode}");
return 1;
