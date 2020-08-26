using PuppeteerSharp.Helpers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PuppeteerSharp
{
    /// <summary>
    /// Target.
    /// </summary>
    [DebuggerDisplay("Target {Type} - {Url}")]
    public class Target
    {
        #region Private members
        private readonly Func<Task<CDPSession>> _sessionFactory;
        private readonly TaskCompletionSource<bool> _initializedTaskWrapper = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        #endregion

        internal bool IsInitialized { get; set; }
        internal TargetInfo TargetInfo { get; set; }

        internal Target(
            TargetInfo targetInfo,
            Func<Task<CDPSession>> sessionFactory,
            BrowserContext browserContext)
        {
            TargetInfo = targetInfo;
            _sessionFactory = sessionFactory;
            BrowserContext = browserContext;
            PageTask = null;

            _ = _initializedTaskWrapper.Task.ContinueWith(async initializedTask =>
            {
                var success = initializedTask.Result;
                if (!success)
                {
                    return;
                }

                var openerPageTask = Opener?.PageTask;
                if (openerPageTask == null || Type != TargetType.Page)
                {
                    return;
                }
                var openerPage = await openerPageTask.ConfigureAwait(false);
                if (!openerPage.HasPopupEventListeners)
                {
                    return;
                }
                var popupPage = await PageAsync().ConfigureAwait(false);
                openerPage.OnPopup(popupPage);
            });

            CloseTaskWrapper = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsInitialized = TargetInfo.Type != TargetType.Page || !string.IsNullOrEmpty(TargetInfo.Url);

            if (IsInitialized)
            {
                _initializedTaskWrapper.TrySetResult(true);
            }
        }

        #region Properties

        /// <summary>
        /// Gets the type. It will be <see cref="TargetInfo.Type"/>.
        /// Can be `"page"`, `"background_page"`, `"service_worker"`, `"shared_worker"`, `"browser"` or `"other"`.
        /// </summary>
        /// <value>The type.</value>
        public TargetType Type => TargetInfo.Type;

        /// <summary>
        /// Gets the target identifier.
        /// </summary>
        /// <value>The target identifier.</value>
        public string TargetId => TargetInfo.TargetId;

        /// <summary>
        /// Get the target that opened this target
        /// </summary>
        /// <remarks>
        /// Top-level targets return <c>null</c>.
        /// </remarks>
        public Target Opener => TargetInfo.OpenerId != null ?
            Browser.TargetsMap.GetValueOrDefault(TargetInfo.OpenerId) : null;

        /// <summary>
        /// Get the browser the target belongs to.
        /// </summary>
        public Browser Browser => BrowserContext.Browser;

        /// <summary>
        /// Get the browser context the target belongs to.
        /// </summary>
        public BrowserContext BrowserContext { get; }

        internal Task<bool> InitializedTask => _initializedTaskWrapper.Task;
        internal Task CloseTask => CloseTaskWrapper.Task;
        internal TaskCompletionSource<bool> CloseTaskWrapper { get; }
        internal Task<Page> PageTask { get; set; }
        #endregion

        /// <summary>
        /// Returns the <see cref="Page"/> associated with the target. If the target is not <c>"page"</c> or <c>"background_page"</c> returns <c>null</c>
        /// </summary>
        /// <returns>a task that returns a <see cref="Page"/></returns>
        public Task<Page> PageAsync()
        {
            if ((TargetInfo.Type == TargetType.Page || TargetInfo.Type == TargetType.BackgroundPage) && PageTask == null)
            {
                PageTask = CreatePageAsync();
            }

            return PageTask ?? Task.FromResult<Page>(null);
        }

        private async Task<Page> CreatePageAsync()
        {
            var session = await _sessionFactory().ConfigureAwait(false);
            return await Page.CreateAsync(session, this, Browser.IgnoreHTTPSErrors, Browser.DefaultViewport, Browser.ScreenshotTaskQueue).ConfigureAwait(false);
        }

        internal void TargetInfoChanged(TargetInfo targetInfo)
        {
            var previousUrl = TargetInfo.Url;
            TargetInfo = targetInfo;

            if (!IsInitialized && (TargetInfo.Type != TargetType.Page || !string.IsNullOrEmpty(TargetInfo.Url)))
            {
                IsInitialized = true;
                _initializedTaskWrapper.TrySetResult(true);
                return;
            }

            if (previousUrl != targetInfo.Url)
            {
                Browser.ChangeTarget(this);
            }
        }
    }
}
