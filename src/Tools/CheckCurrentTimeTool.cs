using TimHanewich.AgentFramework;
using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace AIDA
{
    public class CheckCurrentTimeTool : ExecutableFunction
    {
        public CheckCurrentTimeTool()
        {
            Name = "check_current_time";
            Description = "Check the current date and time right now.";
        }

        public override Task<string> ExecuteAsync(JObject? arguments = null)
        {
            AnsiConsole.MarkupLine("[gray][italic]done[/][/]");
            return Task.FromResult("The current date and time is " + DateTime.Now.ToString());
        }
    }
}
