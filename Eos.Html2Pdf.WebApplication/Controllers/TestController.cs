using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;

namespace Eos.Html2Pdf.WebApplication.Controllers
{
    public class TestController : ApiController
    {
        [HttpGet]
        [Route("api/test/get_pdf/")]
        public IHttpActionResult GetPdf()
        {
            var sourceFile = new FileInfo(HttpContext.Current.Server.MapPath("~/sources/ответ_0.html"));
            var sourceBytes = File.ReadAllBytes(sourceFile.FullName);
            var fileName = $"{Path.GetFileNameWithoutExtension(sourceFile.Name)}.pdf";
            var dataBytes = Html2PdfHelper.Convert(sourceBytes);
            return new PdfResult(new MemoryStream(dataBytes), Request, fileName);
        }

        public class PdfResult : IHttpActionResult
        {
            private MemoryStream memoryStream;
            private string fileName;
            private HttpRequestMessage httpRequestMessage;
            private HttpResponseMessage httpResponseMessage;

            public PdfResult(MemoryStream data, HttpRequestMessage request, string filename)
            {
                memoryStream = data;
                httpRequestMessage = request;
                fileName = filename;
            }

            public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
            {
                httpResponseMessage = httpRequestMessage.CreateResponse(HttpStatusCode.OK);
                httpResponseMessage.Content = new StreamContent(memoryStream);
                httpResponseMessage.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
                httpResponseMessage.Content.Headers.ContentDisposition.FileName = fileName;
                httpResponseMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                return Task.FromResult(httpResponseMessage);
            }
        }
    }
}
