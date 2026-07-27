namespace PepperX.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using PepperX.Core.Database.Interfaces;
    using PepperX.Core.Models;
    using PepperX.Core.Responses;

    /// <summary>
    /// PostgreSQL implementation of multipart upload data access. Upload rows are transient; part rows are
    /// cascade-deleted with their upload. Parameterized SQL throughout.
    /// </summary>
    public sealed class PostgresqlMultipartMethods : IMultipartMethods
    {
        #region Private-Members

        private const string _UploadColumns = "id, container_id, object_key, content_type, tags, initiated_utc, expires_utc";
        private const string _PartColumns = "id, upload_id, part_number, size_bytes, md5, sha256, storage_location, created_utc";
        private readonly NpgsqlDataSource _DataSource;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate multipart methods.
        /// </summary>
        /// <param name="dataSource">Data source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="dataSource"/> is null.</exception>
        public PostgresqlMultipartMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<MultipartUpload> CreateUploadAsync(MultipartUpload upload, CancellationToken token = default)
        {
            if (upload == null) throw new ArgumentNullException(nameof(upload));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "INSERT INTO multipart_uploads (id, container_id, object_key, content_type, tags, initiated_utc, expires_utc) " +
                "VALUES (@id, @cid, @key, @ct, @tags, @iu, @eu);"))
            {
                cmd.Parameters.AddWithValue("id", upload.Id);
                cmd.Parameters.AddWithValue("cid", upload.ContainerId);
                cmd.Parameters.AddWithValue("key", upload.Key);
                cmd.Parameters.AddWithValue("ct", (object?)upload.ContentType ?? DBNull.Value);
                cmd.Parameters.Add(Converters.JsonbParam("tags", upload.Tags));
                cmd.Parameters.AddWithValue("iu", Converters.AsUtc(upload.InitiatedUtc));
                cmd.Parameters.AddWithValue("eu", Converters.AsUtc(upload.ExpiresUtc));
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            return upload;
        }

        /// <inheritdoc />
        public async Task<MultipartUpload?> ReadUploadAsync(string uploadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT " + _UploadColumns + " FROM multipart_uploads WHERE id = @id LIMIT 1;"))
            {
                cmd.Parameters.AddWithValue("id", uploadId);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false)) return Converters.ReadMultipartUpload(reader);
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<MultipartUploadListResult> ListUploadsAsync(string containerId, string? keyMarker, string? uploadIdMarker, int maxUploads, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));
            if (maxUploads < 1) maxUploads = 1;

            // Fetch one extra row to determine truncation without a second COUNT query.
            List<MultipartUpload> uploads = new List<MultipartUpload>();
            string sql = "SELECT " + _UploadColumns + " FROM multipart_uploads WHERE container_id = @cid";
            bool hasMarker = !String.IsNullOrEmpty(keyMarker);
            if (hasMarker)
            {
                // Keyset pagination on (object_key, id) strictly greater than the marker pair.
                sql += " AND (object_key > @km OR (object_key = @km AND id > @um))";
            }
            sql += " ORDER BY object_key ASC, id ASC LIMIT @limit;";

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(sql))
            {
                cmd.Parameters.AddWithValue("cid", containerId);
                if (hasMarker)
                {
                    cmd.Parameters.AddWithValue("km", keyMarker!);
                    cmd.Parameters.AddWithValue("um", (object?)uploadIdMarker ?? String.Empty);
                }
                cmd.Parameters.AddWithValue("limit", maxUploads + 1);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) uploads.Add(Converters.ReadMultipartUpload(reader));
                }
            }

            MultipartUploadListResult result = new MultipartUploadListResult();
            if (uploads.Count > maxUploads)
            {
                uploads.RemoveAt(uploads.Count - 1);
                result.IsTruncated = true;
                if (uploads.Count > 0)
                {
                    MultipartUpload last = uploads[uploads.Count - 1];
                    result.NextKeyMarker = last.Key;
                    result.NextUploadIdMarker = last.Id;
                }
            }

            result.Uploads = uploads;
            return result;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteUploadAsync(string uploadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("DELETE FROM multipart_uploads WHERE id = @id;"))
            {
                cmd.Parameters.AddWithValue("id", uploadId);
                int rows = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return rows > 0;
            }
        }

        /// <inheritdoc />
        public async Task<string?> UpsertPartAsync(MultipartPart part, CancellationToken token = default)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));

            await using (NpgsqlConnection connection = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            await using (NpgsqlTransaction tx = await connection.BeginTransactionAsync(token).ConfigureAwait(false))
            {
                string? priorLocation = null;
                await using (NpgsqlCommand read = new NpgsqlCommand(
                    "SELECT storage_location FROM multipart_parts WHERE upload_id = @uid AND part_number = @pn FOR UPDATE;", connection, tx))
                {
                    read.Parameters.AddWithValue("uid", part.UploadId);
                    read.Parameters.AddWithValue("pn", part.PartNumber);
                    object? existing = await read.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (existing != null && existing != DBNull.Value) priorLocation = (string)existing;
                }

                await using (NpgsqlCommand upsert = new NpgsqlCommand(
                    "INSERT INTO multipart_parts (id, upload_id, part_number, size_bytes, md5, sha256, storage_location, created_utc) " +
                    "VALUES (@id, @uid, @pn, @size, @md5, @sha, @loc, @cu) " +
                    "ON CONFLICT (upload_id, part_number) DO UPDATE SET " +
                    "id = EXCLUDED.id, size_bytes = EXCLUDED.size_bytes, md5 = EXCLUDED.md5, sha256 = EXCLUDED.sha256, " +
                    "storage_location = EXCLUDED.storage_location, created_utc = EXCLUDED.created_utc;", connection, tx))
                {
                    upsert.Parameters.AddWithValue("id", part.Id);
                    upsert.Parameters.AddWithValue("uid", part.UploadId);
                    upsert.Parameters.AddWithValue("pn", part.PartNumber);
                    upsert.Parameters.AddWithValue("size", part.SizeBytes);
                    upsert.Parameters.AddWithValue("md5", part.Md5);
                    upsert.Parameters.AddWithValue("sha", part.Sha256);
                    upsert.Parameters.AddWithValue("loc", part.StorageLocation);
                    upsert.Parameters.AddWithValue("cu", Converters.AsUtc(part.CreatedUtc));
                    await upsert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                await tx.CommitAsync(token).ConfigureAwait(false);

                // Only report a superseded location when the new staged blob is at a different location.
                if (priorLocation != null && !String.Equals(priorLocation, part.StorageLocation, StringComparison.Ordinal)) return priorLocation;
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<MultipartPart>> ListAllPartsAsync(string uploadId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

            List<MultipartPart> parts = new List<MultipartPart>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT " + _PartColumns + " FROM multipart_parts WHERE upload_id = @uid ORDER BY part_number ASC;"))
            {
                cmd.Parameters.AddWithValue("uid", uploadId);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) parts.Add(Converters.ReadMultipartPart(reader));
                }
            }

            return parts;
        }

        /// <inheritdoc />
        public async Task<MultipartPart?> ReadPartAsync(string uploadId, int partNumber, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT " + _PartColumns + " FROM multipart_parts WHERE upload_id = @uid AND part_number = @pn LIMIT 1;"))
            {
                cmd.Parameters.AddWithValue("uid", uploadId);
                cmd.Parameters.AddWithValue("pn", partNumber);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    if (await reader.ReadAsync(token).ConfigureAwait(false)) return Converters.ReadMultipartPart(reader);
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<string?> DeletePartAsync(string uploadId, int partNumber, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));

            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "DELETE FROM multipart_parts WHERE upload_id = @uid AND part_number = @pn RETURNING storage_location;"))
            {
                cmd.Parameters.AddWithValue("uid", uploadId);
                cmd.Parameters.AddWithValue("pn", partNumber);
                object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                return result == null || result == DBNull.Value ? null : (string)result;
            }
        }

        /// <inheritdoc />
        public async Task<MultipartPartListResult> ListPartsAsync(string uploadId, int partNumberMarker, int maxParts, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(uploadId)) throw new ArgumentNullException(nameof(uploadId));
            if (maxParts < 1) maxParts = 1;
            if (partNumberMarker < 0) partNumberMarker = 0;

            List<MultipartPart> parts = new List<MultipartPart>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "SELECT " + _PartColumns + " FROM multipart_parts WHERE upload_id = @uid AND part_number > @marker ORDER BY part_number ASC LIMIT @limit;"))
            {
                cmd.Parameters.AddWithValue("uid", uploadId);
                cmd.Parameters.AddWithValue("marker", partNumberMarker);
                cmd.Parameters.AddWithValue("limit", maxParts + 1);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) parts.Add(Converters.ReadMultipartPart(reader));
                }
            }

            MultipartPartListResult result = new MultipartPartListResult();
            if (parts.Count > maxParts)
            {
                parts.RemoveAt(parts.Count - 1);
                result.IsTruncated = true;
                if (parts.Count > 0) result.NextPartNumberMarker = parts[parts.Count - 1].PartNumber;
            }

            result.Parts = parts;
            return result;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> PurgeExpiredAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            List<string> purged = new List<string>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "DELETE FROM multipart_uploads WHERE expires_utc <= @cutoff RETURNING id;"))
            {
                cmd.Parameters.AddWithValue("cutoff", Converters.AsUtc(olderThanUtc));
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) purged.Add(reader.GetString(0));
                }
            }

            return purged;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> DeleteByContainerAsync(string containerId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(containerId)) throw new ArgumentNullException(nameof(containerId));

            List<string> deleted = new List<string>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand(
                "DELETE FROM multipart_uploads WHERE container_id = @cid RETURNING id;"))
            {
                cmd.Parameters.AddWithValue("cid", containerId);
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) deleted.Add(reader.GetString(0));
                }
            }

            return deleted;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> ListActiveUploadIdsAsync(CancellationToken token = default)
        {
            List<string> ids = new List<string>();
            await using (NpgsqlCommand cmd = _DataSource.CreateCommand("SELECT id FROM multipart_uploads;"))
            {
                await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false)) ids.Add(reader.GetString(0));
                }
            }

            return ids;
        }

        #endregion
    }
}
