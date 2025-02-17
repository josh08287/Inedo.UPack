﻿using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Inedo.UPack.Net
{
    internal sealed class LocalPackageRepository
    {
        private readonly Lazy<ILookup<PackageKey, PackageFile>> allPackages;

        public LocalPackageRepository(string rootPath)
        {
            this.RootPath = rootPath;
            this.allPackages = new Lazy<ILookup<PackageKey, PackageFile>>(this.ReadLocalPackages);
        }

        public string RootPath { get; }

        public IEnumerable<RemoteUniversalPackage> ListPackages(string? group)
        {
            return this.allPackages.Value
                .Where(g => group == null || string.Equals(g.Key.Group, group, StringComparison.OrdinalIgnoreCase))
                .Select(g => new RemoteUniversalPackage(GetMungedPackage(g)));
        }
        public IEnumerable<RemoteUniversalPackage> SearchPackages(string? searchTerm)
        {
            return this.ListPackages(null)
                .Where(p => isMatch(p.Name) || isMatch(p.Title) || isMatch(p.Description));

            bool isMatch(string? s)
            {
                if (string.IsNullOrEmpty(searchTerm))
                    return true;
                if (string.IsNullOrEmpty(s))
                    return false;

                return s!.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
        public IEnumerable<RemoteUniversalPackageVersion> ListPackageVersions(string? group, string? name)
        {
            IEnumerable<PackageFile> packages;

            if (group is null && name is null)
                packages = this.allPackages.Value.SelectMany(g => g);
            else if (group is not null && name is null)
                packages = this.allPackages.Value.Where(g => string.Equals(g.Key.Group, group, StringComparison.OrdinalIgnoreCase)).SelectMany(g => g);
            else if (group is null && name is not null)
                packages = this.allPackages.Value.Where(g => string.Equals(g.Key.Name, name, StringComparison.OrdinalIgnoreCase)).SelectMany(g => g);
            else
                packages = this.allPackages.Value[new PackageKey(group, name!)];

            return packages.Select(p => new RemoteUniversalPackageVersion(p.JObject));
        }
        public RemoteUniversalPackageVersion? GetPackageVersion(UniversalPackageId id, UniversalPackageVersion version)
        {
            return this.ListPackageVersions(id?.Group, id?.Name)
                .FirstOrDefault(p => p.Version == version);
        }
        public Stream? GetPackageStream(UniversalPackageId id, UniversalPackageVersion? version)
        {
            var packageVersions = this.allPackages.Value[new PackageKey(id.Group, id.Name)];
            var match = default(PackageFile);
            if (version == null)
                match = packageVersions.OrderByDescending(p => UniversalPackageVersion.Parse((string?)p.JObject["version"])).FirstOrDefault();
            else
                match = packageVersions.FirstOrDefault(p => UniversalPackageVersion.Parse((string?)p.JObject["version"]) == version);

            if (match.IsNull)
                return null;

            return new FileStream(match.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        public Stream? GetPackageFileStream(UniversalPackageId id, UniversalPackageVersion? version, string filePath)
        {
            Stream? packageStream = null;
            ZipArchive? zip = null;
            try
            {
                packageStream = this.GetPackageStream(id, version);
                if (packageStream == null)
                    return null;

                zip = new ZipArchive(packageStream, ZipArchiveMode.Read);
                var entry = zip.GetEntry(filePath);
                if (entry == null)
                    return null;

                return new ZipEntryStream(entry.Open(), zip);
            }
            catch
            {
                zip?.Dispose();
                packageStream?.Dispose();
                throw;
            }
        }

        private ILookup<PackageKey, PackageFile> ReadLocalPackages()
        {
            return readPackages()
                .ToLookup(p => new PackageKey(p.JObject));

            IEnumerable<PackageFile> readPackages()
            {
                foreach (var fileName in Directory.EnumerateFiles(this.RootPath, "*.upack", SearchOption.TopDirectoryOnly))
                {
                    JsonObject obj;
                    using (var zip = ZipFile.OpenRead(fileName))
                    {
                        var upackEntry = zip.GetEntry("upack.json");
                        if (upackEntry == null)
                            continue;

                        using var upackStream = upackEntry.Open();
                        obj = (JsonObject)JsonNode.Parse(upackStream)!;
                        obj["published"] = new DateTimeOffset(File.GetCreationTime(fileName));
                        obj["size"] = new FileInfo(fileName).Length;
                    }

                    yield return new PackageFile(fileName, obj);
                }
            }
        }

        private static JsonObject GetMungedPackage(IEnumerable<PackageFile> packageVersions)
        {
            var sorted = (from p in packageVersions
                          let v = UniversalPackageVersion.Parse((string?)p.JObject["version"])
                          orderby v descending
                          select p).ToList();

            var latest = (JsonObject)JsonNode.Parse(sorted.First().JObject.ToJsonString())!;
            latest["latestVersion"] = latest["version"];
            latest.Remove("version");
            latest["versions"] = new JsonArray(sorted.Select(v => v.JObject["version"]).ToArray());
            return latest;
        }

        private readonly struct PackageKey : IEquatable<PackageKey>
        {
            public PackageKey(JsonObject obj)
            {
                this.Group = (string?)obj["group"] ?? string.Empty;
                this.Name = (string?)obj["name"] ?? string.Empty;
            }
            public PackageKey(string? group, string name)
            {
                this.Group = group ?? string.Empty;
                this.Name = name;
            }

            public string Group { get; }
            public string Name { get; }

            public bool Equals(PackageKey other) => string.Equals(this.Group, other.Group, StringComparison.OrdinalIgnoreCase) && string.Equals(this.Name, other.Name, StringComparison.OrdinalIgnoreCase);
            public override bool Equals(object? obj) => obj is PackageKey key && this.Equals(key);
            public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(this.Name);
        }

        private readonly struct PackageFile
        {
            public PackageFile(string fileName, JsonObject obj)
            {
                this.FileName = fileName;
                this.JObject = obj;
            }

            public string FileName { get; }
            public JsonObject JObject { get; }
            public bool IsNull => this.FileName == null;
        }

        /// <summary>
        /// Simple wrapper for a <see cref="ZipArchiveEntry"/> that disposes the containing zip archive when this stream is disposed.
        /// </summary>
        private sealed class ZipEntryStream : Stream
        {
            private readonly Stream entryStream;
            private readonly ZipArchive zip;

            public ZipEntryStream(Stream entryStream, ZipArchive zip)
            {
                this.entryStream = entryStream;
                this.zip = zip;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }
            public override int Read(byte[] buffer, int offset, int count) => this.entryStream.Read(buffer, offset, count);
            public override int ReadByte() => this.entryStream.ReadByte();
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => this.entryStream.ReadAsync(buffer, offset, count, cancellationToken);
            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) => this.entryStream.CopyToAsync(destination, bufferSize, cancellationToken);
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.entryStream.Dispose();
                    this.zip.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
