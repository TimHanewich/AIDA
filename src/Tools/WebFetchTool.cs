using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;
using System.Net;
using HtmlAgilityPack;

namespace AIDA
{
    public class WebFetchTool : ExecutableFunction
    {
        public WebFetchTool()
        {
            Name = "web_fetch";
            Description = "Make HTTP GET call to retrieve the contents of a URL endpoint (i.e. a webpage or document). Use this tool if the user asks you to read a webpage or retrieve something specific.";
            InputParameters.Add(new FunctionInputParameter("url", "The specific URL to GET."));
        }

        public override async Task<string> ExecuteAsync(JObject? arguments = null)
        {
            string? url = null;
            if (arguments != null)
            {
                JProperty? prop = arguments.Property("url");
                if (prop != null) url = prop.Value.ToString();
            }

            if (url == null)
            {
                return "Unable to read webpage because the 'url' parameter was not provided.";
            }

            AnsiConsole.Markup("[gray][italic]reading '" + Markup.Escape(url) + "'... [/][/]");

            try
            {
                HttpClient hc = new HttpClient();
                HttpRequestMessage req = new HttpRequestMessage();
                req.Method = HttpMethod.Get;
                req.RequestUri = new Uri(url);
                req.Headers.Add("User-Agent", "AIDA/1.0.0");
                hc.Timeout = new TimeSpan(0, 1, 0);
                HttpResponseMessage resp = await hc.SendAsync(req);

                if (resp.StatusCode != HttpStatusCode.OK)
                {
                    AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                    return "Attempt to read the web page came back with status code '" + resp.StatusCode.ToString() + "', so unfortunately it cannot be read (wasn't 200 OK)";
                }

                string content = await resp.Content.ReadAsStringAsync();
                HtmlDocument doc = new HtmlDocument();
                doc.LoadHtml(content);
                string PlainText = doc.DocumentNode.InnerText;

                AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
                return PlainText;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[gray][italic]failed[/][/]");
                return "Failed to read webpage: " + ex.Message;
            }
        }
    }
}
