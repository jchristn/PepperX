namespace PepperX.Core.Database.Postgresql
{
    using System;
    using System.Collections.Generic;
    using Npgsql;
    using NpgsqlTypes;
    using PepperX.Core.Enums;
    using PepperX.Core.Models;
    using PepperX.Core.Serialization;

    /// <summary>
    /// Row-to-model mapping and parameter helpers for the PostgreSQL provider. This type is thread-safe.
    /// </summary>
    public static class Converters
    {
        #region Private-Members

        private static readonly PepperXSerializer _Serializer = new PepperXSerializer();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ensure a DateTime is expressed in UTC kind for a timestamptz parameter.
        /// </summary>
        /// <param name="value">Input timestamp.</param>
        /// <returns>The timestamp with UTC kind.</returns>
        public static DateTime AsUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        /// <summary>
        /// Create a jsonb parameter from a string dictionary.
        /// </summary>
        /// <param name="name">Parameter name.</param>
        /// <param name="value">Dictionary value.</param>
        /// <returns>An Npgsql jsonb parameter.</returns>
        public static NpgsqlParameter JsonbParam(string name, Dictionary<string, string> value)
        {
            string json = _Serializer.SerializeJson(value ?? new Dictionary<string, string>()) ?? "{}";
            return new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json };
        }

        /// <summary>
        /// Deserialize a jsonb string into a string dictionary.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>The dictionary, never null.</returns>
        public static Dictionary<string, string> DeserializeStringDict(string? json)
        {
            if (String.IsNullOrEmpty(json)) return new Dictionary<string, string>();
            return _Serializer.DeserializeJson<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Read a container row.
        /// </summary>
        /// <param name="reader">Open reader positioned on a row.</param>
        /// <returns>The container.</returns>
        public static Container ReadContainer(NpgsqlDataReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            return new Container
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Tags = DeserializeStringDict(reader.GetString(reader.GetOrdinal("tags"))),
                ObjectCount = reader.GetInt64(reader.GetOrdinal("object_count")),
                TotalBytes = reader.GetInt64(reader.GetOrdinal("total_bytes")),
                CreatedUtc = reader.GetDateTime(reader.GetOrdinal("created_utc")),
                LastUpdateUtc = reader.GetDateTime(reader.GetOrdinal("last_update_utc"))
            };
        }

        /// <summary>
        /// Read an extent row (without labels or tags, which are hydrated separately).
        /// </summary>
        /// <param name="reader">Open reader positioned on a row.</param>
        /// <returns>The extent.</returns>
        public static Extent ReadExtent(NpgsqlDataReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            int contentTypeOrdinal = reader.GetOrdinal("content_type");

            return new Extent
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                ContainerId = reader.GetString(reader.GetOrdinal("container_id")),
                Key = reader.GetString(reader.GetOrdinal("object_key")),
                State = Enum.Parse<ExtentStateEnum>(reader.GetString(reader.GetOrdinal("state"))),
                SizeBytes = reader.GetInt64(reader.GetOrdinal("size_bytes")),
                Sha256 = reader.GetString(reader.GetOrdinal("sha256")),
                ContentType = reader.IsDBNull(contentTypeOrdinal) ? null : reader.GetString(contentTypeOrdinal),
                StorageDriver = Enum.Parse<StorageDriverTypeEnum>(reader.GetString(reader.GetOrdinal("storage_driver"))),
                StorageLocation = reader.GetString(reader.GetOrdinal("storage_location")),
                HasMetadataObject = reader.GetBoolean(reader.GetOrdinal("has_metadata_object")),
                CreatedUtc = reader.GetDateTime(reader.GetOrdinal("created_utc")),
                LastUpdateUtc = reader.GetDateTime(reader.GetOrdinal("last_update_utc"))
            };
        }

        /// <summary>
        /// Read a node row.
        /// </summary>
        /// <param name="reader">Open reader positioned on a row.</param>
        /// <returns>The node record.</returns>
        public static NodeRecord ReadNode(NpgsqlDataReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            int hostnameOrdinal = reader.GetOrdinal("hostname");

            return new NodeRecord
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Hostname = reader.IsDBNull(hostnameOrdinal) ? null : reader.GetString(hostnameOrdinal),
                StartedUtc = reader.GetDateTime(reader.GetOrdinal("started_utc")),
                LastHeartbeatUtc = reader.GetDateTime(reader.GetOrdinal("last_heartbeat_utc")),
                CreatedUtc = reader.GetDateTime(reader.GetOrdinal("created_utc"))
            };
        }

        /// <summary>
        /// Read a request history row.
        /// </summary>
        /// <param name="reader">Open reader positioned on a row.</param>
        /// <param name="includeBodies">Whether the query selected the body and header columns.</param>
        /// <returns>The request history entry.</returns>
        public static RequestHistoryEntry ReadRequestHistory(NpgsqlDataReader reader, bool includeBodies)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));

            RequestHistoryEntry entry = new RequestHistoryEntry
            {
                Id = reader.GetString(reader.GetOrdinal("id")),
                Method = reader.GetString(reader.GetOrdinal("method")),
                Path = reader.GetString(reader.GetOrdinal("path")),
                Url = reader.GetString(reader.GetOrdinal("url")),
                StatusCode = reader.GetInt32(reader.GetOrdinal("status_code")),
                DurationMs = reader.GetDouble(reader.GetOrdinal("duration_ms")),
                RequestBodyBytes = reader.GetInt64(reader.GetOrdinal("request_body_bytes")),
                RequestBodyTruncated = reader.GetBoolean(reader.GetOrdinal("request_body_truncated")),
                ResponseBodyBytes = reader.GetInt64(reader.GetOrdinal("response_body_bytes")),
                ResponseBodyTruncated = reader.GetBoolean(reader.GetOrdinal("response_body_truncated")),
                CreatedUtc = reader.GetDateTime(reader.GetOrdinal("created_utc"))
            };

            int sourceIpOrdinal = reader.GetOrdinal("source_ip");
            entry.SourceIp = reader.IsDBNull(sourceIpOrdinal) ? null : reader.GetString(sourceIpOrdinal);

            int completedOrdinal = reader.GetOrdinal("completed_utc");
            entry.CompletedUtc = reader.IsDBNull(completedOrdinal) ? (DateTime?)null : reader.GetDateTime(completedOrdinal);

            if (includeBodies)
            {
                entry.RequestHeaders = DeserializeStringDict(reader.GetString(reader.GetOrdinal("request_headers")));
                entry.ResponseHeaders = DeserializeStringDict(reader.GetString(reader.GetOrdinal("response_headers")));

                int reqBodyOrdinal = reader.GetOrdinal("request_body");
                entry.RequestBody = reader.IsDBNull(reqBodyOrdinal) ? null : reader.GetString(reqBodyOrdinal);

                int respBodyOrdinal = reader.GetOrdinal("response_body");
                entry.ResponseBody = reader.IsDBNull(respBodyOrdinal) ? null : reader.GetString(respBodyOrdinal);
            }

            return entry;
        }

        /// <summary>
        /// Escape a value for use in a LIKE pattern (escaping backslash, percent, and underscore).
        /// </summary>
        /// <param name="value">Value to escape.</param>
        /// <returns>The escaped value.</returns>
        public static string EscapeLike(string value)
        {
            if (String.IsNullOrEmpty(value)) return value ?? String.Empty;
            return value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        #endregion
    }
}
