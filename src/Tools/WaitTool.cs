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
            Description = "Wait (idle) for a period of time.";
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

            //Wait
            AnsiConsole.Markup("[gray][italic]now waiting " + seconds.ToString("#,##0") + " seconds... [/][/]");
            TimeSpan ToSleep = new TimeSpan(0, 0, seconds);
            await Task.Delay(ToSleep);

            //Return
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return "Successfully waited " + seconds.ToString("#,##0") + " seconds. It is now " + seconds.ToString("#,##0") + " seconds since waiting began.";
        }
    }
}