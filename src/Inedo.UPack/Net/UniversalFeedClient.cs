﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Inedo.UPack.Net
{
    /// <summary>
    /// Provides access to the universal feed API.
    /// </summary>
    public sealed class UniversalFeedClient
    {
        private readonly ApiTransport transport;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniversalFeedClient"/> class.
        /// </summary>
        /// <param name="uri">The uri of the feed API endpoint.</param>
        /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null or empty.</exception>
        /// <exception cref="ArgumentException"><paramref name="uri"/> is invalid.</exception>
        public UniversalFeedClient(string uri)
            : this(new UniversalFeedEndpoint(uri))
        {
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="UniversalFeedClient"/> class.
        /// </summary>
        /// <param name="endpoint">Connection information for the remote endpoint.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is null.</exception>
        public UniversalFeedClient(UniversalFeedEndpoint endpoint)
            : this(endpoint, null)
        {
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="UniversalFeedClient"/> class.
        /// </summary>
        /// <param name="endpoint">Connection information for the remote endpoint.</param>
        /// <param name="transport">Transport layer to use for requests. The default is <see cref="DefaultApiTransport"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is null.</exception>
        public UniversalFeedClient(UniversalFeedEndpoint endpoint, ApiTransport transport)
        {
            this.Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            this.transport = transport ?? new DefaultApiTransport();
        }

        /// <summary>
        /// Gets the endpoint connection information.
        /// </summary>
        public UniversalFeedEndpoint Endpoint { get; }

        /// <summary>
        /// Returns a list of packages with the specified group.
        /// </summary>
        /// <param name="group">Group of packages to return. Null indicates all packages are desired.</param>
        /// <param name="maxCount">Maximum number of packages to return. Null indicates no limit.</param>
        /// <returns>List of packages in the specified group.</returns>
        public Task<IReadOnlyList<RemoteUniversalPackage>> ListPackagesAsync(string group, int? maxCount) => this.ListPackagesAsync(group, maxCount, default);
        /// <summary>
        /// Returns a list of packages with the specified group.
        /// </summary>
        /// <param name="group">Group of packages to return. Null indicates all packages are desired.</param>
        /// <param name="maxCount">Maximum number of packages to return. Null indicates no limit.</param>
        /// <param name="cancellationToken">Cancellation token for asynchronous operations.</param>
        /// <returns>List of packages in the specified group.</returns>
        public async Task<IReadOnlyList<RemoteUniversalPackage>> ListPackagesAsync(string group, int? maxCount, CancellationToken cancellationToken)
        {
            var url = FormatUrl("packages", ("group", group), ("count", maxCount));

            var request = new ApiRequest(this.Endpoint, url);
            using (var response = await this.transport.GetResponseAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
                    throw new InvalidDataException($"Server returned {response.ContentType} content type; expected application/json.");

                using (var responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream, Encoding.UTF8))
                using (var jsonReader = new JsonTextReader(reader))
                {
                    var arr = await JArray.LoadAsync(jsonReader, cancellationToken).ConfigureAwait(false);
                    var results = new List<RemoteUniversalPackage>(arr.Count);

                    foreach (var token in arr)
                    {
                        if (!(token is JObject obj))
                            throw new InvalidDataException("Unexpected token in JSON array.");

                        results.Add(new RemoteUniversalPackage(obj));
                    }

                    return results.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Returns a list of all package versions with the specified package ID.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
        /// <returns>List of all package versions.</returns>
        public Task<IReadOnlyList<RemoteUniversalPackageVersion>> ListPackageVersionsAsync(UniversalPackageId id) => this.ListVersionsInternalAsync(id, null, false, null, default);
        /// <summary>
        /// Returns a list of all package versions with the specified package ID.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="includeFileList">Value indicating whether a list of files inside the package should be included. This will incur additional overhead.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
        /// <returns>List of all package versions.</returns>
        public Task<IReadOnlyList<RemoteUniversalPackageVersion>> ListPackageVersionsAsync(UniversalPackageId id, bool includeFileList) => this.ListVersionsInternalAsync(id, null, includeFileList, null, default);
        /// <summary>
        /// Returns a list of all package versions with the specified package ID.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="includeFileList">Value indicating whether a list of files inside the package should be included. This will incur additional overhead.</param>
        /// <param name="maxCount">Maximum number of versions to return. Null indicates no limit.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
        /// <returns>List of all package versions.</returns>
        public Task<IReadOnlyList<RemoteUniversalPackageVersion>> ListPackageVersionsAsync(UniversalPackageId id, bool includeFileList, int? maxCount) => this.ListVersionsInternalAsync(id, null, includeFileList, maxCount, default);
        /// <summary>
        /// Returns a list of all package versions with the specified package ID.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="includeFileList">Value indicating whether a list of files inside the package should be included. This will incur additional overhead.</param>
        /// <param name="maxCount">Maximum number of versions to return. Null indicates no limit.</param>
        /// <param name="cancellationToken">Cancellation token for asynchronous operations.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
        /// <returns>List of all package versions.</returns>
        public Task<IReadOnlyList<RemoteUniversalPackageVersion>> ListPackageVersionsAsync(UniversalPackageId id, bool includeFileList, int? maxCount, CancellationToken cancellationToken) => this.ListVersionsInternalAsync(id, null, includeFileList, maxCount, cancellationToken);

        /// <summary>
        /// Returns metadata for a specific version of a package, or null if the package was not found.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="version">Version of the package.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null or <paramref name="version"/> is null.</exception>
        /// <returns>Package vesion metadata if it was found; otherwise null.</returns>
        public Task<RemoteUniversalPackageVersion> GetPackageVersionAsync(UniversalPackageId id, UniversalPackageVersion version) => this.GetPackageVersionAsync(id, version, false, default);
        /// <summary>
        /// Returns metadata for a specific version of a package, or null if the package was not found.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="version">Version of the package.</param>
        /// <param name="includeFileList">Value indicating whether a list of files inside the package should be included. This will incur additional overhead.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null or <paramref name="version"/> is null.</exception>
        /// <returns>Package vesion metadata if it was found; otherwise null.</returns>
        public Task<RemoteUniversalPackageVersion> GetPackageVersionAsync(UniversalPackageId id, UniversalPackageVersion version, bool includeFileList) => this.GetPackageVersionAsync(id, version, includeFileList, default);
        /// <summary>
        /// Returns metadata for a specific version of a package, or null if the package was not found.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="version">Version of the package.</param>
        /// <param name="includeFileList">Value indicating whether a list of files inside the package should be included. This will incur additional overhead.</param>
        /// <param name="cancellationToken">Cancellation token for asynchronous operations.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null or <paramref name="version"/> is null.</exception>
        /// <returns>Package vesion metadata if it was found; otherwise null.</returns>
        public async Task<RemoteUniversalPackageVersion> GetPackageVersionAsync(UniversalPackageId id, UniversalPackageVersion version, bool includeFileList, CancellationToken cancellationToken)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            var results = await this.ListVersionsInternalAsync(id, version, includeFileList, null, cancellationToken).ConfigureAwait(false);
            return results.FirstOrDefault();
        }

        /// <summary>
        /// Returns a stream containing the specified package if it is available; otherwise null.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="version">Version of the package. Specify null for the latest version.</param>
        /// <param name="cancellationToken">Cancellation token for asynchronous operations.</param>
        /// <returns>Stream containing the specified package if it is available; otherwise null.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null.</exception>
        /// <remarks>
        /// The stream returned by this method is not buffered at all. If random access is required, the caller must
        /// first copy it to another stream.
        /// </remarks>
        public async Task<Stream> GetPackageStreamAsync(UniversalPackageId id, UniversalPackageVersion version, CancellationToken cancellationToken)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));

            var url = "download/" + Uri.EscapeUriString(id.ToString());
            if (version != null)
                url += "/" + Uri.EscapeUriString(version.ToString());
            else
                url += "?latest";

            var request = new ApiRequest(this.Endpoint, url);
            var response = await this.transport.GetResponseAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                return response.GetResponseStream();
            }
            catch
            {
                response?.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Uploads the package in the specified stream to the feed.
        /// </summary>
        /// <param name="stream">Stream containg a universal package.</param>
        /// <param name="cancellationToken">Cancellation token for asynchronous operations.</param>
        /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
        public async Task UploadPackageAsync(Stream stream, CancellationToken cancellationToken)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var request = new ApiRequest(this.Endpoint, "upload", method: "PUT", contentType: "application/zip", requestBody: stream);

            using (var response = await this.transport.GetResponseAsync(request, cancellationToken).ConfigureAwait(false))
            {
            }
        }

        /// <summary>
        /// Deletes the specified package from the remote feed.
        /// </summary>
        /// <param name="id">Full name of the package.</param>
        /// <param name="version">Version of the package.</param>
        /// <param name="cancellationToken">Cancellation token for asynchronous operations.</param>
        /// <exception cref="ArgumentNullException"><paramref name="id"/> is null or <paramref name="version"/> is null.</exception>
        public async Task DeletePackageAsync(UniversalPackageId id, UniversalPackageVersion version, CancellationToken cancellationToken)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (version == null)
                throw new ArgumentNullException(nameof(version));

            var url = "delete/" + Uri.EscapeUriString(id.ToString()) + "/" + Uri.EscapeUriString(version.ToString());
            var request = new ApiRequest(this.Endpoint, url, method: "DELETE");

            using (var response = await this.transport.GetResponseAsync(request, cancellationToken).ConfigureAwait(false))
            {
            }
        }

        private async Task<IReadOnlyList<RemoteUniversalPackageVersion>> ListVersionsInternalAsync(UniversalPackageId id, UniversalPackageVersion version, bool includeFileList, int? maxCount, CancellationToken cancellationToken)
        {
            var url = FormatUrl("versions", ("group", id?.Group), ("name", id?.Name), ("version", version?.ToString()), ("includeFileList", includeFileList), ("count", maxCount));
            var request = new ApiRequest(this.Endpoint, url);
            using (var response = await this.transport.GetResponseAsync(request, cancellationToken).ConfigureAwait(false))
            {
                if (response.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
                    throw new InvalidDataException($"Server returned {response.ContentType} content type; expected application/json.");

                using (var responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream, AH.UTF8))
                using (var jsonReader = new JsonTextReader(reader))
                {
                    if (version == null)
                    {
                        var arr = await JArray.LoadAsync(jsonReader, cancellationToken).ConfigureAwait(false);
                        var results = new List<RemoteUniversalPackageVersion>(arr.Count);

                        foreach (var token in arr)
                        {
                            if (!(token is JObject obj))
                                throw new InvalidDataException("Unexpected token in JSON array.");

                            results.Add(new RemoteUniversalPackageVersion(obj));
                        }

                        return results.AsReadOnly();
                    }
                    else
                    {
                        var obj = await JObject.LoadAsync(jsonReader, cancellationToken).ConfigureAwait(false);
                        return new[] { new RemoteUniversalPackageVersion(obj) };
                    }
                }
            }
        }

        private static string FormatUrl(string url, params (string key, object value)[] query)
        {
            if (query.Length == 0)
                return url;

            var items = query.Where(i => !string.IsNullOrEmpty(i.value?.ToString())).ToList();
            if (items.Count == 0)
                return url;

            return url + "?" + string.Join("&", items.Select(i => Uri.EscapeDataString(i.key) + "=" + Uri.EscapeDataString(i.value.ToString())));
        }
    }
}
