using PuppeteerSharp.Helpers.Json;
using PuppeteerSharp.Messaging;
using System;
using System.Threading.Tasks;

namespace PuppeteerSharp
{
    /// <summary>
    /// The Worker class represents a WebWorker (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API"/>).
    /// The events <see cref="Page.WorkerCreated"/> and <see cref="Page.WorkerDestroyed"/> are emitted on the page object to signal the worker lifecycle.
    /// </summary>
    /// <example>
    /// <code>
    /// <![CDATA[
    /// page.WorkerCreated += (sender, e) => Console.WriteLine('Worker created: ' + e.Worker.Url);
    /// page.WorkerDestroyed += (sender, e) => Console.WriteLine('Worker destroyed: ' + e.Worker.Url);
    /// for (var worker of page.Workers)
    /// {
    ///     Console.WriteLine('  ' + worker.Url);
    /// }
    /// ]]>
    /// </code>
    /// </example>
    public class Worker
    {
        private readonly CDPSession _client;
        private ExecutionContext _executionContext;
        // private readonly Func<ConsoleType, JSHandle[], StackTrace, Task> _consoleAPICalled;
        private readonly Action<EvaluateExceptionResponseDetails> _exceptionThrown;
        private readonly TaskCompletionSource<ExecutionContext> _executionContextCallback;
        private Func<ExecutionContext, RemoteObject, JSHandle> _jsHandleFactory;

        internal Worker(
            CDPSession client,
            string url,
            Func<ConsoleType, JSHandle[], Task> consoleAPICalled,
            Action<EvaluateExceptionResponseDetails> exceptionThrown)
        {
            _client = client;
            Url = url;
            _exceptionThrown = exceptionThrown;
            _client.MessageReceived += OnMessageReceived;

            _executionContextCallback = new TaskCompletionSource<ExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = _client.SendAsync("Runtime.enable").ContinueWith(task =>
            {

            });
            _ = _client.SendAsync("Log.enable").ContinueWith(task =>
            {

            });
        }

        /// <summary>
        /// Gets the Worker URL.
        /// </summary>
        /// <value>Worker URL.</value>
        public string Url { get; }

        internal void OnMessageReceived(object sender, MessageEventArgs e)
        {
            try
            {
                switch (e.MessageID)
                {
                    case "Runtime.executionContextCreated":
                        OnExecutionContextCreated(e.MessageData.ToObject<RuntimeExecutionContextCreatedResponse>(true));
                        break;
                    //case "Runtime.consoleAPICalled":
                    //    await OnConsoleAPICalled(e).ConfigureAwait(false);
                    //    break;
                    case "Runtime.exceptionThrown":
                        OnExceptionThrown(e.MessageData.ToObject<RuntimeExceptionThrownResponse>(true));
                        break;
                }
            }
            catch (Exception ex)
            {
                var message = $"Worker failed to process {e.MessageID}. {ex.Message}. {ex.StackTrace}";
                _client.Close(message);
            }
        }

        private void OnExceptionThrown(RuntimeExceptionThrownResponse e) => _exceptionThrown(e.ExceptionDetails);

        private void OnExecutionContextCreated(RuntimeExecutionContextCreatedResponse e)
        {
            if (_jsHandleFactory == null)
            {
                _jsHandleFactory = (ctx, remoteObject) => new JSHandle(ctx, _client, remoteObject);
                _executionContext = new ExecutionContext(
                    _client,
                    e.Context,
                    null);
                _executionContextCallback.TrySetResult(_executionContext);
            }
        }
    }
}