namespace PepperX.Core.Serialization
{
    using System;
    using SerializationHelper;

    /// <summary>
    /// JSON serializer for PepperX. Wraps <see cref="Serializer"/> from SerializationHelper, which applies
    /// strict enum handling and null-ignoring writes. This type is protocol-agnostic; the Watson adapter that
    /// implements the web server's serialization interface lives in the server project and delegates here.
    /// This type is thread-safe.
    /// </summary>
    public class PepperXSerializer
    {
        #region Private-Members

        private readonly Serializer _Serializer = new Serializer();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the serializer.
        /// </summary>
        public PepperXSerializer()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        /// <param name="pretty">Whether to indent the output. Default false.</param>
        /// <returns>JSON string. Null when <paramref name="obj"/> is null.</returns>
        public string? SerializeJson(object? obj, bool pretty = false)
        {
            if (obj == null) return null;
            return _Serializer.SerializeJson(obj, pretty);
        }

        /// <summary>
        /// Deserialize JSON to an instance of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON string.</param>
        /// <returns>Deserialized instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null or empty.</exception>
        public T DeserializeJson<T>(string json)
        {
            if (String.IsNullOrEmpty(json)) throw new ArgumentNullException(nameof(json));
            return _Serializer.DeserializeJson<T>(json);
        }

        /// <summary>
        /// Deserialize JSON bytes to an instance of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON bytes.</param>
        /// <returns>Deserialized instance.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        public T DeserializeJson<T>(byte[] json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return _Serializer.DeserializeJson<T>(json);
        }

        /// <summary>
        /// Deep-copy an object by serializing and deserializing it.
        /// </summary>
        /// <typeparam name="T">Type to copy.</typeparam>
        /// <param name="obj">Object to copy.</param>
        /// <returns>A deep copy.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="obj"/> is null.</exception>
        public T CopyObject<T>(T obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            return _Serializer.CopyObject<T>(obj);
        }

        #endregion
    }
}
