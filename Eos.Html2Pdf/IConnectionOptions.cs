using PuppeteerSharp.Transport;
using System;

namespace PuppeteerSharp
{
    /// <summary>
    /// Options for <see cref="Connection"/> creation.
    /// </summary>
    public interface IConnectionOptions
    {
        /// <summary>
        /// Slows down Puppeteer operations by the specified amount of milliseconds. Useful so that you can see what is going on.
        /// </summary>
        int SlowMo { get; set; }

        /// <summary>
        /// Optional factory for <see cref="WebSocket"/> implementations.
        /// If <see cref="Transport"/> is set this property will be ignored.
        /// </summary>
        WebSocketFactory WebSocketFactory { get; set; }

        /// <summary>
        /// Optional connection transport factory.
        /// </summary>
        [Obsolete("Use " + nameof(TransportFactory) + " instead")]
        IConnectionTransport Transport { get; set; }

        /// <summary>
        /// Optional factory for <see cref="IConnectionTransport"/> implementations.
        /// </summary>
        TransportFactory TransportFactory { get; set; }

        /// <summary>
        /// If not <see cref="Transport"/> is set this will be use to determine is the default <see cref="WebSocketTransport"/> will enqueue messages.
        /// </summary>
        bool EnqueueTransportMessages { get; set; }
    }
}