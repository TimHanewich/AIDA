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
            Console.WriteLine("Hello!");

            JArray messages = new JArray();
            JArray tools = new JArray();

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

                //Send to model
                Console.Write("Thinking...");
                messages.Add(NewMsg);
                JObject ModelResponse = await LLMClient.CallAsync(messages, tools);
                Console.WriteLine();

                //Is the response a tool call?
                if (ModelResponse.Property("tool_calls") != null) //there was a tool call!
                {

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