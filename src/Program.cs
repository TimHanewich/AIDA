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
            //Confirm ollama is running
            Console.Write("Confirming Ollama is running locally... ");
            bool OllamaRunning = await LLMClient.ConfirmOllamaRunning();
            if (!OllamaRunning)
            {
                Console.WriteLine("Ollama was not confirmed to be running! Make sure it is installed and running.");
                return;
            }
            Console.WriteLine("Ollama is running!");


            //Construct lists that are needed
            JArray messages = new JArray();
            JArray tools = new JArray();
            
            //Add tools
            JObject GetTemperature = JObject.Parse("{\"type\":\"function\",\"function\": {\"name\": \"get_temperature\",\"description\": \"Gets the temperature for the user's current location.\",\"parameters\": {}}}");
            tools.Add(GetTemperature);

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

                //Send to model
                PromptModel:
                Console.Write("Thinking...");
                JObject ModelResponse = await LLMClient.CallAsync(messages, tools);
                Console.WriteLine();

                //Show messages
                Console.WriteLine("MESSAGES: ");
                Console.WriteLine(messages.ToString());
                Console.Write("Enter to continue...");
                Console.ReadLine();

                //SHOW RESPONSE
                Console.WriteLine("Model Response: ");
                Console.WriteLine(ModelResponse.ToString());
                Console.Write("Enter to continue.");
                Console.ReadLine();

                //Is the response a tool call?
                JToken? tool_calls = ModelResponse.SelectToken("tool_calls");
                if (tool_calls != null) //there was a tool call!
                {
                    //Add the tool call message to the list of messages
                    messages.Add(ModelResponse);

                    //Loop through all tool calls, gather data, or handle accordingly.
                    JArray tcs = (JArray)tool_calls;
                    foreach (JObject tool_call in tcs) //loop through each tool call
                    {
                        //Select name
                        string name = "";
                        JToken? tool_name = tool_call.SelectToken("function.name");
                        if (tool_name != null){name = tool_name.ToString();}

                        if (name == "get_temperature")
                        {

                            //Call to API  
                            Console.WriteLine("Querying temperature...");
                            HttpClient hc = new HttpClient();
                            HttpResponseMessage resp = await hc.GetAsync("https://api.open-meteo.com/v1/forecast?latitude=27.190&longitude=-82.454&current=temperature_2m&temperature_unit=fahrenheit");
                            string ResponseContent = await resp.Content.ReadAsStringAsync();
                            if (resp.StatusCode != System.Net.HttpStatusCode.OK)
                            {
                                Console.WriteLine("Unable to get weather data from API! Content: " + ResponseContent);
                            }
                            Console.WriteLine("API call for temperature complete.");

                            //Add response to message array
                            JObject TemperatureDataForModel = new JObject();
                            TemperatureDataForModel.Add("role", "tool");
                            TemperatureDataForModel.Add("content", ResponseContent.ToString());
                            messages.Add(TemperatureDataForModel);
                        }

                    }

                    //Now that each tool call was handled and the result (or data) of each call is now in the message array, prompt the model again so it can respond in natural language!
                    goto PromptModel;
                }
                else //it is noraml content
                {
                    
                    //Parse out and print the response
                    JProperty? content = ModelResponse.Property("content");
                    if (content != null)
                    {
                        Console.WriteLine();
                        Console.WriteLine(content.Value.ToString());
                        Console.WriteLine();
                    }

                    //Add the model's response to the messages
                    messages.Append(ModelResponse);
                    
                }
  
                
            }
        }
    }
}