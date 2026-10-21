using ShareX.HelpersLib;
using System.IO.Compression;

internal static class FileAndZipTests
{
    internal static void Register(List<(string, Func<Task>)> tests)
    {
        tests.Add(("File unique name large suffix", UniqueName));
        tests.Add(("Zip extraction blocks symlink escape", ZipLinks));
        tests.Add(("Zip extraction blocks lexical traversal", ZipTraversal));
        tests.Add(("Zip extraction trailing destination separator", ZipTrailingSeparator));
        tests.Add(("File unique name consecutive maximum suffixes", MaximumSuffix));
    }

    private static Task UniqueName()
    {
        foreach (string name in new[] { "photo (999999999999999999999999).png", "photo (2147483647).png", "photo (9223372036854775807).png", "photo (3).png" })
        {
            string path = Path.Combine(Program.Root, name);
            File.WriteAllText(path, "original");
            string unique = FileHelpers.GetUniqueFilePath(path);
            Program.Check(!File.Exists(unique), "Unique name already exists.");
            Program.Check(!Path.GetFileName(unique).Contains("(-"), "Suffix overflowed to negative.");
            Program.Check(Path.GetDirectoryName(unique) == Program.Root, "Unique name escaped directory.");
            if (name == "photo (3).png") Program.Check(Path.GetFileName(unique) == "photo (4).png", "Normal suffix behavior changed.");
        }
        return Task.CompletedTask;
    }

    private static Task ZipLinks()
    {
        string destination = Path.Combine(Program.Root, "extract");
        string outside = Path.Combine(Program.Root, "outside");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(destination, "link"), outside);
        File.WriteAllText(Path.Combine(outside, "existing.txt"), "original");
        File.CreateSymbolicLink(Path.Combine(destination, "file.txt"), Path.Combine(outside, "existing.txt"));
        string archive = CreateArchive("links.zip", "link/escaped.txt", "file.txt", "safe.txt");
        ZipManager.Extract(archive, destination);
        Program.Check(!File.Exists(Path.Combine(outside, "escaped.txt")), "ZIP escaped through existing directory symlink.");
        Program.Check(File.ReadAllText(Path.Combine(outside, "existing.txt")) == "original", "ZIP overwrote symlink target.");
        Program.Check(File.ReadAllText(Path.Combine(destination, "safe.txt")) == "payload", "Safe entry was not extracted.");
        return Task.CompletedTask;
    }

    private static Task ZipTraversal()
    {
        string destination = Path.Combine(Program.Root, "traversal");
        string archive = CreateArchive("traversal.zip", "../traversal-sibling/escape.txt", "../escape.txt", "nested/safe.txt");
        ZipManager.Extract(archive, destination);
        Program.Check(!File.Exists(Path.Combine(Program.Root, "escape.txt")), "ZIP escaped through parent traversal.");
        Program.Check(!File.Exists(Path.Combine(Program.Root, "traversal-sibling/escape.txt")), "ZIP escaped to sibling with shared prefix.");
        Program.Check(File.Exists(Path.Combine(destination, "nested/safe.txt")), "Nested safe entry was not extracted.");
        return Task.CompletedTask;
    }

    private static Task ZipTrailingSeparator()
    {
        string destination = Path.Combine(Program.Root, "trailing") + Path.DirectorySeparatorChar;
        string archive = CreateArchive("trailing.zip", "safe.txt");
        ZipManager.Extract(archive, destination);
        Program.Check(File.Exists(Path.Combine(destination, "safe.txt")), "Trailing destination separator prevented extraction.");
        return Task.CompletedTask;
    }

    private static Task MaximumSuffix()
    {
        string path = Path.Combine(Program.Root, "max (9223372036854775806).txt");
        File.WriteAllText(path, "original");
        File.WriteAllText(Path.Combine(Program.Root, "max (9223372036854775807).txt"), "original");
        string unique = FileHelpers.GetUniqueFilePath(path);
        Program.Check(!Path.GetFileName(unique).Contains("(-"), "Consecutive maximum suffixes caused overflow.");
        return Task.CompletedTask;
    }

    private static string CreateArchive(string name, params string[] entries)
    {
        string path = Path.Combine(Program.Root, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string entry in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write("payload");
        }
        return path;
    }
}
