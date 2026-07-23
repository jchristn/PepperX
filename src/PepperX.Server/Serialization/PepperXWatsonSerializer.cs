namespace PepperX.Server.Serialization
{
    using System;
    using PepperX.Core.Serialization;
    using WatsonWebserver.Core;

    /// <summary>
    /// Adapts the PepperX core serializer to the Watson web server serialization interface so that request
    /// and response bodies use the same JSON conventions (strict enums, null omission) as the rest of the
    /// system.
    /// </summary>
    public class PepperXWatsonSerializer : ISerializationHelper
    {
        #region Private-Members

        private readonly PepperXSerializer _Serializer = new PepperXSerializer();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the serializer adapter.
        /// </summary>
        public PepperXWatsonSerializer()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Deserialize JSON into an instance of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>The deserialized instance, or default when the input is empty.</returns>
        public T DeserializeJson<T>(string json)
        {
            if (String.IsNullOrEmpty(json)) return default!;
            return _Serializer.DeserializeJson<T>(json);
        }

        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <param name="obj">Object to serialize.</param>
        /// <param name="pretty">Whether to indent the output.</param>
        /// <returns>JSON text, or null when the object is null.</returns>
        public string SerializeJson(object obj, bool pretty = true)
        {
            if (obj == null) return null!;
            return _Serializer.SerializeJson(obj, pretty) ?? "null";
        }

        #endregion
    }
}
