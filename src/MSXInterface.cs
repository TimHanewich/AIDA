using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIDA
{
    public class MSXInterface
    {
        //Cookie
        private string COOKIE;

        //API URLS
        private string URL_ROOT;
        private string URL_ACCOUNTS;
        private string URL_OPPORTUNITIES;
        private string URL_TASKS;

        public MSXInterface(string cookie)
        {
            if (cookie == string.Empty)
            {
                throw new Exception("Cookie cannot be blank!");
            }
            COOKIE = cookie;

            URL_ROOT = "https://microsoftsales.crm.dynamics.com/api/data/v9.2/";
            URL_ACCOUNTS = URL_ROOT + "accounts";
            URL_OPPORTUNITIES = URL_ROOT + "opportunities";
            URL_TASKS = URL_ROOT + "tasks";
        }

        private HttpRequestMessage PrepareHttpRequestMessage()
        {
            HttpRequestMessage req = new HttpRequestMessage();
            req.Headers.Add("cookie", COOKIE);
            return req;
        }

        private async Task<HttpResponseMessage> HttpGetAsync(string url)
        {
            HttpRequestMessage req = PrepareHttpRequestMessage();
            req.RequestUri = new Uri(url);
            HttpClient hc = new HttpClient();
            HttpResponseMessage msg = await hc.SendAsync(req);
            return msg;
        }

        public async Task<JArray> SearchAccountsAsync(string search_term)
        {
            string url = URL_ACCOUNTS + "?$filter=contains(name, '" + search_term + "')";
            HttpResponseMessage resp = await HttpGetAsync(url);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Searching accounts returned code " + resp.StatusCode.ToString() + "! Msg: " + content);   
            }
            
            //Parse
            JObject jo = JObject.Parse(content);

            JArray sourceArray = (JArray)jo["value"];
            JArray result = new JArray();

            foreach (JObject account in sourceArray)
            {
                JObject trimmed = new JObject
                {
                    ["name"]      = account["name"],
                    ["accountid"] = account["accountid"]
                };
                result.Add(trimmed);
            }

            return result;
        }
        
        public async Task<JArray> SearchOpportunitiesAsync(string accountid, string title_search_term)
        {
            string url = URL_OPPORTUNITIES + "?$filter=";
            url = url + "_parentaccountid_value eq '" + accountid + "'"; //for an account
            url = url + " and statecode eq 0"; //must be an open opportunitiy
            url = url + " and contains(name, '" + title_search_term + "')"; //search term
            
            //Make call
            HttpResponseMessage resp = await HttpGetAsync(url);
            string content = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception("Searching accounts returned code " + resp.StatusCode.ToString() + "! Msg: " + content);   
            }

            //Parse
            JObject jo = JObject.Parse(content);

            JArray sourceArray = (JArray)jo["value"];
            JArray result = new JArray();

            foreach (JObject opportunity in sourceArray)
            {
                JObject trimmed = new JObject
                {
                    ["opportunityid"]  = opportunity["opportunityid"],
                    ["name"]           = opportunity["name"],
                    ["description"]    = opportunity["description"],
                    ["estimatedvalue"] = opportunity["estimatedvalue"]
                };
                result.Add(trimmed);
            }

            return result;
    
        }
    
        public async Task CreateTaskAsync(string title, string description, DateTime timestamp, string? tiedto_account = null, string? tiedto_opportunity = null)
        {
            JObject body = new JObject();
            body.Add("subject", title);
            body.Add("description", description);
            body.Add("scheduledstart", timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            
            //attach to account?
            if (tiedto_account != null)
            {
                body.Add("regardingobjectid_account@odata.bind", "/accounts(" + tiedto_account + ")");
            }
            else if (tiedto_opportunity != null)
            {
                body.Add("regardingobjectid_opportunity@odata.bind", "/opportunities(" + tiedto_opportunity + ")");
            }

            //Make the post
            HttpRequestMessage req = PrepareHttpRequestMessage();
            req.Method = HttpMethod.Post;
            req.RequestUri = new Uri(URL_TASKS);
            req.Content = new StringContent(body.ToString(), System.Text.Encoding.UTF8, "application/json");
            HttpClient hc = new HttpClient();
            HttpResponseMessage resp = await hc.SendAsync(req);
            if (resp.StatusCode != System.Net.HttpStatusCode.NoContent)
            {
                string content = await resp.Content.ReadAsStringAsync();
                throw new Exception("Creation of task returned code " + resp.StatusCode.ToString() + ". Msg: " + content);
            }
        }
    }
}