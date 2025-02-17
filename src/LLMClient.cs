using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AIDA
{
    public class LLMClient
    {
        public static async Task<JObject> CallAsync(JArray messages, JArray tools)
        {
            HttpRequestMessage req = new HttpRequestMessage();
            req.Method = HttpMethod.Post;
            req.RequestUri = new Uri("http://localhost:11434/api/chat");

            //Construct body
            JObject body = new JObject();
            body.Add("model", "qwen2.5:0.5b");
            body.Add("stream", false);
            body.Add("messages", messages);
            if (tools.Count > 0)
            {
                body.Add("tools", tools);
            }

            //Add body to request
            req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            
            //Make HTTP calls
            HttpClient hc = new HttpClient();
            HttpResponseMessage resp = await hc.SendAsync(req);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Call to model failed with code '" + resp.StatusCode.ToString() + "'. Msg: " + content);
            }
            
            //Strip out message portion
            JObject contentjo = JObject.Parse(content);
            JProperty? prop_message = contentjo.Property("message");
            if (prop_message == null)
            {
                throw new Exception("Property 'message' not in model's response.");
            }
            JObject ToReturn = (JObject)prop_message.Value;
            return ToReturn;
        }
    }
}