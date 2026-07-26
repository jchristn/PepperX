namespace PepperX.Core.Database.Postgresql
{
    using System.Collections.Generic;

    /// <summary>
    /// The ordered set of PostgreSQL schema migrations for PepperX.
    /// </summary>
    public static class PostgresqlMigrations
    {
        #region Public-Methods

        /// <summary>
        /// Return all migrations in ascending version order.
        /// </summary>
        /// <returns>Migrations.</returns>
        public static IReadOnlyList<SchemaMigration> All()
        {
            return new List<SchemaMigration>
            {
                new SchemaMigration(1, "Initial schema", new List<string>
                {
                    @"CREATE TABLE IF NOT EXISTS containers (
                        id varchar(64) PRIMARY KEY,
                        name varchar(255) NOT NULL UNIQUE,
                        tags jsonb NOT NULL DEFAULT '{}',
                        object_count bigint NOT NULL DEFAULT 0,
                        total_bytes bigint NOT NULL DEFAULT 0,
                        created_utc timestamptz NOT NULL DEFAULT now(),
                        last_update_utc timestamptz NOT NULL DEFAULT now()
                    );",

                    @"CREATE TABLE IF NOT EXISTS extents (
                        id varchar(64) PRIMARY KEY,
                        container_id varchar(64) NOT NULL REFERENCES containers(id),
                        object_key varchar(1024) NOT NULL,
                        state varchar(16) NOT NULL,
                        size_bytes bigint NOT NULL,
                        sha256 varchar(64) NOT NULL,
                        content_type varchar(255),
                        storage_driver varchar(32) NOT NULL,
                        storage_location text NOT NULL,
                        has_metadata_object boolean NOT NULL DEFAULT false,
                        created_utc timestamptz NOT NULL DEFAULT now(),
                        last_update_utc timestamptz NOT NULL DEFAULT now()
                    );",

                    @"CREATE UNIQUE INDEX IF NOT EXISTS ux_extents_active_key ON extents (container_id, object_key) WHERE state = 'Active';",
                    @"CREATE INDEX IF NOT EXISTS ix_extents_container_state_created ON extents (container_id, state, created_utc DESC);",
                    @"CREATE INDEX IF NOT EXISTS ix_extents_container_state_key ON extents (container_id, state, object_key text_pattern_ops);",
                    @"CREATE INDEX IF NOT EXISTS ix_extents_state ON extents (state);",

                    @"CREATE TABLE IF NOT EXISTS extent_labels (
                        id bigserial PRIMARY KEY,
                        extent_id varchar(64) NOT NULL REFERENCES extents(id) ON DELETE CASCADE,
                        container_id varchar(64) NOT NULL,
                        label varchar(512) NOT NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS ix_extent_labels_lookup ON extent_labels (container_id, label, extent_id);",
                    @"CREATE INDEX IF NOT EXISTS ix_extent_labels_extent ON extent_labels (extent_id);",
                    @"CREATE INDEX IF NOT EXISTS ix_extent_labels_lower ON extent_labels (container_id, lower(label), extent_id);",

                    @"CREATE TABLE IF NOT EXISTS extent_tags (
                        id bigserial PRIMARY KEY,
                        extent_id varchar(64) NOT NULL REFERENCES extents(id) ON DELETE CASCADE,
                        container_id varchar(64) NOT NULL,
                        tag_key varchar(512) NOT NULL,
                        tag_value text NOT NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS ix_extent_tags_lookup ON extent_tags (container_id, tag_key, tag_value, extent_id);",
                    @"CREATE INDEX IF NOT EXISTS ix_extent_tags_extent ON extent_tags (extent_id);",
                    @"CREATE INDEX IF NOT EXISTS ix_extent_tags_lower ON extent_tags (container_id, lower(tag_key), lower(tag_value), extent_id);",

                    @"CREATE TABLE IF NOT EXISTS extent_read_leases (
                        id varchar(64) PRIMARY KEY,
                        extent_id varchar(64) NOT NULL,
                        node_id varchar(64) NOT NULL,
                        acquired_utc timestamptz NOT NULL DEFAULT now(),
                        expires_utc timestamptz NOT NULL
                    );",
                    @"CREATE INDEX IF NOT EXISTS ix_leases_extent_expires ON extent_read_leases (extent_id, expires_utc);",
                    @"CREATE INDEX IF NOT EXISTS ix_leases_node ON extent_read_leases (node_id);",

                    @"CREATE TABLE IF NOT EXISTS nodes (
                        id varchar(64) PRIMARY KEY,
                        hostname text,
                        started_utc timestamptz NOT NULL DEFAULT now(),
                        last_heartbeat_utc timestamptz NOT NULL DEFAULT now(),
                        created_utc timestamptz NOT NULL DEFAULT now()
                    );",
                    @"CREATE INDEX IF NOT EXISTS ix_nodes_heartbeat ON nodes (last_heartbeat_utc);",

                    @"CREATE TABLE IF NOT EXISTS request_history (
                        id varchar(64) PRIMARY KEY,
                        method varchar(16) NOT NULL,
                        path text NOT NULL,
                        url text NOT NULL,
                        status_code int NOT NULL,
                        duration_ms double precision NOT NULL,
                        source_ip varchar(64),
                        request_headers jsonb NOT NULL DEFAULT '{}',
                        request_body text,
                        request_body_bytes bigint NOT NULL DEFAULT 0,
                        request_body_truncated boolean NOT NULL DEFAULT false,
                        response_headers jsonb NOT NULL DEFAULT '{}',
                        response_body text,
                        response_body_bytes bigint NOT NULL DEFAULT 0,
                        response_body_truncated boolean NOT NULL DEFAULT false,
                        created_utc timestamptz NOT NULL DEFAULT now(),
                        completed_utc timestamptz
                    );",
                    @"CREATE INDEX IF NOT EXISTS ix_reqhist_created ON request_history (created_utc DESC);",
                    @"CREATE INDEX IF NOT EXISTS ix_reqhist_status ON request_history (status_code);",
                    @"CREATE INDEX IF NOT EXISTS ix_reqhist_method ON request_history (method);"
                }),

                new SchemaMigration(2, "Per-container cache settings", new List<string>
                {
                    @"ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_enabled boolean NOT NULL DEFAULT false;",
                    @"ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_policy varchar(8) NOT NULL DEFAULT 'LRU';",
                    @"ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_max_objects integer NOT NULL DEFAULT 1000;",
                    @"ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_max_memory_bytes bigint NOT NULL DEFAULT 0;",
                    @"ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_evict_count integer NOT NULL DEFAULT 10;",
                    @"ALTER TABLE containers ADD COLUMN IF NOT EXISTS cache_max_object_bytes bigint NOT NULL DEFAULT 1048576;"
                }),

                new SchemaMigration(3, "Per-container RESP database index", new List<string>
                {
                    @"ALTER TABLE containers ADD COLUMN IF NOT EXISTS resp_database_index integer;",
                    // A container may claim at most one RESP database index, and an index maps to at most one
                    // container: a partial unique index enforces both, leaving unmapped containers (NULL) free.
                    @"CREATE UNIQUE INDEX IF NOT EXISTS ux_containers_resp_db_index ON containers (resp_database_index) WHERE resp_database_index IS NOT NULL;"
                })
            };
        }

        #endregion
    }
}
