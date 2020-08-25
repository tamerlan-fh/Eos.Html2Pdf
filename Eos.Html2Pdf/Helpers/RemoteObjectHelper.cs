using Newtonsoft.Json.Linq;
using PuppeteerSharp.Helpers.Json;
using PuppeteerSharp.Messaging;
using System;
using System.Numerics;
using System.Threading.Tasks;

namespace PuppeteerSharp.Helpers
{
    internal class RemoteObjectHelper
    {
        internal static object ValueFromRemoteObject<T>(RemoteObject remoteObject)
        {
            var unserializableValue = remoteObject.UnserializableValue;

            if (unserializableValue != null)
            {
                return ValueFromUnserializableValue(remoteObject, unserializableValue);
            }

            var value = remoteObject.Value;

            if (value == null)
            {
                return default(T);
            }

            return typeof(T) == typeof(JToken) ? value : ValueFromType<T>(value, remoteObject.Type);
        }

        private static object ValueFromType<T>(JToken value, RemoteObjectType objectType)
        {
            switch (objectType)
            {
                case RemoteObjectType.Object:
                    return value.ToObject<T>(true);
                case RemoteObjectType.Undefined:
                    return null;
                case RemoteObjectType.Number:
                    return value.Value<T>();
                case RemoteObjectType.Boolean:
                    return value.Value<bool>();
                case RemoteObjectType.Bigint:
                    return value.Value<double>();
                default: // string, symbol, function
                    return value.ToObject<T>();
            }
        }

        private static object ValueFromUnserializableValue(RemoteObject remoteObject, string unserializableValue)
        {
            if (remoteObject.Type == RemoteObjectType.Bigint &&
                                decimal.TryParse(remoteObject.UnserializableValue.Replace("n", string.Empty), out var decimalValue))
            {
                return new BigInteger(decimalValue);
            }
            switch (unserializableValue)
            {
                case "-0":
                    return -0;
                case "NaN":
                    return double.NaN;
                case "Infinity":
                    return double.PositiveInfinity;
                case "-Infinity":
                    return double.NegativeInfinity;
                default:
                    throw new Exception("Unsupported unserializable value: " + unserializableValue);
            }
        }

        internal static async Task ReleaseObjectAsync(CDPSession client, RemoteObject remoteObject)
        {
            if (remoteObject.ObjectId == null)
            {
                return;
            }

            try
            {
                await client.SendAsync("Runtime.releaseObject", new RuntimeReleaseObjectRequest
                {
                    ObjectId = remoteObject.ObjectId
                }).ConfigureAwait(false);
            }
            catch 
            {
            }
        }
    }
}