using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;

namespace ForgeScope.Hub;

// Uploaded content is data: never execute builds, scripts or extraction metadata.
public static class SourceImport
{
    public static async Task<Investigation> Analyze(string owner, IFormCollection form, Analyzer analyzer, CancellationToken ct)
    {
        string temp = Path.Combine(Path.GetTempPath(), "forgescope-upload-" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        try
        {
            string root = Path.Combine(temp, "source"); Directory.CreateDirectory(root);
            string title; var file = form.Files.GetFile("archive");
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            if (file is not null)
            {
                if (form["code"].ToString().Length > 0 || form.Files.Count != 1 || file.Length is <= 0 or > 3_000_000 || !file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid ZIP input");
                using var bytes = new MemoryStream(); await file.CopyToAsync(bytes, ct); bytes.Position = 0;
                using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
                if (zip.Entries.Count > 500) throw new InvalidOperationException("ZIP entry limit exceeded");
                long size = 0; int count = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = entry.FullName;
                    if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(p => p is "." or "..") || name.Any(char.IsControl) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new InvalidOperationException("Unsafe ZIP entry");
                    if (name.EndsWith('/')) continue;
                    if (!GitHub.SafePath(name)) continue; // Exclude credentials and generated folders.
                    if (!names.Add(name) || ++count > 250 || entry.Length > 512_000 || (size += entry.Length) > 10_000_000)
                        throw new InvalidOperationException("ZIP source limits exceeded");
                    using var input = entry.Open(); using var content = new MemoryStream(); var buffer = new byte[16_384]; int read;
                    while ((read = await input.ReadAsync(buffer, ct)) > 0)
                    { if (content.Length + read > 512_000 || content.Length + read > entry.Length) throw new InvalidOperationException("ZIP decoded size mismatch"); content.Write(buffer, 0, read); }
                    var data = content.ToArray(); if (data.LongLength != entry.Length) throw new InvalidOperationException("ZIP decoded size mismatch");
                    string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await File.WriteAllBytesAsync(path, data, ct); digest.AppendData(Encoding.UTF8.GetBytes(name + "\0")); digest.AppendData(data);
                }
                if (count == 0) throw new InvalidOperationException("ZIP has no eligible source files");
                title = Path.GetFileNameWithoutExtension(file.FileName.Replace('\\', '/'));
            }
            else
            {
                string name = form["filename"].ToString(), code = form["code"].ToString();
                if (!GitHub.SafePath(name) || name.Length > 200 || !Path.HasExtension(name) || string.IsNullOrWhiteSpace(code) || Encoding.UTF8.GetByteCount(code) > 200_000)
                    throw new InvalidOperationException("Invalid code or file name");
                // Strict UTF-8 avoids silently changing the source snapshot.
                var data = new UTF8Encoding(false, true).GetBytes(code); digest.AppendData(Encoding.UTF8.GetBytes(name + "\0")); digest.AppendData(data);
                string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, data, ct); title = name;
            }
            var report = await analyzer.Scan(root, "", "", temp, ct);
            var value = new Investigation { OwnerId = owner, Origin = "local", Repository = "local/source", Title = title[..Math.Min(title.Length, 200)],
                Commit = Convert.ToHexStringLower(digest.GetHashAndReset()), Report = report };
            value.Event(owner, "analyzed", value.Commit); return value;
        }
        finally { Directory.Delete(temp, true); }
    }
}
