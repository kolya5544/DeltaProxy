using DeltaProxy;
using EmbedIO.Routing;
using EmbedIO.WebApi;
using EmbedIO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VKBridgeModule
{
    public class Controller : WebApiController
    {
        public static async Task AsText(IHttpContext context, object? data)
        {
            if (data is null)
            {
                // Send an empty response
                return;
            }

            context.Response.ContentType = MimeType.PlainText;
            using var text = context.OpenResponseText(Encoding.UTF8);
            // string.ToString returns the string itself
            await text.WriteAsync(data.ToString()).ConfigureAwait(false);
        }

        [Route(EmbedIO.HttpVerbs.Get, "/{id?}")]
        public async Task<string> GetCache(string id)
        {
            id = id.Trim(')');
            var i = VKBridgeModule.lc.cache.FirstOrDefault(z => z.ID == id);
            if (i is null) throw HttpException.NotFound();
            HttpContext.Redirect(i.fullURL);
            return "OK";
        }
    }
}
