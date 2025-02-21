using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIDA
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Chat().Wait();
        }

        public static async Task Chat()
        {

            //SETTINGS
            float latitude = 27.19f; //latitude of where the user is located (home)
            float longitude = -82.454f; //longitude of where the user is located (home)

            //Construct lists that are needed
            JArray messages = new JArray();
            JArray tools = new JArray();

            //Add system prompt
            JObject SystemPrompt = new JObject();
            SystemPrompt.Add("role", "system");
            SystemPrompt.Add("content", "You are AIDA, Artificial Intelligence Desktop Assistant. Your role is to be a friendly and helpful assistant. Speak in a playful, lighthearted, and fun manner.");
            messages.Add(SystemPrompt);
            
            //Add tools
            JObject GetTemperature = JObject.Parse("{\"type\":\"function\",\"function\": {\"name\": \"get_temperature\",\"description\": \"Gets the outside temperature.\",\"parameters\": {}}}");
            tools.Add(GetTemperature);
            JObject GetWindSpeed = JObject.Parse("{\"type\":\"function\",\"function\": {\"name\": \"get_wind_speed\",\"description\": \"Gets the outside wind speed.\",\"parameters\": {}}}");
            tools.Add(GetWindSpeed);

            //Infinite chat loop
            while (true)
            {
                //Input from user
                string? input = null;
                while (input == null)
                {
                    Console.Write("> ");
                    input = Console.ReadLine();
                }
                
                //Construct request
                JObject NewMsg = new JObject();
                NewMsg.Add("role", "user");
                NewMsg.Add("content", input);
                messages.Add(NewMsg);

                //Say thinking...
                PromptModel:
                Console.Write("Thinking...");

                //Call to LLM
                JObject ModelResponse;
                Console.WriteLine("Sending these messages to the model:");
                Console.WriteLine(messages.ToString());
                ModelResponse = await LLMClient.CallAsync(messages, tools);

                //Is there content?
                JProperty? content = ModelResponse.Property("content");
                if (content != null) //it is noraml content
                {
                    if (content.Value.ToString() != "")
                    {
                        Console.WriteLine();
                        Console.WriteLine(content.Value.ToString());
                        Console.WriteLine();
                    }
                }

                //Are there tool calls?
                JToken? tool_calls = ModelResponse.SelectToken("tool_calls");
                if (tool_calls != null) //there was a tool call!
                {

                    //Loop through all tool calls, gather data, or handle accordingly.
                    JArray tcs = (JArray)tool_calls;
                    foreach (JObject tool_call in tcs) //loop through each tool call
                    {
                        //Select name of the tool being called
                        string name = "";
                        JToken? tool_name = tool_call.SelectToken("function.name");
                        if (tool_name != null){name = tool_name.ToString();}

                        //Select ID of the tool call
                        string tool_call_id = "";
                        JProperty? prop_id = tool_call.Property("id");
                        if (prop_id != null){tool_call_id = prop_id.Value.ToString();}

                        //Handle get temperature function
                        if (name == "get_temperature")
                        {

                            //Call to API  
                            HttpClient hc = new HttpClient();
                            HttpResponseMessage resp = await hc.GetAsync("https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString() + "&longitude=" + longitude.ToString() + "&current=temperature_2m&temperature_unit=fahrenheit");
                            string ResponseContent = await resp.Content.ReadAsStringAsync();
                            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
                            {
                                Console.WriteLine("Unable to get weather data from API! Content: " + ResponseContent);
                            }

                            //Add response to message array
                            JObject TemperatureDataForModel = new JObject();
                            TemperatureDataForModel.Add("role", "tool");
                            TemperatureDataForModel.Add("tool_call_id", tool_call_id); //Add the ID of the tool call that this content is fulfilling.
                            TemperatureDataForModel.Add("content", ResponseContent.ToString());
                            messages.Add(TemperatureDataForModel);
                        }

                        //Handle get wind speed
                        if (name == "get_wind_speed")
                        {

                            //Call to API  
                            HttpClient hc = new HttpClient();
                            HttpResponseMessage resp = await hc.GetAsync("https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString() + "&longitude=" + longitude.ToString() + "&current=wind_speed_10m&wind_speed_unit=mph");
                            string ResponseContent = await resp.Content.ReadAsStringAsync();
                            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
                            {
                                Console.WriteLine("Unable to get wind speed data from API! Content: " + ResponseContent);
                            }

                            //Add response to message array
                            JObject WindSpeedDataForModel = new JObject();
                            WindSpeedDataForModel.Add("role", "tool");
                            WindSpeedDataForModel.Add("tool_call_id", tool_call_id); //Add the ID of the tool call that this content is fulfilling.
                            WindSpeedDataForModel.Add("content", ResponseContent.ToString());
                            messages.Add(WindSpeedDataForModel);
                        }

                    }

                    //Now that each tool call was handled and the result (or data) of each call is now in the message array, prompt the model again so it can respond in natural language!
                    goto PromptModel;
                }

                //Add the model's response to the messages
                messages.Add(ModelResponse);
  
            }
        }
    }
}