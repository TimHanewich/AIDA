using TimHanewich.AgentFramework;
using TimHanewich.Foundry.OpenAI.Responses;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class WaitTool : ExecutableFunction
    {
        public WaitTool()
        {
            Name = "wait";
            Description = "Wait (idle) for a period of time. Can be used reliably for both short and long term waits.";
            InputParameters.Add(new FunctionInputParameter("seconds", "The number of seconds to wait.", "integer"));
        }

        public override async Task<string> ExecuteAsync(JObject? arguments = null)
        {
            if (arguments == null)
            {
                return "Must provide arguments";
            }

            //Get wait time
            JProperty? prop_seconds = arguments.Property("seconds");
            if (prop_seconds == null)
            {
                return "You must provide the duration to wait, in seconds, as parameter 'seconds'.";
            }
            if (prop_seconds.Value.Type != JTokenType.Integer)
            {
                return "Provided argument 'seconds' not provided as integer. Must be an integer.";
            }
            int seconds = Convert.ToInt32(prop_seconds.Value.ToString());

            //Wait v2
            while (seconds > 0)
            {
                //Print
                int StartingLeft = Console.CursorLeft;
                string ToPrint = "waiting " + seconds.ToString("#,##0") + " seconds... ";
                AnsiConsole.Markup("[italic][gray]" + ToPrint + "[/][/]");

                //Wait
                await Task.Delay(1_000); //wait 1 second

                //Clear
                Console.CursorLeft = StartingLeft;
                Console.Write(new string(' ', ToPrint.Length));
                Console.CursorLeft = StartingLeft;

                //Decrement
                seconds = seconds - 1;
            }

            //Return
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return "Successfully waited " + seconds.ToString("#,##0") + " seconds. It is now " + seconds.ToString("#,##0") + " seconds since waiting began.";
        }
    }
}