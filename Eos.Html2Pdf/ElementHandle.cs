using PuppeteerSharp.Messaging;

namespace PuppeteerSharp
{
    /// <summary>
    /// Inherits from <see cref="JSHandle"/>. It represents an in-page DOM element. 
    /// ElementHandles can be created by <see cref="PuppeteerSharp.Page.QuerySelectorAsync(string)"/> or <see cref="PuppeteerSharp.Page.QuerySelectorAllAsync(string)"/>.
    /// </summary>
    public class ElementHandle : JSHandle
    {
        internal ElementHandle(
            ExecutionContext context,
            CDPSession client,
            RemoteObject remoteObject)
            : base(context, client, remoteObject) { }
    }
}
