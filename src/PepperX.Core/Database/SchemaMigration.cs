namespace PepperX.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A versioned, ordered, idempotent schema migration. Migrations are tracked in a
    /// <c>schema_migrations</c> table so they run at most once per database.
    /// </summary>
    public class SchemaMigration
    {
        #region Public-Members

        /// <summary>
        /// Monotonic migration version. Must be greater than zero and unique.
        /// </summary>
        public int Version
        {
            get
            {
                return _Version;
            }
            set
            {
                if (value < 1) throw new ArgumentOutOfRangeException(nameof(Version));
                _Version = value;
            }
        }

        /// <summary>
        /// Human-readable description.
        /// </summary>
        public string Description
        {
            get
            {
                return _Description;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Description));
                _Description = value;
            }
        }

        /// <summary>
        /// Ordered SQL statements applied for this migration. Never null.
        /// </summary>
        public List<string> Statements
        {
            get
            {
                return _Statements;
            }
            set
            {
                _Statements = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private int _Version = 1;
        private string _Description = String.Empty;
        private List<string> _Statements = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a schema migration.
        /// </summary>
        /// <param name="version">Migration version (greater than zero).</param>
        /// <param name="description">Description.</param>
        /// <param name="statements">Ordered SQL statements.</param>
        public SchemaMigration(int version, string description, List<string> statements)
        {
            Version = version;
            Description = description;
            Statements = statements;
        }

        #endregion
    }
}
