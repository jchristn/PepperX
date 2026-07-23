namespace PepperX.Core.Storage.Format
{
    using System;

    /// <summary>
    /// Byte-level constants for the self-describing PepperX extent file format ("PXE1").
    /// Layout: magic(4) + version(2) + flags(2) + headerLength(4) + headerJson(H) + payload(N) + sha256(32) + closingMagic(4).
    /// All multi-byte integers are little-endian.
    /// </summary>
    public static class ExtentFormatConstants
    {
        #region Public-Members

        /// <summary>
        /// Leading magic identifying a PepperX extent file.
        /// </summary>
        public static ReadOnlySpan<byte> Magic => "PXE1"u8;

        /// <summary>
        /// Closing magic used to detect truncation.
        /// </summary>
        public static ReadOnlySpan<byte> ClosingMagic => "1EXP"u8;

        /// <summary>
        /// Current format version.
        /// </summary>
        public const ushort FormatVersion = 1;

        /// <summary>
        /// Flag bit indicating the extent carries a freeform metadata object.
        /// </summary>
        public const ushort FlagHasMetadataObject = 0x0001;

        /// <summary>
        /// Length in bytes of the fixed prefix (magic + version + flags + header length).
        /// </summary>
        public const int PrefixLength = 12;

        /// <summary>
        /// Byte offset of the 4-byte header-length field within the prefix.
        /// </summary>
        public const int HeaderLengthOffset = 8;

        /// <summary>
        /// Length in bytes of the payload SHA-256 stored in the trailer.
        /// </summary>
        public const int HashLength = 32;

        /// <summary>
        /// Length in bytes of the closing magic.
        /// </summary>
        public const int ClosingLength = 4;

        /// <summary>
        /// Total trailer length (hash + closing magic).
        /// </summary>
        public const int TrailerLength = HashLength + ClosingLength;

        #endregion
    }
}
