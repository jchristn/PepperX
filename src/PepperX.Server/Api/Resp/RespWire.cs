namespace PepperX.Server.Api.Resp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Builds RESP2/RESP3 wire responses as raw byte arrays. Framing is ASCII; values are written as raw
    /// bytes so binary payloads are preserved. Latin1 is used to map value strings back to their original
    /// bytes losslessly.
    /// </summary>
    public static class RespWire
    {
        #region Public-Methods

        /// <summary>
        /// Encode a value string to its original bytes (Latin1 is a lossless byte mapping).
        /// </summary>
        /// <param name="value">Value string as delivered by the parser.</param>
        /// <returns>The original bytes.</returns>
        public static byte[] ToBytes(string value)
        {
            return Encoding.Latin1.GetBytes(value ?? string.Empty);
        }

        /// <summary>
        /// A RESP simple string (<c>+OK\r\n</c>).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] SimpleString(string value)
        {
            return Ascii("+" + value + "\r\n");
        }

        /// <summary>
        /// A RESP error (<c>-ERR message\r\n</c>).
        /// </summary>
        /// <param name="message">Error message.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] Error(string message)
        {
            return Ascii("-" + message + "\r\n");
        }

        /// <summary>
        /// A RESP integer.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] Integer(long value)
        {
            return Ascii(":" + value + "\r\n");
        }

        /// <summary>
        /// A RESP bulk string from raw bytes.
        /// </summary>
        /// <param name="data">Payload bytes.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] BulkString(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] prefix = Ascii("$" + data.Length + "\r\n");
                ms.Write(prefix, 0, prefix.Length);
                ms.Write(data, 0, data.Length);
                ms.Write(_Crlf, 0, _Crlf.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// A RESP bulk string from text.
        /// </summary>
        /// <param name="value">Text value.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] BulkString(string value)
        {
            return BulkString(ToBytes(value));
        }

        /// <summary>
        /// A RESP null (RESP2 <c>$-1</c> or RESP3 <c>_</c>).
        /// </summary>
        /// <param name="resp3">Whether the connection is RESP3.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] Null(bool resp3)
        {
            return resp3 ? Ascii("_\r\n") : Ascii("$-1\r\n");
        }

        /// <summary>
        /// A RESP array from pre-encoded element byte arrays.
        /// </summary>
        /// <param name="elements">Encoded elements.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] Array(IReadOnlyList<byte[]> elements)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] header = Ascii("*" + elements.Count + "\r\n");
                ms.Write(header, 0, header.Length);
                foreach (byte[] element in elements) ms.Write(element, 0, element.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// A RESP null array (RESP2 <c>*-1</c>).
        /// </summary>
        /// <returns>Wire bytes.</returns>
        public static byte[] NullArray()
        {
            return Ascii("*-1\r\n");
        }

        /// <summary>
        /// A RESP map (RESP3 <c>%</c>) or, in RESP2, a flat array of key/value pairs.
        /// </summary>
        /// <param name="pairs">Key/value pairs, already encoded.</param>
        /// <param name="resp3">Whether the connection is RESP3.</param>
        /// <returns>Wire bytes.</returns>
        public static byte[] Map(IReadOnlyList<byte[]> pairs, bool resp3)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] header = resp3 ? Ascii("%" + (pairs.Count / 2) + "\r\n") : Ascii("*" + pairs.Count + "\r\n");
                ms.Write(header, 0, header.Length);
                foreach (byte[] element in pairs) ms.Write(element, 0, element.Length);
                return ms.ToArray();
            }
        }

        #endregion

        #region Private-Members

        private static readonly byte[] _Crlf = new byte[] { 13, 10 };

        #endregion

        #region Private-Methods

        private static byte[] Ascii(string text)
        {
            return Encoding.ASCII.GetBytes(text);
        }

        #endregion
    }
}
