using PuppeteerSharp.Helpers;
using PuppeteerSharp.Helpers.Json;
using PuppeteerSharp.Input;
using PuppeteerSharp.Media;
using PuppeteerSharp.Messaging;
using PuppeteerSharp.PageAccessibility;
using PuppeteerSharp.PageCoverage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace PuppeteerSharp
{
    /// <summary>
    /// Provides methods to interact with a single tab in Chromium. One <see cref="Browser"/> instance might have multiple <see cref="Page"/> instances.
    /// </summary>
    /// <example>
    /// This example creates a page, navigates it to a URL, and then saves a screenshot:
    /// <code>
    /// var browser = await Puppeteer.LaunchAsync(new LaunchOptions());
    /// var page = await browser.NewPageAsync();
    /// await page.GoToAsync("https://example.com");
    /// await page.ScreenshotAsync("screenshot.png");
    /// await browser.CloseAsync();
    /// </code>
    /// </example>
    [DebuggerDisplay("Page {Url}")]
    public class Page : IDisposable
    {
        private readonly TaskQueue _screenshotTaskQueue;
        private readonly EmulationManager _emulationManager;
        private readonly Dictionary<string, Delegate> _pageBindings;
        //private readonly Dictionary<string, Worker> _workers;
        private PageGetLayoutMetricsResponse _burstModeMetrics;
        private bool _screenshotBurstModeOn;
        private ScreenshotOptions _screenshotBurstModeOptions;
        private readonly TaskCompletionSource<bool> _closeCompletedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _sessionClosedTcs;
        private readonly TimeoutSettings _timeoutSettings;
        private bool _fileChooserInterceptionIsDisabled;
        //private ConcurrentDictionary<Guid, TaskCompletionSource<FileChooser>> _fileChooserInterceptors;

        private static readonly Dictionary<string, decimal> _unitToPixels = new Dictionary<string, decimal> {
            {"px", 1},
            {"in", 96},
            {"cm", 37.8m},
            {"mm", 3.78m}
        };

        private Page(
            CDPSession client,
            Target target,
            TaskQueue screenshotTaskQueue)
        {
            Client = client;
            Target = target;
            Keyboard = new Keyboard(client);
            Mouse = new Mouse(client, Keyboard);
            Touchscreen = new Touchscreen(client, Keyboard);
            Tracing = new Tracing(client);
            Coverage = new Coverage(client);

            //_fileChooserInterceptors = new ConcurrentDictionary<Guid, TaskCompletionSource<FileChooser>>();
            _timeoutSettings = new TimeoutSettings();
            _emulationManager = new EmulationManager(client);
            _pageBindings = new Dictionary<string, Delegate>();
            //_workers = new Dictionary<string, Worker>();
            Accessibility = new Accessibility(client);

            _screenshotTaskQueue = screenshotTaskQueue;

            _ = target.CloseTask.ContinueWith((arg) =>
            {
                try
                {
                    Close?.Invoke(this, EventArgs.Empty);
                }
                finally
                {
                    IsClosed = true;
                    _closeCompletedTcs.TrySetResult(true);
                }
            });
        }

        #region Public Properties

        /// <summary>
        /// Chrome DevTools Protocol session.
        /// </summary>
        public CDPSession Client { get; }

        /// <summary>
        /// Raised when the JavaScript <c>load</c> <see href="https://developer.mozilla.org/en-US/docs/Web/Events/load"/> event is dispatched.
        /// </summary>
        public event EventHandler Load;

        /// <summary>
        /// Raised when the page crashes
        /// </summary>
        public event EventHandler<ErrorEventArgs> Error;

        /// <summary>
        /// Raised when the JavaScript code makes a call to <c>console.timeStamp</c>. For the list of metrics see <see cref="MetricsAsync"/>.
        /// </summary>
        public event EventHandler<MetricEventArgs> Metrics;

        /// <summary>
        /// Raised when a JavaScript dialog appears, such as <c>alert</c>, <c>prompt</c>, <c>confirm</c> or <c>beforeunload</c>. Puppeteer can respond to the dialog via <see cref="Dialog"/>'s <see cref="Dialog.Accept(string)"/> or <see cref="Dialog.Dismiss"/> methods.
        /// </summary>
        public event EventHandler<DialogEventArgs> Dialog;

        /// <summary>
        /// Raised when the JavaScript <c>DOMContentLoaded</c> <see href="https://developer.mozilla.org/en-US/docs/Web/Events/DOMContentLoaded"/> event is dispatched.
        /// </summary>
        public event EventHandler DOMContentLoaded;

        /// <summary>
        /// Raised when JavaScript within the page calls one of console API methods, e.g. <c>console.log</c> or <c>console.dir</c>. Also emitted if the page throws an error or a warning.
        /// The arguments passed into <c>console.log</c> appear as arguments on the event handler.
        /// </summary>
        /// <example>
        /// An example of handling <see cref="Console"/> event:
        /// <code>
        /// <![CDATA[
        /// page.Console += (sender, e) => 
        /// {
        ///     for (var i = 0; i < e.Message.Args.Count; ++i)
        ///     {
        ///         System.Console.WriteLine($"{i}: {e.Message.Args[i]}");
        ///     }
        /// }
        /// ]]>
        /// </code>
        /// </example>
        //public event EventHandler<ConsoleEventArgs> Console;

        /// <summary>
        /// Raised when a frame is attached.
        /// </summary>
        public event EventHandler<FrameEventArgs> FrameAttached;

        /// <summary>
        /// Raised when a frame is detached.
        /// </summary>
        public event EventHandler<FrameEventArgs> FrameDetached;

        /// <summary>
        /// Raised when a frame is navigated to a new url.
        /// </summary>
        public event EventHandler<FrameEventArgs> FrameNavigated;

        /// <summary>
        /// Raised when a <see cref="Response"/> is received.
        /// </summary>
        /// <example>
        /// An example of handling <see cref="Response"/> event:
        /// <code>
        /// <![CDATA[
        /// var tcs = new TaskCompletionSource<string>();
        /// page.Response += async(sender, e) =>
        /// {
        ///     if (e.Response.Url.Contains("script.js"))
        ///     {
        ///         tcs.TrySetResult(await e.Response.TextAsync());
        ///     }
        /// };
        ///
        /// await Task.WhenAll(
        ///     page.GoToAsync(TestConstants.ServerUrl + "/grid.html"),
        ///     tcs.Task);
        /// Console.WriteLine(await tcs.Task);
        /// ]]>
        /// </code>
        /// </example>
        public event EventHandler<ResponseCreatedEventArgs> Response;

        /// <summary>
        /// Raised when a page issues a request. The <see cref="Request"/> object is read-only.
        /// In order to intercept and mutate requests, see <see cref="SetRequestInterceptionAsync(bool)"/>
        /// </summary>
        public event EventHandler<RequestEventArgs> Request;

        /// <summary>
        /// Raised when a request finishes successfully.
        /// </summary>
        public event EventHandler<RequestEventArgs> RequestFinished;

        /// <summary>
        /// Raised when a request fails, for example by timing out.
        /// </summary>
        public event EventHandler<RequestEventArgs> RequestFailed;

        /// <summary>
        /// Raised when an uncaught exception happens within the page.
        /// </summary>
        public event EventHandler<PageErrorEventArgs> PageError;

        ///// <summary>
        ///// Emitted when a dedicated WebWorker (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API"/>) is spawned by the page.
        ///// </summary>
        //public event EventHandler<WorkerEventArgs> WorkerCreated;

        ///// <summary>
        ///// Emitted when a dedicated WebWorker (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Workers_API"/>) is terminated.
        ///// </summary>
        //public event EventHandler<WorkerEventArgs> WorkerDestroyed;

        /// <summary>
        /// Raised when the page closes.
        /// </summary>
        public event EventHandler Close;

        /// <summary>
        /// Raised when the page opens a new tab or window.
        /// </summary>
        public event EventHandler<PopupEventArgs> Popup;

        /// <summary>
        /// This setting will change the default maximum time for the following methods:
        /// - <see cref="GoToAsync(string, NavigationOptions)"/>
        /// - <see cref="GoBackAsync(NavigationOptions)"/>
        /// - <see cref="GoForwardAsync(NavigationOptions)"/>
        /// - <see cref="ReloadAsync(NavigationOptions)"/>
        /// - <see cref="SetContentAsync(string, NavigationOptions)"/>
        /// - <see cref="WaitForNavigationAsync(NavigationOptions)"/>
        /// **NOTE** <see cref="DefaultNavigationTimeout"/> takes priority over <seealso cref="DefaultTimeout"/>
        /// </summary>
        public int DefaultNavigationTimeout
        {
            get => _timeoutSettings.NavigationTimeout;
            set => _timeoutSettings.NavigationTimeout = value;
        }

        /// <summary>
        /// This setting will change the default maximum times for the following methods:
        /// - <see cref="GoBackAsync(NavigationOptions)"/>
        /// - <see cref="GoForwardAsync(NavigationOptions)"/>
        /// - <see cref="GoToAsync(string, NavigationOptions)"/>
        /// - <see cref="ReloadAsync(NavigationOptions)"/>
        /// - <see cref="SetContentAsync(string, NavigationOptions)"/>
        /// - <see cref="WaitForFunctionAsync(string, object[])"/>
        /// - <see cref="WaitForNavigationAsync(NavigationOptions)"/>
        /// - <see cref="WaitForRequestAsync(string, WaitForOptions)"/>
        /// - <see cref="WaitForResponseAsync(string, WaitForOptions)"/>
        /// - <see cref="WaitForXPathAsync(string, WaitForSelectorOptions)"/>
        /// - <see cref="WaitForSelectorAsync(string, WaitForSelectorOptions)"/>
        /// - <see cref="WaitForExpressionAsync(string, WaitForFunctionOptions)"/>
        /// </summary>
        public int DefaultTimeout
        {
            get => _timeoutSettings.Timeout;
            set => _timeoutSettings.Timeout = value;
        }

        /// <summary>
        /// Gets page's main frame
        /// </summary>
        /// <remarks>
        /// Page is guaranteed to have a main frame which persists during navigations.
        /// </remarks>
        public Frame MainFrame => FrameManager.MainFrame;

        /// <summary>
        /// Gets all frames attached to the page.
        /// </summary>
        /// <value>An array of all frames attached to the page.</value>
        public Frame[] Frames => FrameManager.GetFrames();

        ///// <summary>
        ///// Gets all workers in the page.
        ///// </summary>
        //public Worker[] Workers => _workers.Values.ToArray();

        /// <summary>
        /// Shortcut for <c>page.MainFrame.Url</c>
        /// </summary>
        public string Url => MainFrame.Url;

        /// <summary>
        /// Gets that target this page was created from.
        /// </summary>
        public Target Target { get; }

        /// <summary>
        /// Gets this page's keyboard
        /// </summary>
        public Keyboard Keyboard { get; }

        /// <summary>
        /// Gets this page's touchscreen
        /// </summary>
        public Touchscreen Touchscreen { get; }

        /// <summary>
        /// Gets this page's coverage
        /// </summary>
        public Coverage Coverage { get; }

        /// <summary>
        /// Gets this page's tracing
        /// </summary>
        public Tracing Tracing { get; }

        /// <summary>
        /// Gets this page's mouse
        /// </summary>
        public Mouse Mouse { get; }

        /// <summary>
        /// Gets this page's viewport
        /// </summary>
        public ViewPortOptions Viewport { get; private set; }

        /// <summary>
        /// List of supported metrics provided by the <see cref="Metrics"/> event.
        /// </summary>
        public static readonly IEnumerable<string> SupportedMetrics = new List<string>
        {
            "Timestamp",
            "Documents",
            "Frames",
            "JSEventListeners",
            "Nodes",
            "LayoutCount",
            "RecalcStyleCount",
            "LayoutDuration",
            "RecalcStyleDuration",
            "ScriptDuration",
            "TaskDuration",
            "JSHeapUsedSize",
            "JSHeapTotalSize"
        };

        /// <summary>
        /// Get the browser the page belongs to.
        /// </summary>
        public Browser Browser => Target.Browser;

        /// <summary>
        /// Get the browser context that the page belongs to.
        /// </summary>
        public BrowserContext BrowserContext => Target.BrowserContext;

        /// <summary>
        /// Get an indication that the page has been closed.
        /// </summary>
        public bool IsClosed { get; private set; }

        /// <summary>
        /// Gets the accessibility.
        /// </summary>
        public Accessibility Accessibility { get; }

        internal bool JavascriptEnabled { get; set; } = true;
        internal bool HasPopupEventListeners => Popup?.GetInvocationList().Any() == true;
        internal FrameManager FrameManager { get; private set; }

        private Task SessionClosedTask
        {
            get
            {
                if (_sessionClosedTcs == null)
                {
                    _sessionClosedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Client.Disconnected += clientDisconnected;

                    void clientDisconnected(object sender, EventArgs e)
                    {
                        _sessionClosedTcs.TrySetException(new TargetClosedException("Target closed", "Session closed"));
                        Client.Disconnected -= clientDisconnected;
                    }
                }

                return _sessionClosedTcs.Task;
            }
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Sets the page's geolocation.
        /// </summary>
        /// <returns>The task.</returns>
        /// <param name="options">Geolocation options.</param>
        /// <remarks>
        /// Consider using <seealso cref="BrowserContext.OverridePermissionsAsync(string, IEnumerable{OverridePermission})"/> to grant permissions for the page to read its geolocation.
        /// </remarks>
        public Task SetGeolocationAsync(GeolocationOption options)
        {
            if (options.Longitude < -180 || options.Longitude > 180)
            {
                throw new ArgumentException($"Invalid longitude '{ options.Longitude }': precondition - 180 <= LONGITUDE <= 180 failed.");
            }
            if (options.Latitude < -90 || options.Latitude > 90)
            {
                throw new ArgumentException($"Invalid latitude '{ options.Latitude }': precondition - 90 <= LATITUDE <= 90 failed.");
            }
            if (options.Accuracy < 0)
            {
                throw new ArgumentException($"Invalid accuracy '{options.Accuracy}': precondition 0 <= ACCURACY failed.");
            }

            return Client.SendAsync("Emulation.setGeolocationOverride", options);
        }

        /// <summary>
        /// Returns metrics
        /// </summary>
        /// <returns>Task which resolves into a list of metrics</returns>
        /// <remarks>
        /// All timestamps are in monotonic time: monotonically increasing time in seconds since an arbitrary point in the past.
        /// </remarks>
        public async Task<Dictionary<string, decimal>> MetricsAsync()
        {
            var response = await Client.SendAsync<PerformanceGetMetricsResponse>("Performance.getMetrics").ConfigureAwait(false);
            return BuildMetricsObject(response.Metrics);
        }

        /// <summary>
        /// A utility function to be used with <see cref="Extensions.EvaluateFunctionAsync{T}(Task{JSHandle}, string, object[])"/>
        /// </summary>
        /// <param name="selector">A selector to query page for</param>
        /// <returns>Task which resolves to a <see cref="JSHandle"/> of <c>document.querySelectorAll</c> result</returns>
        public Task<JSHandle> QuerySelectorAllHandleAsync(string selector)
            => EvaluateFunctionHandleAsync("selector => Array.from(document.querySelectorAll(selector))", selector);

        /// <summary>
        /// Executes a script in browser context
        /// </summary>
        /// <param name="script">Script to be evaluated in browser context</param>
        /// <remarks>
        /// If the script, returns a Promise, then the method would wait for the promise to resolve and return its value.
        /// </remarks>
        /// <returns>Task which resolves to script return value</returns>
        public async Task<JSHandle> EvaluateExpressionHandleAsync(string script)
        {
            var context = await MainFrame.GetExecutionContextAsync().ConfigureAwait(false);
            return await context.EvaluateExpressionHandleAsync(script).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a script in browser context
        /// </summary>
        /// <param name="pageFunction">Script to be evaluated in browser context</param>
        /// <param name="args">Function arguments</param>
        /// <remarks>
        /// If the script, returns a Promise, then the method would wait for the promise to resolve and return its value.
        /// <see cref="JSHandle"/> instances can be passed as arguments
        /// </remarks>
        /// <returns>Task which resolves to script return value</returns>
        public async Task<JSHandle> EvaluateFunctionHandleAsync(string pageFunction, params object[] args)
        {
            var context = await MainFrame.GetExecutionContextAsync().ConfigureAwait(false);
            return await context.EvaluateFunctionHandleAsync(pageFunction, args).ConfigureAwait(false);
        }

        /// <summary>
        /// Activating request interception enables <see cref="Request.AbortAsync(RequestAbortErrorCode)">request.AbortAsync</see>, 
        /// <see cref="Request.ContinueAsync(Payload)">request.ContinueAsync</see> and <see cref="Request.RespondAsync(ResponseData)">request.RespondAsync</see> methods.
        /// </summary>
        /// <returns>The request interception task.</returns>
        /// <param name="value">Whether to enable request interception..</param>
        public Task SetRequestInterceptionAsync(bool value)
            => FrameManager.NetworkManager.SetRequestInterceptionAsync(value);

        /// <summary>
        /// Set offline mode for the page.
        /// </summary>
        /// <returns>Result task</returns>
        /// <param name="value">When <c>true</c> enables offline mode for the page.</param>
        public Task SetOfflineModeAsync(bool value) => FrameManager.NetworkManager.SetOfflineModeAsync(value);

        /// <summary>
        /// Adds a <c><![CDATA[<script>]]></c> tag into the page with the desired url or content
        /// </summary>
        /// <param name="options">add script tag options</param>
        /// <remarks>
        /// Shortcut for <c>page.MainFrame.AddScriptTagAsync(options)</c>
        /// </remarks>
        /// <returns>Task which resolves to the added tag when the script's onload fires or when the script content was injected into frame</returns>
        /// <seealso cref="Frame.AddScriptTagAsync(AddTagOptions)"/>
        public Task<ElementHandle> AddScriptTagAsync(AddTagOptions options) => MainFrame.AddScriptTagAsync(options);

        /// <summary>
        /// Adds a <c><![CDATA[<script>]]></c> tag into the page with the desired url or content
        /// </summary>
        /// <param name="url">script url</param>
        /// <remarks>
        /// Shortcut for <c>page.MainFrame.AddScriptTagAsync(new AddTagOptions { Url = url })</c>
        /// </remarks>
        /// <returns>Task which resolves to the added tag when the script's onload fires or when the script content was injected into frame</returns>
        public Task<ElementHandle> AddScriptTagAsync(string url) => AddScriptTagAsync(new AddTagOptions { Url = url });

        /// <summary>
        /// Adds a <c><![CDATA[<link rel="stylesheet">]]></c> tag into the page with the desired url or a <c><![CDATA[<link rel="stylesheet">]]></c> tag with the content
        /// </summary>
        /// <param name="options">add style tag options</param>
        /// <remarks>
        /// Shortcut for <c>page.MainFrame.AddStyleTagAsync(options)</c>
        /// </remarks>
        /// <returns>Task which resolves to the added tag when the stylesheet's onload fires or when the CSS content was injected into frame</returns>
        /// <seealso cref="Frame.AddStyleTag(AddTagOptions)"/>
        public Task<ElementHandle> AddStyleTagAsync(AddTagOptions options) => MainFrame.AddStyleTagAsync(options);

        /// <summary>
        /// Adds a <c><![CDATA[<link rel="stylesheet">]]></c> tag into the page with the desired url or a <c><![CDATA[<link rel="stylesheet">]]></c> tag with the content
        /// </summary>
        /// <param name="url">stylesheel url</param>
        /// <remarks>
        /// Shortcut for <c>page.MainFrame.AddStyleTagAsync(new AddTagOptions { Url = url })</c>
        /// </remarks>
        /// <returns>Task which resolves to the added tag when the stylesheet's onload fires or when the CSS content was injected into frame</returns>
        public Task<ElementHandle> AddStyleTagAsync(string url) => AddStyleTagAsync(new AddTagOptions { Url = url });

        /// <summary>
        /// Adds a function called <c>name</c> on the page's <c>window</c> object.
        /// When called, the function executes <paramref name="puppeteerFunction"/> in C# and returns a <see cref="Task"/> which resolves when <paramref name="puppeteerFunction"/> completes.
        /// </summary>
        /// <param name="name">Name of the function on the window object</param>
        /// <param name="puppeteerFunction">Callback function which will be called in Puppeteer's context.</param>
        /// <remarks>
        /// If the <paramref name="puppeteerFunction"/> returns a <see cref="Task"/>, it will be awaited.
        /// Functions installed via <see cref="ExposeFunctionAsync(string, Action)"/> survive navigations
        /// </remarks>
        /// <returns>Task</returns>
        public Task ExposeFunctionAsync(string name, Action puppeteerFunction)
            => ExposeFunctionAsync(name, (Delegate)puppeteerFunction);

        /// <summary>
        /// Adds a function called <c>name</c> on the page's <c>window</c> object.
        /// When called, the function executes <paramref name="puppeteerFunction"/> in C# and returns a <see cref="Task"/> which resolves to the return value of <paramref name="puppeteerFunction"/>.
        /// </summary>
        /// <typeparam name="TResult">The result of <paramref name="puppeteerFunction"/></typeparam>
        /// <param name="name">Name of the function on the window object</param>
        /// <param name="puppeteerFunction">Callback function which will be called in Puppeteer's context.</param>
        /// <remarks>
        /// If the <paramref name="puppeteerFunction"/> returns a <see cref="Task"/>, it will be awaited.
        /// Functions installed via <see cref="ExposeFunctionAsync{TResult}(string, Func{TResult})"/> survive navigations
        /// </remarks>
        /// <returns>Task</returns>
        public Task ExposeFunctionAsync<TResult>(string name, Func<TResult> puppeteerFunction)
            => ExposeFunctionAsync(name, (Delegate)puppeteerFunction);

        /// <summary>
        /// Adds a function called <c>name</c> on the page's <c>window</c> object.
        /// When called, the function executes <paramref name="puppeteerFunction"/> in C# and returns a <see cref="Task"/> which resolves to the return value of <paramref name="puppeteerFunction"/>.
        /// </summary>
        /// <typeparam name="TResult">The result of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T">The parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <param name="name">Name of the function on the window object</param>
        /// <param name="puppeteerFunction">Callback function which will be called in Puppeteer's context.</param>
        /// <remarks>
        /// If the <paramref name="puppeteerFunction"/> returns a <see cref="Task"/>, it will be awaited.
        /// Functions installed via <see cref="ExposeFunctionAsync{T, TResult}(string, Func{T, TResult})"/> survive navigations
        /// </remarks>
        /// <returns>Task</returns>
        public Task ExposeFunctionAsync<T, TResult>(string name, Func<T, TResult> puppeteerFunction)
            => ExposeFunctionAsync(name, (Delegate)puppeteerFunction);

        /// <summary>
        /// Adds a function called <c>name</c> on the page's <c>window</c> object.
        /// When called, the function executes <paramref name="puppeteerFunction"/> in C# and returns a <see cref="Task"/> which resolves to the return value of <paramref name="puppeteerFunction"/>.
        /// </summary>
        /// <typeparam name="TResult">The result of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T1">The first parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T2">The second parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <param name="name">Name of the function on the window object</param>
        /// <param name="puppeteerFunction">Callback function which will be called in Puppeteer's context.</param>
        /// <remarks>
        /// If the <paramref name="puppeteerFunction"/> returns a <see cref="Task"/>, it will be awaited.
        /// Functions installed via <see cref="ExposeFunctionAsync{T1, T2, TResult}(string, Func{T1, T2, TResult})"/> survive navigations
        /// </remarks>
        /// <returns>Task</returns>
        public Task ExposeFunctionAsync<T1, T2, TResult>(string name, Func<T1, T2, TResult> puppeteerFunction)
            => ExposeFunctionAsync(name, (Delegate)puppeteerFunction);

        /// <summary>
        /// Adds a function called <c>name</c> on the page's <c>window</c> object.
        /// When called, the function executes <paramref name="puppeteerFunction"/> in C# and returns a <see cref="Task"/> which resolves to the return value of <paramref name="puppeteerFunction"/>.
        /// </summary>
        /// <typeparam name="TResult">The result of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T1">The first parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T2">The second parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T3">The third parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <param name="name">Name of the function on the window object</param>
        /// <param name="puppeteerFunction">Callback function which will be called in Puppeteer's context.</param>
        /// <remarks>
        /// If the <paramref name="puppeteerFunction"/> returns a <see cref="Task"/>, it will be awaited.
        /// Functions installed via <see cref="ExposeFunctionAsync{T1, T2, T3, TResult}(string, Func{T1, T2, T3, TResult})"/> survive navigations
        /// </remarks>
        /// <returns>Task</returns>
        public Task ExposeFunctionAsync<T1, T2, T3, TResult>(string name, Func<T1, T2, T3, TResult> puppeteerFunction)
            => ExposeFunctionAsync(name, (Delegate)puppeteerFunction);

        /// <summary>
        /// Adds a function called <c>name</c> on the page's <c>window</c> object.
        /// When called, the function executes <paramref name="puppeteerFunction"/> in C# and returns a <see cref="Task"/> which resolves to the return value of <paramref name="puppeteerFunction"/>.
        /// </summary>
        /// <typeparam name="TResult">The result of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T1">The first parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T2">The second parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T3">The third parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <typeparam name="T4">The fourth parameter of <paramref name="puppeteerFunction"/></typeparam>
        /// <param name="name">Name of the function on the window object</param>
        /// <param name="puppeteerFunction">Callback function which will be called in Puppeteer's context.</param>
        /// <remarks>
        /// If the <paramref name="puppeteerFunction"/> returns a <see cref="Task"/>, it will be awaited.
        /// Functions installed via <see cref="ExposeFunctionAsync{T1, T2, T3, T4, TResult}(string, Func{T1, T2, T3, T4, TResult})"/> survive navigations
        /// </remarks>
        /// <returns>Task</returns>
        public Task ExposeFunctionAsync<T1, T2, T3, T4, TResult>(string name, Func<T1, T2, T3, T4, TResult> puppeteerFunction)
            => ExposeFunctionAsync(name, (Delegate)puppeteerFunction);

        /// <summary>
        /// Gets the full HTML contents of the page, including the doctype.
        /// </summary>
        /// <returns>Task which resolves to the HTML content.</returns>
        /// <seealso cref="Frame.GetContentAsync"/>
        public Task<string> GetContentAsync() => FrameManager.MainFrame.GetContentAsync();

        /// <summary>
        /// Sets the HTML markup to the page
        /// </summary>
        /// <param name="html">HTML markup to assign to the page.</param>
        /// <param name="options">The navigations options</param>
        /// <returns>Task.</returns>
        /// <seealso cref="Frame.SetContentAsync(string, NavigationOptions)"/>
        public Task SetContentAsync(string html, NavigationOptions options = null) => FrameManager.MainFrame.SetContentAsync(html, options);

        /// <summary>
        /// Navigates to an url
        /// </summary>
        /// <remarks>
        /// <see cref="GoToAsync(string, int?, WaitUntilNavigation[])"/> will throw an error if:
        /// - there's an SSL error (e.g. in case of self-signed certificates).
        /// - target URL is invalid.
        /// - the `timeout` is exceeded during navigation.
        /// - the remote server does not respond or is unreachable.
        /// - the main resource failed to load.
        /// 
        /// <see cref="GoToAsync(string, int?, WaitUntilNavigation[])"/> will not throw an error when any valid HTTP status code is returned by the remote server, 
        /// including 404 "Not Found" and 500 "Internal Server Error".  The status code for such responses can be retrieved by calling <see cref="Response.Status"/>
        /// 
        /// > **NOTE** <see cref="GoToAsync(string, int?, WaitUntilNavigation[])"/> either throws an error or returns a main resource response. 
        /// The only exceptions are navigation to `about:blank` or navigation to the same URL with a different hash, which would succeed and return `null`.
        /// 
        /// > **NOTE** Headless mode doesn't support navigation to a PDF document. See the <see fref="https://bugs.chromium.org/p/chromium/issues/detail?id=761295">upstream issue</see>.
        /// 
        /// Shortcut for <seealso cref="Frame.GoToAsync(string, int?, WaitUntilNavigation[])"/>
        /// </remarks>
        /// <param name="url">URL to navigate page to. The url should include scheme, e.g. https://.</param>
        /// <param name="options">Navigation parameters.</param>
        /// <returns>Task which resolves to the main resource response. In case of multiple redirects, the navigation will resolve with the response of the last redirect.</returns>
        /// <seealso cref="GoToAsync(string, int?, WaitUntilNavigation[])"/>
        public Task<Response> GoToAsync(string url, NavigationOptions options) => FrameManager.MainFrame.GoToAsync(url, options);

        /// <summary>
        /// Navigates to an url
        /// </summary>
        /// <param name="url">URL to navigate page to. The url should include scheme, e.g. https://.</param>
        /// <param name="timeout">Maximum navigation time in milliseconds, defaults to 30 seconds, pass <c>0</c> to disable timeout. </param>
        /// <param name="waitUntil">When to consider navigation succeeded, defaults to <see cref="WaitUntilNavigation.Load"/>. Given an array of <see cref="WaitUntilNavigation"/>, navigation is considered to be successful after all events have been fired</param>
        /// <returns>Task which resolves to the main resource response. In case of multiple redirects, the navigation will resolve with the response of the last redirect</returns>
        /// <seealso cref="GoToAsync(string, NavigationOptions)"/>
        public Task<Response> GoToAsync(string url, int? timeout = null, WaitUntilNavigation[] waitUntil = null)
            => GoToAsync(url, new NavigationOptions { Timeout = timeout, WaitUntil = waitUntil });

        /// <summary>
        /// Navigates to an url
        /// </summary>
        /// <param name="url">URL to navigate page to. The url should include scheme, e.g. https://.</param>
        /// <param name="waitUntil">When to consider navigation succeeded.</param>
        /// <returns>Task which resolves to the main resource response. In case of multiple redirects, the navigation will resolve with the response of the last redirect</returns>
        /// <seealso cref="GoToAsync(string, NavigationOptions)"/>
        public Task<Response> GoToAsync(string url, WaitUntilNavigation waitUntil)
            => GoToAsync(url, new NavigationOptions { WaitUntil = new[] { waitUntil } });

        /// <summary>
        /// generates a pdf of the page with <see cref="MediaType.Print"/> css media. To generate a pdf with <see cref="MediaType.Screen"/> media call <see cref="EmulateMediaAsync(MediaType)"/> with <see cref="MediaType.Screen"/>
        /// </summary>
        /// <param name="file">The file path to save the PDF to. paths are resolved using <see cref="Path.GetFullPath(string)"/></param>
        /// <returns></returns>
        /// <remarks>
        /// Generating a pdf is currently only supported in Chrome headless
        /// </remarks>
        public Task PdfAsync(string file) => PdfAsync(file, new PdfOptions());

        /// <summary>
        ///  generates a pdf of the page with <see cref="MediaType.Print"/> css media. To generate a pdf with <see cref="MediaType.Screen"/> media call <see cref="EmulateMediaAsync(MediaType)"/> with <see cref="MediaType.Screen"/>
        /// </summary>
        /// <param name="file">The file path to save the PDF to. paths are resolved using <see cref="Path.GetFullPath(string)"/></param>
        /// <param name="options">pdf options</param>
        /// <returns></returns>
        /// <remarks>
        /// Generating a pdf is currently only supported in Chrome headless
        /// </remarks>
        public async Task PdfAsync(string file, PdfOptions options)
            => await PdfInternalAsync(file, options).ConfigureAwait(false);

        /// <summary>
        /// generates a pdf of the page with <see cref="MediaType.Print"/> css media. To generate a pdf with <see cref="MediaType.Screen"/> media call <see cref="EmulateMediaAsync(MediaType)"/> with <see cref="MediaType.Screen"/>
        /// </summary>
        /// <returns>Task which resolves to a <see cref="Stream"/> containing the PDF data.</returns>
        /// <remarks>
        /// Generating a pdf is currently only supported in Chrome headless
        /// </remarks>
        public Task<Stream> PdfStreamAsync() => PdfStreamAsync(new PdfOptions());

        /// <summary>
        /// Generates a pdf of the page with <see cref="MediaType.Print"/> css media. To generate a pdf with <see cref="MediaType.Screen"/> media call <see cref="EmulateMediaAsync(MediaType)"/> with <see cref="MediaType.Screen"/>
        /// </summary>
        /// <param name="options">pdf options</param>
        /// <returns>Task which resolves to a <see cref="Stream"/> containing the PDF data.</returns>
        /// <remarks>
        /// Generating a pdf is currently only supported in Chrome headless
        /// </remarks>
        public async Task<Stream> PdfStreamAsync(PdfOptions options)
            => new MemoryStream(await PdfDataAsync(options).ConfigureAwait(false));

        /// <summary>
        /// Generates a pdf of the page with <see cref="MediaType.Print"/> css media. To generate a pdf with <see cref="MediaType.Screen"/> media call <see cref="EmulateMediaAsync(MediaType)"/> with <see cref="MediaType.Screen"/>
        /// </summary>
        /// <returns>Task which resolves to a <see cref="byte"/>[] containing the PDF data.</returns>
        /// <remarks>
        /// Generating a pdf is currently only supported in Chrome headless
        /// </remarks>
        public Task<byte[]> PdfDataAsync() => PdfDataAsync(new PdfOptions());

        /// <summary>
        /// Generates a pdf of the page with <see cref="MediaType.Print"/> css media. To generate a pdf with <see cref="MediaType.Screen"/> media call <see cref="EmulateMediaAsync(MediaType)"/> with <see cref="MediaType.Screen"/>
        /// </summary>
        /// <param name="options">pdf options</param>
        /// <returns>Task which resolves to a <see cref="byte"/>[] containing the PDF data.</returns>
        /// <remarks>
        /// Generating a pdf is currently only supported in Chrome headless
        /// </remarks>
        public Task<byte[]> PdfDataAsync(PdfOptions options) => PdfInternalAsync(null, options);

        internal async Task<byte[]> PdfInternalAsync(string file, PdfOptions options)
        {
            var paperWidth = PaperFormat.Letter.Width;
            var paperHeight = PaperFormat.Letter.Height;

            if (options.Format != null)
            {
                paperWidth = options.Format.Width;
                paperHeight = options.Format.Height;
            }
            else
            {
                if (options.Width != null)
                {
                    paperWidth = ConvertPrintParameterToInches(options.Width);
                }
                if (options.Height != null)
                {
                    paperHeight = ConvertPrintParameterToInches(options.Height);
                }
            }

            var marginTop = ConvertPrintParameterToInches(options.MarginOptions.Top);
            var marginLeft = ConvertPrintParameterToInches(options.MarginOptions.Left);
            var marginBottom = ConvertPrintParameterToInches(options.MarginOptions.Bottom);
            var marginRight = ConvertPrintParameterToInches(options.MarginOptions.Right);

            var result = await Client.SendAsync<PagePrintToPDFResponse>("Page.printToPDF", new PagePrintToPDFRequest
            {
                TransferMode = "ReturnAsStream",
                Landscape = options.Landscape,
                DisplayHeaderFooter = options.DisplayHeaderFooter,
                HeaderTemplate = options.HeaderTemplate,
                FooterTemplate = options.FooterTemplate,
                PrintBackground = options.PrintBackground,
                Scale = options.Scale,
                PaperWidth = paperWidth,
                PaperHeight = paperHeight,
                MarginTop = marginTop,
                MarginBottom = marginBottom,
                MarginLeft = marginLeft,
                MarginRight = marginRight,
                PageRanges = options.PageRanges,
                PreferCSSPageSize = options.PreferCSSPageSize
            }).ConfigureAwait(false);

            return await ProtocolStreamReader.ReadProtocolStreamByteAsync(Client, result.Stream, file).ConfigureAwait(false);
        }

        /// <summary>
        /// Enables/Disables Javascript on the page
        /// </summary>
        /// <returns>Task.</returns>
        /// <param name="enabled">Whether or not to enable JavaScript on the page.</param>
        public Task SetJavaScriptEnabledAsync(bool enabled)
        {
            if (enabled == JavascriptEnabled)
            {
                return Task.CompletedTask;
            }
            JavascriptEnabled = enabled;
            return Client.SendAsync("Emulation.setScriptExecutionDisabled", new EmulationSetScriptExecutionDisabledRequest
            {
                Value = !enabled
            });
        }

        /// <summary>
        /// Toggles bypassing page's Content-Security-Policy.
        /// </summary>
        /// <param name="enabled">sets bypassing of page's Content-Security-Policy.</param>
        /// <returns></returns>
        /// <remarks>
        /// CSP bypassing happens at the moment of CSP initialization rather then evaluation.
        /// Usually this means that <see cref="SetBypassCSPAsync(bool)"/> should be called before navigating to the domain.
        /// </remarks>
        public Task SetBypassCSPAsync(bool enabled) => Client.SendAsync("Page.setBypassCSP", new PageSetBypassCSPRequest
        {
            Enabled = enabled
        });

        /// <summary>
        /// Emulates a media such as screen or print.
        /// </summary>
        /// <returns>Task.</returns>
        /// <param name="media">Media to set.</param>
        [Obsolete("User EmulateMediaTypeAsync instead")]
        public Task EmulateMediaAsync(MediaType media) => EmulateMediaTypeAsync(media);

        /// <summary>
        /// Emulates a media such as screen or print.
        /// </summary>
        /// <param name="type">Media to set.</param>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('screen').matches)");
        /// // → true
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('print').matches)");
        /// // → true
        /// await page.EmulateMediaTypeAsync(MediaType.Print);
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('screen').matches)");
        /// // → false
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('print').matches)");
        /// // → true
        /// await page.EmulateMediaTypeAsync(MediaType.None);
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('screen').matches)");
        /// // → true
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('print').matches)");
        /// // → true
        /// ]]>
        /// </code>
        /// </example>
        /// <returns>Emulate media type task.</returns>
        public Task EmulateMediaTypeAsync(MediaType type)
            => Client.SendAsync("Emulation.setEmulatedMedia", new EmulationSetEmulatedMediaTypeRequest { Media = type });

        /// <summary>
        /// Given an array of media feature objects, emulates CSS media features on the page.
        /// </summary>
        /// <param name="features">Features to apply</param>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// await page.EmulateMediaFeaturesAsync(new MediaFeature[]{ new MediaFeature { MediaFeature =  MediaFeature.PrefersColorScheme, Value = "dark" }});
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: dark)').matches)");
        /// // → true
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: light)').matches)");
        /// // → false
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: no-preference)').matches)");
        /// // → false
        /// await page.EmulateMediaFeaturesAsync(new MediaFeature[]{ new MediaFeature { MediaFeature = MediaFeature.PrefersReducedMotion, Value = "reduce" }});
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches)");
        /// // → true
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: no-preference)').matches)");
        /// // → false
        /// await page.EmulateMediaFeaturesAsync(new MediaFeature[]
        /// { 
        ///   new MediaFeature { MediaFeature = MediaFeature.PrefersColorScheme, Value = "dark" },
        ///   new MediaFeature { MediaFeature = MediaFeature.PrefersReducedMotion, Value = "reduce" },
        /// });
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: dark)').matches)");
        /// // → true
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: light)').matches)");
        /// // → false
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: no-preference)').matches)");
        /// // → false
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches)");
        /// // → true
        /// await page.EvaluateFunctionAsync<bool>("() => matchMedia('(prefers-color-scheme: no-preference)').matches)");
        /// // → false
        /// ]]>
        /// </code>
        /// </example>
        /// <returns>Emulate features task</returns>
        public Task EmulateMediaFeaturesAsync(IEnumerable<MediaFeatureValue> features)
            => Client.SendAsync("Emulation.setEmulatedMedia", new EmulationSetEmulatedMediaFeatureRequest { Features = features });

        /// <summary>
        /// Sets the viewport.
        /// In the case of multiple pages in a single browser, each page can have its own viewport size.
        /// <see cref="SetViewportAsync(ViewPortOptions)"/> will resize the page. A lot of websites don't expect phones to change size, so you should set the viewport before navigating to the page.
        /// </summary>
        /// <example>
        ///<![CDATA[
        /// using(var page = await browser.NewPageAsync())
        /// {
        ///     await page.SetViewPortAsync(new ViewPortOptions
        ///     {
        ///         Width = 640, 
        ///         Height = 480, 
        ///         DeviceScaleFactor = 1
        ///     });
        ///     await page.goto('https://www.example.com');
        /// }
        /// ]]>
        /// </example>
        /// <returns>The viewport task.</returns>
        /// <param name="viewport">Viewport options.</param>
        public async Task SetViewportAsync(ViewPortOptions viewport)
        {
            var needsReload = await _emulationManager.EmulateViewport(viewport).ConfigureAwait(false);
            Viewport = viewport;

            if (needsReload)
            {
                await ReloadAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Closes the page.
        /// </summary>
        /// <param name="options">Close options.</param>
        /// <returns>Task.</returns>
        public Task CloseAsync(PageCloseOptions options = null)
        {
            if (!(Client?.Connection?.IsClosed ?? true))
            {
                var runBeforeUnload = options?.RunBeforeUnload ?? false;

                if (runBeforeUnload)
                {
                    return Client.SendAsync("Page.close");
                }

                return Client.Connection.SendAsync("Target.closeTarget", new TargetCloseTargetRequest
                {
                    TargetId = Target.TargetId
                }).ContinueWith(task => Target.CloseTask);
            }

            return _closeCompletedTcs.Task;
        }

        /// <summary>
        /// Reloads the page
        /// </summary>
        /// <param name="options">Navigation options</param>
        /// <returns>Task which resolves to the main resource response. In case of multiple redirects, the navigation will resolve with the response of the last redirect</returns>
        /// <seealso cref="ReloadAsync(int?, WaitUntilNavigation[])"/>
        public async Task<Response> ReloadAsync(NavigationOptions options)
        {
            var navigationTask = WaitForNavigationAsync(options);

            await Task.WhenAll(
              navigationTask,
              Client.SendAsync("Page.reload")
            ).ConfigureAwait(false);

            return navigationTask.Result;
        }

        /// <summary>
        /// Reloads the page
        /// </summary>
        /// <param name="timeout">Maximum navigation time in milliseconds, defaults to 30 seconds, pass <c>0</c> to disable timeout. </param>
        /// <param name="waitUntil">When to consider navigation succeeded, defaults to <see cref="WaitUntilNavigation.Load"/>. Given an array of <see cref="WaitUntilNavigation"/>, navigation is considered to be successful after all events have been fired</param>
        /// <returns>Task which resolves to the main resource response. In case of multiple redirects, the navigation will resolve with the response of the last redirect</returns>
        /// <seealso cref="ReloadAsync(NavigationOptions)"/>
        public Task<Response> ReloadAsync(int? timeout = null, WaitUntilNavigation[] waitUntil = null)
            => ReloadAsync(new NavigationOptions { Timeout = timeout, WaitUntil = waitUntil });

        /// <summary>
        /// Waits for a function to be evaluated to a truthy value
        /// </summary>
        /// <param name="script">Function to be evaluated in browser context</param>
        /// <param name="options">Optional waiting parameters</param>
        /// <param name="args">Arguments to pass to <c>script</c></param>
        /// <returns>A task that resolves when the <c>script</c> returns a truthy value</returns>
        /// <seealso cref="Frame.WaitForFunctionAsync(string, WaitForFunctionOptions, object[])"/>
        public Task<JSHandle> WaitForFunctionAsync(string script, WaitForFunctionOptions options = null, params object[] args)
            => MainFrame.WaitForFunctionAsync(script, options ?? new WaitForFunctionOptions(), args);

        /// <summary>
        /// Waits for an expression to be evaluated to a truthy value
        /// </summary>
        /// <param name="script">Expression to be evaluated in browser context</param>
        /// <param name="options">Optional waiting parameters</param>
        /// <returns>A task that resolves when the <c>script</c> returns a truthy value</returns>
        /// <seealso cref="Frame.WaitForExpressionAsync(string, WaitForFunctionOptions)"/>
        public Task<JSHandle> WaitForExpressionAsync(string script, WaitForFunctionOptions options = null)
            => MainFrame.WaitForExpressionAsync(script, options ?? new WaitForFunctionOptions());

        /// <summary>
        /// Waits for a selector to be added to the DOM
        /// </summary>
        /// <param name="selector">A selector of an element to wait for</param>
        /// <param name="options">Optional waiting parameters</param>
        /// <returns>A task that resolves when element specified by selector string is added to DOM.
        /// Resolves to `null` if waiting for `hidden: true` and selector is not found in DOM.</returns>
        /// <seealso cref="WaitForXPathAsync(string, WaitForSelectorOptions)"/>
        /// <seealso cref="Frame.WaitForSelectorAsync(string, WaitForSelectorOptions)"/>
        public Task<ElementHandle> WaitForSelectorAsync(string selector, WaitForSelectorOptions options = null)
            => MainFrame.WaitForSelectorAsync(selector, options ?? new WaitForSelectorOptions());

        /// <summary>
        /// Waits for a xpath selector to be added to the DOM
        /// </summary>
        /// <param name="xpath">A xpath selector of an element to wait for</param>
        /// <param name="options">Optional waiting parameters</param>
        /// <returns>A task which resolves when element specified by xpath string is added to DOM. 
        /// Resolves to `null` if waiting for `hidden: true` and xpath is not found in DOM.</returns>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// var browser = await Puppeteer.LaunchAsync(new LaunchOptions());
        /// var page = await browser.NewPageAsync();
        /// string currentURL = null;
        /// page
        ///     .WaitForXPathAsync("//img")
        ///     .ContinueWith(_ => Console.WriteLine("First URL with image: " + currentURL));
        /// foreach (var current in new[] { "https://example.com", "https://google.com", "https://bbc.com" })
        /// {
        ///     currentURL = current;
        ///     await page.GoToAsync(currentURL);
        /// }
        /// await browser.CloseAsync();
        /// ]]>
        /// </code>
        /// </example>
        /// <seealso cref="WaitForSelectorAsync(string, WaitForSelectorOptions)"/>
        /// <seealso cref="Frame.WaitForXPathAsync(string, WaitForSelectorOptions)"/>
        public Task<ElementHandle> WaitForXPathAsync(string xpath, WaitForSelectorOptions options = null)
            => MainFrame.WaitForXPathAsync(xpath, options ?? new WaitForSelectorOptions());

        /// <summary>
        /// This resolves when the page navigates to a new URL or reloads.
        /// It is useful for when you run code which will indirectly cause the page to navigate.
        /// </summary>
        /// <param name="options">navigation options</param>
        /// <returns>Task which resolves to the main resource response. 
        /// In case of multiple redirects, the navigation will resolve with the response of the last redirect.
        /// In case of navigation to a different anchor or navigation due to History API usage, the navigation will resolve with `null`.
        /// </returns>
        /// <remarks>
        /// Usage of the <c>History API</c> <see href="https://developer.mozilla.org/en-US/docs/Web/API/History_API"/> to change the URL is considered a navigation
        /// </remarks>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// var navigationTask = page.WaitForNavigationAsync();
        /// await page.ClickAsync("a.my-link");
        /// await navigationTask;
        /// ]]>
        /// </code>
        /// </example>
        public Task<Response> WaitForNavigationAsync(NavigationOptions options = null) => FrameManager.WaitForFrameNavigationAsync(FrameManager.MainFrame, options);

        /// <summary>
        /// Waits for a request.
        /// </summary>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// var firstRequest = await page.WaitForRequestAsync("http://example.com/resource");
        /// return firstRequest.Url;
        /// ]]>
        /// </code>
        /// </example>
        /// <returns>A task which resolves when a matching request was made.</returns>
        /// <param name="url">URL to wait for.</param>
        /// <param name="options">Options.</param>
        public Task<Request> WaitForRequestAsync(string url, WaitForOptions options = null)
            => WaitForRequestAsync(request => request.Url == url, options);

        /// <summary>
        /// Waits for a request.
        /// </summary>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// var request = await page.WaitForRequestAsync(request => request.Url === "http://example.com" && request.Method === HttpMethod.Get;
        /// return request.Url;
        /// ]]>
        /// </code>
        /// </example>
        /// <returns>A task which resolves when a matching request was made.</returns>
        /// <param name="predicate">Function which looks for a matching request.</param>
        /// <param name="options">Options.</param>
        public async Task<Request> WaitForRequestAsync(Func<Request, bool> predicate, WaitForOptions options = null)
        {
            var timeout = options?.Timeout ?? DefaultTimeout;
            var requestTcs = new TaskCompletionSource<Request>(TaskCreationOptions.RunContinuationsAsynchronously);

            void requestEventListener(object sender, RequestEventArgs e)
            {
                if (predicate(e.Request))
                {
                    requestTcs.TrySetResult(e.Request);
                    FrameManager.NetworkManager.Request -= requestEventListener;
                }
            }

            FrameManager.NetworkManager.Request += requestEventListener;

            await Task.WhenAny(requestTcs.Task, SessionClosedTask).WithTimeout(timeout, t =>
            {
                FrameManager.NetworkManager.Request -= requestEventListener;
                return new TimeoutException($"Timeout of {t.TotalMilliseconds} ms exceeded");
            }).ConfigureAwait(false);

            if (SessionClosedTask.IsFaulted)
            {
                await SessionClosedTask.ConfigureAwait(false);
            }
            return await requestTcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Waits for a response.
        /// </summary>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// var firstResponse = await page.WaitForResponseAsync("http://example.com/resource");
        /// return firstResponse.Url;
        /// ]]>
        /// </code>
        /// </example>
        /// <returns>A task which resolves when a matching response is received.</returns>
        /// <param name="url">URL to wait for.</param>
        /// <param name="options">Options.</param>
        public Task<Response> WaitForResponseAsync(string url, WaitForOptions options = null)
            => WaitForResponseAsync(response => response.Url == url, options);

        /// <summary>
        /// Waits for a response.
        /// </summary>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// var response = await page.WaitForResponseAsync(response => response.Url === "http://example.com" && response.Status === HttpStatus.Ok;
        /// return response.Url;
        /// ]]>
        /// </code>
        /// </example>
        /// <returns>A task which resolves when a matching response is received.</returns>
        /// <param name="predicate">Function which looks for a matching response.</param>
        /// <param name="options">Options.</param>
        public async Task<Response> WaitForResponseAsync(Func<Response, bool> predicate, WaitForOptions options = null)
        {
            var timeout = options?.Timeout ?? DefaultTimeout;
            var responseTcs = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);

            void responseEventListener(object sender, ResponseCreatedEventArgs e)
            {
                if (predicate(e.Response))
                {
                    responseTcs.TrySetResult(e.Response);
                    FrameManager.NetworkManager.Response -= responseEventListener;
                }
            }

            FrameManager.NetworkManager.Response += responseEventListener;

            await Task.WhenAny(responseTcs.Task, SessionClosedTask).WithTimeout(timeout).ConfigureAwait(false);

            if (SessionClosedTask.IsFaulted)
            {
                await SessionClosedTask.ConfigureAwait(false);
            }
            return await responseTcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Navigate to the previous page in history.
        /// </summary>
        /// <returns>Task that resolves to the main resource response. In case of multiple redirects, 
        /// the navigation will resolve with the response of the last redirect. If can not go back, resolves to null.</returns>
        /// <param name="options">Navigation parameters.</param>
        public Task<Response> GoBackAsync(NavigationOptions options = null) => GoAsync(-1, options);

        /// <summary>
        /// Navigate to the next page in history.
        /// </summary>
        /// <returns>Task that resolves to the main resource response. In case of multiple redirects, 
        /// the navigation will resolve with the response of the last redirect. If can not go forward, resolves to null.</returns>
        /// <param name="options">Navigation parameters.</param>
        public Task<Response> GoForwardAsync(NavigationOptions options = null) => GoAsync(1, options);

        /// <summary>
        /// Resets the background color and Viewport after taking Screenshots using BurstMode.
        /// </summary>
        /// <returns>The burst mode off.</returns>
        public Task SetBurstModeOffAsync()
        {
            _screenshotBurstModeOn = false;
            if (_screenshotBurstModeOptions != null)
            {
                ResetBackgroundColorAndViewportAsync(_screenshotBurstModeOptions);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Brings page to front (activates tab).
        /// </summary>
        /// <returns>A task that resolves when the message has been sent to Chromium.</returns>
        public Task BringToFrontAsync() => Client.SendAsync("Page.bringToFront");

        /// <summary>
        /// Changes the timezone of the page.
        /// </summary>
        /// <param name="timezoneId">Timezone to set. See <seealso href="https://cs.chromium.org/chromium/src/third_party/icu/source/data/misc/metaZones.txt?rcl=faee8bc70570192d82d2978a71e2a615788597d1" >ICU’s `metaZones.txt`</seealso>
        /// for a list of supported timezone IDs. Passing `null` disables timezone emulation.</param>
        /// <returns>The viewport task.</returns>
        public async Task EmulateTimezoneAsync(string timezoneId)
        {
            try
            {
                await Client.SendAsync("Emulation.setTimezoneOverride", new EmulateTimezoneRequest
                {
                    TimezoneId = timezoneId ?? string.Empty
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex.Message.Contains("Invalid timezone"))
            {
                throw new PuppeteerException($"Invalid timezone ID: { timezoneId }");
            }
        }

        #endregion

        internal void OnPopup(Page popupPage) => Popup?.Invoke(this, new PopupEventArgs { PopupPage = popupPage });

        #region Private Method

        internal static async Task<Page> CreateAsync(
            CDPSession client,
            Target target,
            bool ignoreHTTPSErrors,
            ViewPortOptions defaultViewPort,
            TaskQueue screenshotTaskQueue)
        {
            var page = new Page(client, target, screenshotTaskQueue);
            await page.InitializeAsync(ignoreHTTPSErrors).ConfigureAwait(false);

            if (defaultViewPort != null)
            {
                await page.SetViewportAsync(defaultViewPort).ConfigureAwait(false);
            }

            return page;
        }

        private async Task InitializeAsync(bool ignoreHTTPSErrors)
        {
            FrameManager = await FrameManager.CreateFrameManagerAsync(Client, this, ignoreHTTPSErrors, _timeoutSettings).ConfigureAwait(false);
            var networkManager = FrameManager.NetworkManager;

            Client.MessageReceived += Client_MessageReceived;
            FrameManager.FrameAttached += (sender, e) => FrameAttached?.Invoke(this, e);
            FrameManager.FrameDetached += (sender, e) => FrameDetached?.Invoke(this, e);
            FrameManager.FrameNavigated += (sender, e) => FrameNavigated?.Invoke(this, e);

            networkManager.Request += (sender, e) => Request?.Invoke(this, e);
            networkManager.RequestFailed += (sender, e) => RequestFailed?.Invoke(this, e);
            networkManager.Response += (sender, e) => Response?.Invoke(this, e);
            networkManager.RequestFinished += (sender, e) => RequestFinished?.Invoke(this, e);

            await Task.WhenAll(
               Client.SendAsync("Target.setAutoAttach", new TargetSetAutoAttachRequest
               {
                   AutoAttach = true,
                   WaitForDebuggerOnStart = false,
                   Flatten = true
               }),
               Client.SendAsync("Performance.enable", null),
               Client.SendAsync("Log.enable", null)
           ).ConfigureAwait(false);

            try
            {
                await Client.SendAsync("Page.setInterceptFileChooserDialog", new PageSetInterceptFileChooserDialog
                {
                    Enabled = true
                }).ConfigureAwait(false);
            }
            catch
            {
                _fileChooserInterceptionIsDisabled = true;
            }
        }

        private async Task<Response> GoAsync(int delta, NavigationOptions options)
        {
            var history = await Client.SendAsync<PageGetNavigationHistoryResponse>("Page.getNavigationHistory").ConfigureAwait(false);

            if (history.Entries.Count <= history.CurrentIndex + delta)
            {
                return null;
            }
            var entry = history.Entries[history.CurrentIndex + delta];
            var waitTask = WaitForNavigationAsync(options);

            await Task.WhenAll(
                waitTask,
                Client.SendAsync("Page.navigateToHistoryEntry", new PageNavigateToHistoryEntryRequest
                {
                    EntryId = entry.Id
                })
            ).ConfigureAwait(false);

            return waitTask.Result;
        }

        private Dictionary<string, decimal> BuildMetricsObject(List<Metric> metrics)
        {
            var result = new Dictionary<string, decimal>();

            foreach (var item in metrics)
            {
                if (SupportedMetrics.Contains(item.Name))
                {
                    result.Add(item.Name, item.Value);
                }
            }

            return result;
        }

        private async Task<string> PerformScreenshot(ScreenshotType type, ScreenshotOptions options)
        {
            if (!_screenshotBurstModeOn)
            {
                await Client.SendAsync("Target.activateTarget", new TargetActivateTargetRequest
                {
                    TargetId = Target.TargetId
                }).ConfigureAwait(false);
            }

            var clip = options.Clip != null ? ProcessClip(options.Clip) : null;

            if (!_screenshotBurstModeOn)
            {
                if (options != null && options.FullPage)
                {
                    var metrics = _screenshotBurstModeOn
                        ? _burstModeMetrics :
                        await Client.SendAsync<PageGetLayoutMetricsResponse>("Page.getLayoutMetrics").ConfigureAwait(false);

                    if (options.BurstMode)
                    {
                        _burstModeMetrics = metrics;
                    }

                    var contentSize = metrics.ContentSize;

                    var width = Convert.ToInt32(Math.Ceiling(contentSize.Width));
                    var height = Convert.ToInt32(Math.Ceiling(contentSize.Height));

                    // Overwrite clip for full page at all times.
                    clip = new Clip
                    {
                        X = 0,
                        Y = 0,
                        Width = width,
                        Height = height,
                        Scale = 1
                    };

                    var isMobile = Viewport?.IsMobile ?? false;
                    var deviceScaleFactor = Viewport?.DeviceScaleFactor ?? 1;
                    var isLandscape = Viewport?.IsLandscape ?? false;
                    var screenOrientation = isLandscape ?
                        new ScreenOrientation
                        {
                            Angle = 90,
                            Type = ScreenOrientationType.LandscapePrimary
                        } :
                        new ScreenOrientation
                        {
                            Angle = 0,
                            Type = ScreenOrientationType.PortraitPrimary
                        };

                    await Client.SendAsync("Emulation.setDeviceMetricsOverride", new EmulationSetDeviceMetricsOverrideRequest
                    {
                        Mobile = isMobile,
                        Width = width,
                        Height = height,
                        DeviceScaleFactor = deviceScaleFactor,
                        ScreenOrientation = screenOrientation
                    }).ConfigureAwait(false);
                }

                if (options?.OmitBackground == true && type == ScreenshotType.Png)
                {
                    await Client.SendAsync("Emulation.setDefaultBackgroundColorOverride", new EmulationSetDefaultBackgroundColorOverrideRequest
                    {
                        Color = new EmulationSetDefaultBackgroundColorOverrideColor
                        {
                            R = 0,
                            G = 0,
                            B = 0,
                            A = 0
                        }
                    }).ConfigureAwait(false);
                }
            }

            var screenMessage = new PageCaptureScreenshotRequest
            {
                Format = type.ToString().ToLower()
            };

            if (options.Quality.HasValue)
            {
                screenMessage.Quality = options.Quality.Value;
            }

            if (clip != null)
            {
                screenMessage.Clip = clip;
            }

            var result = await Client.SendAsync<PageCaptureScreenshotResponse>("Page.captureScreenshot", screenMessage).ConfigureAwait(false);

            if (options.BurstMode)
            {
                _screenshotBurstModeOptions = options;
                _screenshotBurstModeOn = true;
            }
            else
            {
                await ResetBackgroundColorAndViewportAsync(options).ConfigureAwait(false);
            }
            return result.Data;
        }

        private Clip ProcessClip(Clip clip)
        {
            var x = Math.Round(clip.X);
            var y = Math.Round(clip.Y);

            return new Clip
            {
                X = x,
                Y = y,
                Width = Math.Round(clip.Width + clip.X - x, MidpointRounding.AwayFromZero),
                Height = Math.Round(clip.Height + clip.Y - y, MidpointRounding.AwayFromZero),
                Scale = 1
            };
        }

        private Task ResetBackgroundColorAndViewportAsync(ScreenshotOptions options)
        {
            var omitBackgroundTask = options?.OmitBackground == true && options.Type == ScreenshotType.Png ?
                Client.SendAsync("Emulation.setDefaultBackgroundColorOverride") : Task.CompletedTask;
            var setViewPortTask = (options?.FullPage == true && Viewport != null) ?
                SetViewportAsync(Viewport) : Task.CompletedTask;
            return Task.WhenAll(omitBackgroundTask, setViewPortTask);
        }

        private decimal ConvertPrintParameterToInches(object parameter)
        {
            if (parameter == null)
            {
                return 0;
            }

            decimal pixels;
            if (parameter is decimal || parameter is int)
            {
                pixels = Convert.ToDecimal(parameter);
            }
            else
            {
                var text = parameter.ToString();
                var unit = text.Substring(text.Length - 2).ToLower();
                string valueText;
                if (_unitToPixels.ContainsKey(unit))
                {
                    valueText = text.Substring(0, text.Length - 2);
                }
                else
                {
                    // In case of unknown unit try to parse the whole parameter as number of pixels.
                    // This is consistent with phantom's paperSize behavior.
                    unit = "px";
                    valueText = text;
                }

                if (decimal.TryParse(valueText, NumberStyles.Any, CultureInfo.InvariantCulture.NumberFormat, out var number))
                {
                    pixels = number * _unitToPixels[unit];
                }
                else
                {
                    throw new ArgumentException($"Failed to parse parameter value: '{text}'", nameof(parameter));
                }
            }

            return pixels / 96;
        }

        private async void Client_MessageReceived(object sender, MessageEventArgs e)
        {
            try
            {
                switch (e.MessageID)
                {
                    case "Page.domContentEventFired":
                        DOMContentLoaded?.Invoke(this, EventArgs.Empty);
                        break;
                    case "Page.loadEventFired":
                        Load?.Invoke(this, EventArgs.Empty);
                        break;
                    //case "Runtime.consoleAPICalled":
                    //    await OnConsoleAPIAsync(e.MessageData.ToObject<PageConsoleResponse>(true)).ConfigureAwait(false);
                    //    break;
                    case "Page.javascriptDialogOpening":
                        OnDialog(e.MessageData.ToObject<PageJavascriptDialogOpeningResponse>(true));
                        break;
                    case "Runtime.exceptionThrown":
                        HandleException(e.MessageData.ToObject<RuntimeExceptionThrownResponse>(true).ExceptionDetails);
                        break;
                    case "Inspector.targetCrashed":
                        OnTargetCrashed();
                        break;
                    case "Performance.metrics":
                        EmitMetrics(e.MessageData.ToObject<PerformanceMetricsResponse>(true));
                        break;
                    case "Target.attachedToTarget":
                        await OnAttachedToTargetAsync(e.MessageData.ToObject<TargetAttachedToTargetResponse>(true)).ConfigureAwait(false);
                        break;
                    case "Target.detachedFromTarget":
                        OnDetachedFromTarget(e.MessageData.ToObject<TargetDetachedFromTargetResponse>(true));
                        break;
                    case "Log.entryAdded":
                        await OnLogEntryAddedAsync(e.MessageData.ToObject<LogEntryAddedResponse>(true)).ConfigureAwait(false);
                        break;
                    case "Runtime.bindingCalled":
                        await OnBindingCalled(e.MessageData.ToObject<BindingCalledResponse>(true)).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                var message = $"Page failed to process {e.MessageID}. {ex.Message}. {ex.StackTrace}";
                Client.Close(message);
            }
        }

            private async Task OnBindingCalled(BindingCalledResponse e)
        {
            string expression;
            try
            {
                var result = await ExecuteBinding(e).ConfigureAwait(false);

                expression = EvaluationString(
                    @"function deliverResult(name, seq, result) {
                        window[name]['callbacks'].get(seq).resolve(result);
                        window[name]['callbacks'].delete(seq);
                    }",
                    e.BindingPayload.Name,
                    e.BindingPayload.Seq,
                    result);
            }
            catch (Exception ex)
            {
                if (ex is TargetInvocationException)
                {
                    ex = ex.InnerException;
                }

                expression = EvaluationString(
                    @"function deliverError(name, seq, message, stack) {
                        const error = new Error(message);
                        error.stack = stack;
                        window[name]['callbacks'].get(seq).reject(error);
                        window[name]['callbacks'].delete(seq);
                    }",
                    e.BindingPayload.Name,
                    e.BindingPayload.Seq,
                    ex.Message,
                    ex.StackTrace);
            }

            Client.Send("Runtime.evaluate", new
            {
                expression,
                contextId = e.ExecutionContextId
            });
        }

        private async Task<object> ExecuteBinding(BindingCalledResponse e)
        {
            const string taskResultPropertyName = "Result";
            object result;
            var binding = _pageBindings[e.BindingPayload.Name];
            var methodParams = binding.Method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

            var args = e.BindingPayload.Args.Select((token, i) => token.ToObject(methodParams[i])).ToArray();

            result = binding.DynamicInvoke(args);
            if (result is Task taskResult)
            {
                await taskResult.ConfigureAwait(false);

                if (taskResult.GetType().IsGenericType)
                {
                    // the task is already awaited and therefore the call to property Result will not deadlock
                    result = taskResult.GetType().GetProperty(taskResultPropertyName).GetValue(taskResult);
                }
            }

            return result;
        }

        private void OnDetachedFromTarget(TargetDetachedFromTargetResponse e)
        {
            var sessionId = e.SessionId;
            //if (_workers.TryGetValue(sessionId, out var worker))
            //{
            //    WorkerDestroyed?.Invoke(this, new WorkerEventArgs(worker));
            //    _workers.Remove(sessionId);
            //}
        }

        private async Task OnAttachedToTargetAsync(TargetAttachedToTargetResponse e)
        {
            var targetInfo = e.TargetInfo;
            var sessionId = e.SessionId;
            if (targetInfo.Type != TargetType.Worker)
            {
                try
                {
                    await Client.SendAsync("Target.detachFromTarget", new TargetDetachFromTargetRequest
                    {
                        SessionId = sessionId
                    }).ConfigureAwait(false);
                }
                catch { }
                return;
            }

            //var session = Connection.FromSession(Client).GetSession(sessionId);
            //var worker = new Worker(session, targetInfo.Url, AddConsoleMessageAsync, HandleException);
            //_workers[sessionId] = worker;
            //WorkerCreated?.Invoke(this, new WorkerEventArgs(worker));
        }

        private async Task OnLogEntryAddedAsync(LogEntryAddedResponse e)
        {
            if (e.Entry.Args != null)
            {
                foreach (var arg in e.Entry?.Args)
                {
                    await RemoteObjectHelper.ReleaseObjectAsync(Client, arg).ConfigureAwait(false);
                }
            }
            //if (e.Entry.Source != TargetType.Worker)
            //{
            //    Console?.Invoke(this, new ConsoleEventArgs(new ConsoleMessage(
            //        e.Entry.Level,
            //        e.Entry.Text,
            //        null,
            //        new ConsoleMessageLocation
            //        {
            //            URL = e.Entry.URL,
            //            LineNumber = e.Entry.LineNumber
            //        })));
            //}
        }

        private void OnTargetCrashed()
        {
            if (Error == null)
            {
                throw new TargetCrashedException();
            }

            Error.Invoke(this, new ErrorEventArgs("Page crashed!"));
        }

        private void EmitMetrics(PerformanceMetricsResponse metrics)
            => Metrics?.Invoke(this, new MetricEventArgs(metrics.Title, BuildMetricsObject(metrics.Metrics)));

        private void HandleException(EvaluateExceptionResponseDetails exceptionDetails)
            => PageError?.Invoke(this, new PageErrorEventArgs(GetExceptionMessage(exceptionDetails)));

        private string GetExceptionMessage(EvaluateExceptionResponseDetails exceptionDetails)
        {
            if (exceptionDetails.Exception != null)
            {
                return exceptionDetails.Exception.Description;
            }
            var message = exceptionDetails.Text;
            if (exceptionDetails.StackTrace != null)
            {
                foreach (var callframe in exceptionDetails.StackTrace.CallFrames)
                {
                    var location = $"{callframe.Url}:{callframe.LineNumber}:{callframe.ColumnNumber}";
                    var functionName = callframe.FunctionName ?? "<anonymous>";
                    message += $"\n at {functionName} ({location})";
                }
            }
            return message;
        }

        private void OnDialog(PageJavascriptDialogOpeningResponse message)
        {
            var dialog = new Dialog(Client, message.Type, message.Message, message.DefaultPrompt);
            Dialog?.Invoke(this, new DialogEventArgs(dialog));
        }

        //private Task OnConsoleAPIAsync(PageConsoleResponse message)
        //{
        //    if (message.ExecutionContextId == 0)
        //    {
        //        return Task.CompletedTask;
        //    }
        //    var ctx = FrameManager.ExecutionContextById(message.ExecutionContextId);
        //    var values = message.Args.Select(ctx.CreateJSHandle).ToArray();

        //    return AddConsoleMessageAsync(message.Type, values, message.StackTrace);
        //}

        //private async Task AddConsoleMessageAsync(ConsoleType type, JSHandle[] values, Messaging.StackTrace stackTrace)
        //{
        //    if (Console?.GetInvocationList().Length == 0)
        //    {
        //        await Task.WhenAll(values.Select(v => RemoteObjectHelper.ReleaseObjectAsync(Client, v.RemoteObject))).ConfigureAwait(false);
        //        return;
        //    }

        //    var tokens = values.Select(i => i.RemoteObject.ObjectId != null
        //        ? i.ToString()
        //        : RemoteObjectHelper.ValueFromRemoteObject<string>(i.RemoteObject));

        //    var location = new ConsoleMessageLocation();
        //    if (stackTrace?.CallFrames?.Length > 0)
        //    {
        //        var callFrame = stackTrace.CallFrames[0];
        //        location.URL = callFrame.URL;
        //        location.LineNumber = callFrame.LineNumber;
        //        location.ColumnNumber = callFrame.ColumnNumber;
        //    }

        //    var consoleMessage = new ConsoleMessage(type, string.Join(" ", tokens), values, location);
        //    Console?.Invoke(this, new ConsoleEventArgs(consoleMessage));
        //}

        private async Task ExposeFunctionAsync(string name, Delegate puppeteerFunction)
        {
            if (_pageBindings.ContainsKey(name))
            {
                throw new PuppeteerException($"Failed to add page binding with name {name}: window['{name}'] already exists!");
            }
            _pageBindings.Add(name, puppeteerFunction);

            const string addPageBinding = @"function addPageBinding(bindingName) {
              const binding = window[bindingName];
              window[bindingName] = (...args) => {
                const me = window[bindingName];
                let callbacks = me['callbacks'];
                if (!callbacks) {
                  callbacks = new Map();
                  me['callbacks'] = callbacks;
                }
                const seq = (me['lastSeq'] || 0) + 1;
                me['lastSeq'] = seq;
                const promise = new Promise((resolve, reject) => callbacks.set(seq, {resolve, reject}));
                binding(JSON.stringify({name: bindingName, seq, args}));
                return promise;
              };
            }";
            var expression = EvaluationString(addPageBinding, name);
            await Client.SendAsync("Runtime.addBinding", new RuntimeAddBindingRequest { Name = name }).ConfigureAwait(false);
            await Client.SendAsync("Page.addScriptToEvaluateOnNewDocument", new PageAddScriptToEvaluateOnNewDocumentRequest
            {
                Source = expression
            }).ConfigureAwait(false);

            await Task.WhenAll(Frames.Select(frame => frame.EvaluateExpressionAsync(expression)
                .ContinueWith(task =>
                {

                }))).ConfigureAwait(false);
        }

        private static string EvaluationString(string fun, params object[] args)
        {
            return $"({fun})({string.Join(",", args.Select(SerializeArgument))})";

            string SerializeArgument(object arg)
            {
                return "undefined";
                //return arg == null
                //    ? "undefined"
                //    : JsonConvert.SerializeObject(arg, JsonHelper.DefaultJsonSerializerSettings);
            }
        }
        #endregion

        #region IDisposable

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases all resource used by the <see cref="Page"/> object by calling the <see cref="CloseAsync"/> method.
        /// </summary>
        /// <remarks>Call <see cref="Dispose()"/> when you are finished using the <see cref="Page"/>. The
        /// <see cref="Dispose()"/> method leaves the <see cref="Page"/> in an unusable state. After
        /// calling <see cref="Dispose()"/>, you must release all references to the <see cref="Page"/> so
        /// the garbage collector can reclaim the memory that the <see cref="Page"/> was occupying.</remarks>
        /// <param name="disposing">Indicates whether disposal was initiated by <see cref="Dispose()"/> operation.</param>
        protected virtual void Dispose(bool disposing) => _ = CloseAsync();
        #endregion
    }
}
